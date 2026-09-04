import Foundation

/// The session that is running right now, as the screen sees it.
///
/// Owned by `FieldSessionStore` rather than by a view, because a recording session has to
/// outlive whatever screen started it: somebody starts a session, walks to another room,
/// checks the feed, comes back. The session was never watching the screen.
@Observable
@MainActor
public final class ActiveFieldSession {

    public let sessionId: UUID
    /// The session's clock. Creation time until Start is pressed, then the moment it was.
    public private(set) var startedAt: Date
    /// False while pending — the gauge runs, nothing is logged, no marks, no captures.
    public private(set) var isRecording = false

    /// The gauges. Updated at the sampling rate; nothing here is written to the log by itself.
    public private(set) var sample = LiveSample(at: .distantPast)
    public private(set) var baselines = Baselines()
    public private(set) var policy: SamplingPolicy
    public private(set) var channels: CaptureChannels
    public private(set) var markers: [FieldMarkerRecord] = []
    public private(set) var readingCount = 0
    /// Set when location was asked for and refused — the screen says so rather than showing an
    /// empty position readout forever.
    public private(set) var locationAuthorization: LocationAuthorization = .notDetermined

    /// Which room the operator says they are in. Everything recorded carries it until changed.
    public private(set) var room: String?

    /// Rooms this session has already been in, most recent first. Going back to one is then a
    /// single tap in the dark instead of typing a name again with cold hands.
    public private(set) var roomsVisited: [String] = []

    /// What has been captured into this session, newest first.
    public private(set) var captures: [CaptureRecord] = []
    /// The audio recording currently running, if any.
    public private(set) var recording: RecordingState?
    /// What this session is watching for while nobody is holding it. Nil until armed.
    public private(set) var sentry: SentryConfig?
    public var isArmed: Bool { sentry != nil }

    /// Set when a recording stopped for a reason nobody chose — a call, another app taking the
    /// microphone. Surfaced rather than swallowed: somebody who thinks they are recording and
    /// is not has lost the night.
    public private(set) var recordingProblem: String?

    public struct RecordingState: Sendable, Equatable {
        public var relativePath: String
        public var startedAt: Date
    }

    /// A file captured into the session — the value the screen lists.
    public struct CaptureRecord: Sendable, Equatable, Identifiable {
        public var id: UUID
        public var at: Date
        public var kind: CaptureKind
        public var relativePath: String
        public var byteCount: Int64
        public var durationSeconds: Double?
        public var latitude: Double?
        public var longitude: Double?
        public var headingDegrees: Double?
        public var room: String?

        public init(id: UUID = UUID(), at: Date, kind: CaptureKind, relativePath: String,
                    byteCount: Int64, durationSeconds: Double? = nil,
                    latitude: Double? = nil, longitude: Double? = nil,
                    headingDegrees: Double? = nil, room: String? = nil) {
            self.id = id
            self.at = at
            self.kind = kind
            self.relativePath = relativePath
            self.byteCount = byteCount
            self.durationSeconds = durationSeconds
            self.latitude = latitude
            self.longitude = longitude
            self.headingDegrees = headingDegrees
            self.room = room
        }
    }

    private let engine: FieldSessionEngine
    private var pump: Task<Void, Never>?
    private let sensors: SensorSuite
    private let files: SessionFileStore
    private let now: @Sendable () -> Date

    public init(sessionId: UUID, startedAt: Date, engine: FieldSessionEngine,
                sensors: SensorSuite, files: SessionFileStore,
                policy: SamplingPolicy, channels: CaptureChannels,
                now: @escaping @Sendable () -> Date = Date.init) {
        self.sessionId = sessionId
        self.startedAt = startedAt
        self.engine = engine
        self.sensors = sensors
        self.files = files
        self.policy = policy
        self.channels = channels
        self.now = now
    }

    public func begin() async {
        if channels.contains(.location), let location = sensors.location {
            locationAuthorization = await location.authorizationState()
        }
        // Audio does NOT start here any more: a recording that began before Start would begin
        // before the session's own clock, and the media clock would place it in the past.
        let stream = await engine.events()
        await engine.start()
        pump = Task { [weak self] in
            for await event in stream {
                guard let self else { return }
                switch event {
                case .sample(let sample): self.sample = sample
                case .marked(let marker):
                    // Guarded because `mark()` records its own result immediately — see there
                    // for why waiting for this stream is not safe.
                    if !self.markers.contains(where: { $0.id == marker.id }) {
                        self.markers.insert(marker, at: 0)
                    }
                case .logged(let count): self.readingCount = count
                }
            }
        }
    }

