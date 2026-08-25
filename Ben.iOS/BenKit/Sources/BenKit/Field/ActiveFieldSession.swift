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
    public let startedAt: Date

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

    /// What has been captured into this session, newest first.
    public private(set) var captures: [CaptureRecord] = []
    /// The audio recording currently running, if any.
    public private(set) var recording: RecordingState?
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

        public init(id: UUID = UUID(), at: Date, kind: CaptureKind, relativePath: String,
                    byteCount: Int64, durationSeconds: Double? = nil,
                    latitude: Double? = nil, longitude: Double? = nil,
                    headingDegrees: Double? = nil) {
            self.id = id
            self.at = at
            self.kind = kind
            self.relativePath = relativePath
            self.byteCount = byteCount
            self.durationSeconds = durationSeconds
            self.latitude = latitude
            self.longitude = longitude
            self.headingDegrees = headingDegrees
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
        if channels.contains(.audio) { await startRecording() }
        let stream = await engine.events()
        await engine.start()
        pump = Task { [weak self] in
            for await event in stream {
                guard let self else { return }
                switch event {
                case .sample(let sample): self.sample = sample
                case .marked(let marker): self.markers.insert(marker, at: 0)
                case .logged(let count): self.readingCount = count
                }
            }
        }
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
        if channels.contains(.audio), !wasRecordingAudio, recording == nil {
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
            headingDegrees: sample.headingDegrees), at: 0)
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
            headingDegrees: sample.headingDegrees), at: 0)
    }

    public func clearRecordingProblem() { recordingProblem = nil }

    public func setPolicy(_ policy: SamplingPolicy) async {
        self.policy = policy
        await engine.setPolicy(policy)
    }

    @discardableResult
    public func mark(kind: MarkerKind = .manual, note: String? = nil) async -> FieldMarkerRecord {
        await engine.mark(kind: kind, note: note)
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