    /// Start, on the live screen. The clock begins, the log opens, and the audio recording —
    /// if the channel is on — starts now, so its first second is the session's first second.
    public func startSession(at moment: Date) async {
        guard !isRecording else { return }
        startedAt = moment
        isRecording = true
        await engine.beginLogging()
        if channels.contains(.audio) { await startRecording() }
    }

    public func end() async {
        // The recording is closed FIRST: an m4a whose moov atom never got written is not a
        // short recording, it is an unplayable file.
        await stopRecording()
        pump?.cancel()
        pump = nil
        await engine.stop()
    }

    /// Asks for location the first time a session needs it, and remembers the answer.
    public func requestLocation() async {
        guard let location = sensors.location else { return }
        locationAuthorization = await location.requestWhenInUse()
    }

    @discardableResult
    public func setBaselines() async -> Baselines {
        baselines = await engine.setBaselines()
        return baselines
    }

    public func setChannels(_ channels: CaptureChannels) async {
        let wasRecordingAudio = self.channels.contains(.audio)
        self.channels = channels
        await engine.setChannels(channels)

        if channels.contains(.location), locationAuthorization == .notDetermined {
            await requestLocation()
        }

        // The audio switch means "record sound", not "show me a meter" — so it starts and stops
        // the recording itself. Switching it off mid-session closes the file cleanly; anything
        // marked so far keeps pointing at it.
        if channels.contains(.audio), !wasRecordingAudio, recording == nil, isRecording {
            await startRecording()
        } else if !channels.contains(.audio), wasRecordingAudio {
            await stopRecording()
        }
    }

    // MARK: - Recording

    /// Starts recording sound into the session's own directory.
    public func startRecording() async {
        guard recording == nil, let recorder = sensors.recorder else { return }
        recordingProblem = nil
        do {
            let (relative, url) = try files.nextMediaPath(
                for: sessionId, kind: .audio, fileExtension: "m4a")
            try await recorder.beginRecording(to: url)
            let state = RecordingState(relativePath: relative, startedAt: now())
            recording = state
            await engine.setRecording(filename: relative, startedAt: state.startedAt)
        } catch {
            // Said out loud. A recording somebody believes is running and is not is the worst
            // outcome this feature has.
            recordingProblem = error.localizedDescription
        }
    }

    public func stopRecording() async {
        guard let state = recording, let recorder = sensors.recorder else { return }
        let duration = await recorder.endRecording()
        recording = nil
        await engine.setRecording(filename: nil, startedAt: nil)

        let url = files.fileURL(for: sessionId, relativePath: state.relativePath)
        let size = (try? FileManager.default.attributesOfItem(atPath: url.path)[.size]
                    as? NSNumber)??.int64Value ?? 0

        // A recording that produced nothing is not listed as a capture — an empty row somebody
        // taps into and finds silent is worse than saying it failed.
        guard duration > 0.4, size > 1_024 else {
            try? FileManager.default.removeItem(at: url)
            recordingProblem = "That recording came back empty — the microphone may be in use "
                             + "by something else."
            return
        }

        await engine.noteCapture(kind: .audio, relativePath: state.relativePath,
                                 durationSeconds: duration)
        captures.insert(CaptureRecord(
            at: state.startedAt, kind: .audio, relativePath: state.relativePath,
            byteCount: size, durationSeconds: duration,
            latitude: sample.position?.latitude, longitude: sample.position?.longitude,
            headingDegrees: sample.headingDegrees, room: room), at: 0)
    }

    /// Records a file the camera just handed us. The file has ALREADY been moved into the
    /// session directory by the caller — this notes what it is and where it was taken.
    public func noteCapture(kind: CaptureKind, relativePath: String, byteCount: Int64,
                            durationSeconds: Double? = nil) async {
        await engine.noteCapture(kind: kind, relativePath: relativePath,
                                 durationSeconds: durationSeconds)
        captures.insert(CaptureRecord(
            at: now(), kind: kind, relativePath: relativePath, byteCount: byteCount,
            durationSeconds: durationSeconds,
            latitude: sample.position?.latitude, longitude: sample.position?.longitude,
            headingDegrees: sample.headingDegrees, room: room), at: 0)
    }

    public func clearRecordingProblem() { recordingProblem = nil }

    // MARK: - EVP

    /// When the question currently being waited on was asked. Nil when nobody is waiting.
    public private(set) var questionOpenedAt: Date?

    /// The questions asked in this session, newest first, with how long the silence after each
    /// one ran. An unanswered wait is still open and reads as such.
    public private(set) var questions: [AskedQuestion] = []

    public struct AskedQuestion: Sendable, Equatable, Identifiable {
        public var id: UUID
        public var at: Date
        public var text: String?
        public var waitedSeconds: TimeInterval?

        public init(id: UUID, at: Date, text: String?, waitedSeconds: TimeInterval? = nil) {
            self.id = id
            self.at = at
            self.text = text
            self.waitedSeconds = waitedSeconds
        }
    }

    @discardableResult
    public func askQuestion(_ text: String?) async -> FieldMarkerRecord {
        // Asking again closes the previous wait, so the list has to catch up too.
        if let open = questionOpenedAt, let index = questions.firstIndex(where: {
            $0.at == open && $0.waitedSeconds == nil
        }) {
            questions[index].waitedSeconds = now().timeIntervalSince(open)
        }

        let marker = await engine.askQuestion(text)
        if !markers.contains(where: { $0.id == marker.id }) { markers.insert(marker, at: 0) }
        questions.insert(AskedQuestion(id: marker.id, at: marker.at, text: text), at: 0)
        questionOpenedAt = marker.at
        return marker
    }

    @discardableResult
    public func endWait() async -> FieldMarkerRecord? {
        guard let open = questionOpenedAt else { return nil }
        let marker = await engine.endWait()
        if let marker, !markers.contains(where: { $0.id == marker.id }) {
            markers.insert(marker, at: 0)
        }
        if let index = questions.firstIndex(where: { $0.at == open && $0.waitedSeconds == nil }) {
            questions[index].waitedSeconds = (marker?.at ?? now()).timeIntervalSince(open)
        }
        questionOpenedAt = nil
        return marker
    }

    // MARK: - Sentry

    /// Starts watching. Refused without a base level for whatever is being watched, because a
    /// threshold with nothing to measure against is not a threshold.
    public func arm(_ config: SentryConfig) async {
        sentry = config
        await engine.arm(config)
        // The engine only reads the accelerometer or the camera while those are armed, so the
        // streams have to be re-evaluated.
        await engine.setChannels(channels)
    }

    public func disarm() async {
        sentry = nil
        await engine.disarm()
    }

    /// Why arming would be pointless right now, in words that say what to do about it.
    public func armingProblem(for config: SentryConfig) -> String? {
        if !config.watchesAnything { return "Nothing is selected to watch for." }
        if config.watchMagnetic, baselines.magneticMicrotesla == nil {
            return "Set a base level first — the magnetic trigger measures against it."
        }
        if config.watchSound, baselines.soundDbfs == nil {
            return "Set a base level first — the sound trigger measures against it."
        }
        if config.watchSceneMotion, !channels.contains(.video) {
            return "Switch video on to watch for movement in the camera's view."
        }
        return nil
    }

    /// Moves the session to another room, and marks the moment.
    ///
    /// The mark matters as much as the label: reviewing a night later, "they went into the
    /// cellar at 01:14" is exactly the sort of thing that explains a reading.
    public func setRoom(_ room: String?) async {
        let trimmed = room?.trimmingCharacters(in: .whitespacesAndNewlines)
        let value = (trimmed?.isEmpty ?? true) ? nil : trimmed
        guard value != self.room else { return }

        self.room = value
        if let value {
            roomsVisited.removeAll { $0.caseInsensitiveCompare(value) == .orderedSame }
            roomsVisited.insert(value, at: 0)
        }
        await engine.setRoom(value)
        if let value {
            await mark(kind: .manual, note: "moved to \(value)")
        }
    }

    public func setPolicy(_ policy: SamplingPolicy) async {
        self.policy = policy
        await engine.setPolicy(policy)
    }

    /// Marks the moment, and records it here immediately.
    ///
    /// The engine also announces markers on its event stream, but that arrives whenever the pump
    /// gets to it — and somebody who marks something and then stops the session straight away
    /// would lose exactly the marker they just made. So the returned record is kept at once and
    /// the stream de-duplicates.
    @discardableResult
    public func mark(kind: MarkerKind = .manual, note: String? = nil) async -> FieldMarkerRecord {
        let record = await engine.mark(kind: kind, note: note)
        if !markers.contains(where: { $0.id == record.id }) {
            markers.insert(record, at: 0)
        }
        return record
    }

    // MARK: - Derived, for the dial

    /// How far from base, in milligauss — what the needle points at.
    public var magneticDeviationMilligauss: Double? {
        sample.magneticDeviationMilligauss(from: baselines)
    }

    /// The dial's half-span. Wide enough to show the report level with room past it, and it
    /// grows to keep a pegged needle honest rather than parking it at the stop.
    public var meterRange: Double {
        let fromPolicy = max(policy.reportAtMilligauss * 2.5, 30)
        guard let deviation = magneticDeviationMilligauss else { return fromPolicy }
        return max(fromPolicy, abs(deviation) * 1.15)
    }

    public var isReportingNow: Bool {
        guard let deviation = magneticDeviationMilligauss else { return false }
        return abs(deviation) >= policy.reportAtMilligauss
    }
}
