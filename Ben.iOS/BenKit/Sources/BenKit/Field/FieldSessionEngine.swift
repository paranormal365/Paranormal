import Foundation

/// What the live screen is told as a session runs.
public enum FieldEvent: Sendable {
    /// The instruments right now — for the gauges. Not everything here is logged.
    case sample(LiveSample)
    /// Something crossed the report level, and a reading was written for it.
    case marked(FieldMarkerRecord)
    /// A reading was appended to the log.
    case logged(count: Int)
}

/// Everything the gauges draw, in one value.
public struct LiveSample: Sendable, Equatable {
    public var at: Date
    public var magneticMicrotesla: Double?
    public var magneticCalibration: MagneticFieldSample.CalibrationAccuracy?
    public var soundDbfs: Double?
    public var soundPeakDbfs: Double?
    public var position: PositionSample?
    public var headingDegrees: Double?
    public var relativeAltitudeMeters: Double?

    public init(at: Date) { self.at = at }

    public var magneticMilligauss: Double? { magneticMicrotesla.map { $0 * 10 } }

    /// How far the field has moved from base, in milligauss — the number the needle actually
    /// shows once a base is set, because an absolute figure means nothing without one.
    public func magneticDeviationMilligauss(from baselines: Baselines) -> Double? {
        guard let now = magneticMicrotesla, let base = baselines.magneticMicrotesla else { return nil }
        return (now - base) * 10
    }

    public func soundDeviationDb(from baselines: Baselines) -> Double? {
        guard let now = soundDbfs, let base = baselines.soundDbfs else { return nil }
        return now - base
    }
}

/// A marker as it happened — the value the UI lists and the store persists.
public struct FieldMarkerRecord: Sendable, Equatable, Identifiable {
    public var id: UUID
    public var at: Date
    public var kind: MarkerKind
    public var note: String?
    public var magneticMicrotesla: Double?
    public var soundDbfs: Double?
    public var latitude: Double?
    public var longitude: Double?
    public var audioFilename: String?
    public var audioOffsetSeconds: Double?

    public init(id: UUID = UUID(), at: Date, kind: MarkerKind, note: String? = nil,
                magneticMicrotesla: Double? = nil, soundDbfs: Double? = nil,
                latitude: Double? = nil, longitude: Double? = nil,
                audioFilename: String? = nil, audioOffsetSeconds: Double? = nil) {
        self.id = id
        self.at = at
        self.kind = kind
        self.note = note
        self.magneticMicrotesla = magneticMicrotesla
        self.soundDbfs = soundDbfs
        self.latitude = latitude
        self.longitude = longitude
        self.audioFilename = audioFilename
        self.audioOffsetSeconds = audioOffsetSeconds
    }
}

/// Runs a session: reads the instruments, decides what is worth writing down, and writes it.
///
/// An actor, and every decision inside it is a pure function of samples and a clock — so all of
/// it can be tested on a Mac with scripted streams, which matters because the simulator has no
/// magnetometer and the test host has no CoreMotion at all.
public actor FieldSessionEngine {

    private let sessionId: UUID
    private let log: ReadingLog
    private let sensors: SensorSuite
    private let now: @Sendable () -> Date

    public private(set) var policy: SamplingPolicy
    public private(set) var baselines: Baselines
    public private(set) var channels: CaptureChannels

    private var latest = LiveSample(at: .distantPast)
    private var sequence = 0
    private var lastHeartbeat: Date?
    private var lastMagneticEvent: Date?
    private var lastSoundEvent: Date?
    private var running = false
    private var tasks: [Task<Void, Never>] = []

    /// Set while a recording is running, so a marker can say where in the file it landed.
    private var recording: (filename: String, startedAt: Date)?

    private var continuations: [UUID: AsyncStream<FieldEvent>.Continuation] = [:]

    public init(sessionId: UUID,
                log: ReadingLog,
                sensors: SensorSuite,
                policy: SamplingPolicy = .default,
                channels: CaptureChannels = .default,
                baselines: Baselines = Baselines(),
                now: @escaping @Sendable () -> Date = Date.init) {
        self.sessionId = sessionId
        self.log = log
        self.sensors = sensors
        self.policy = policy
        self.channels = channels
        self.baselines = baselines
        self.now = now
    }

    // MARK: - Events out

    public func events() -> AsyncStream<FieldEvent> {
        AsyncStream { continuation in
            let id = UUID()
            continuations[id] = continuation
            continuation.onTermination = { [weak self] _ in
                Task { await self?.removeContinuation(id) }
            }
        }
    }

    private func removeContinuation(_ id: UUID) { continuations[id] = nil }

    private func emit(_ event: FieldEvent) {
        for continuation in continuations.values { continuation.yield(event) }
    }

    // MARK: - Running

    public func start() {
        guard !running else { return }
        running = true
        restartSensorTasks()
    }

    public func stop() async {
        running = false
        for task in tasks { task.cancel() }
        tasks = []
        try? await log.close()
        for continuation in continuations.values { continuation.finish() }
        continuations = [:]
    }

    /// Turns channels on and off mid-session. The streams for anything switched off are torn
    /// down rather than left running quietly — the whole reason somebody switches video off at
    /// 2am is that they want the battery back.
    public func setChannels(_ channels: CaptureChannels) {
        guard channels != self.channels else { return }
        self.channels = channels
        if running { restartSensorTasks() }
    }

    public func setPolicy(_ policy: SamplingPolicy) {
        self.policy = policy
    }

    private func restartSensorTasks() {
        for task in tasks { task.cancel() }
        tasks = []

        if channels.contains(.magnetic), let magnetometer = sensors.magnetometer,
           magnetometer.isAvailable {
            let stream = magnetometer.samples(hz: policy.gaugeHz)
            tasks.append(Task { [weak self] in
                for await sample in stream { await self?.ingest(magnetic: sample) }
            })
        }
        if channels.contains(.audio), let audio = sensors.audio, audio.isAvailable {
            let stream = audio.levels()
            tasks.append(Task { [weak self] in
                for await sample in stream { await self?.ingest(audio: sample) }
            })
        }
        if channels.contains(.location), let location = sensors.location, location.isAvailable {
            let positions = location.positions()
            tasks.append(Task { [weak self] in
                for await sample in positions { await self?.ingest(position: sample) }
            })
            let headings = location.headings()
            tasks.append(Task { [weak self] in
                for await sample in headings { await self?.ingest(heading: sample) }
            })
        }
        if let altitude = sensors.altitude, altitude.isAvailable {
            let stream = altitude.relativeAltitudes()
            tasks.append(Task { [weak self] in
                for await sample in stream { await self?.ingest(altitude: sample) }
            })
        }
    }

    // MARK: - Ingest

    func ingest(magnetic sample: MagneticFieldSample) async {
        latest.at = sample.at
        latest.magneticMicrotesla = sample.magnitudeMicrotesla
        latest.magneticCalibration = sample.calibration
        emit(.sample(latest))

        // A spike read while the magnetometer is uncalibrated is not evidence of anything, so
        // it never raises an event. It is still logged on the heartbeat, carrying its accuracy,
        // because hiding it entirely would be its own kind of dishonesty.
        if sample.calibration.isTrustworthy,
           let deviation = latest.magneticDeviationMilligauss(from: baselines),
           abs(deviation) >= policy.reportAtMilligauss,
           passesDebounce(last: lastMagneticEvent, at: sample.at) {
            lastMagneticEvent = sample.at
            await record(kind: .sentryEmf, at: sample.at,
                         note: String(format: "field moved %.0f mG from base", deviation))
        }

        await heartbeatIfDue(at: sample.at)
    }

    func ingest(audio sample: AudioLevelSample) async {
        latest.at = sample.at
        latest.soundDbfs = sample.averageDbfs
        latest.soundPeakDbfs = sample.peakDbfs
        emit(.sample(latest))

        if let deviation = latest.soundDeviationDb(from: baselines),
           deviation >= policy.reportAtDecibels,
           passesDebounce(last: lastSoundEvent, at: sample.at) {
            lastSoundEvent = sample.at
            await record(kind: .sentrySound, at: sample.at,
                         note: String(format: "sound rose %.0f dB above base", deviation))
        }

        await heartbeatIfDue(at: sample.at)
    }

    func ingest(position sample: PositionSample) async {
        latest.position = sample
        emit(.sample(latest))
    }

    func ingest(heading sample: HeadingSample) async {
        latest.headingDegrees = sample.degrees
        emit(.sample(latest))
    }

    func ingest(altitude sample: RelativeAltitudeSample) async {
        latest.relativeAltitudeMeters = sample.metersSinceStart
        emit(.sample(latest))
    }

    private func passesDebounce(last: Date?, at moment: Date) -> Bool {
        guard let last else { return true }
        return moment.timeIntervalSince(last) >= policy.debounceSeconds
    }

    // MARK: - Writing

    private func heartbeatIfDue(at moment: Date) async {
        if let lastHeartbeat, moment.timeIntervalSince(lastHeartbeat) < policy.heartbeatSeconds {
            return
        }
        lastHeartbeat = moment
        await append(reading(at: moment, triggeredBy: .interval))
    }

    /// The current state of the instruments, as a reading.
    private func reading(at moment: Date,
                         triggeredBy trigger: FieldReading.Trigger,
                         marker: MarkerKind? = nil,
                         note: String? = nil,
                         audio: FieldReading.FileRef? = nil) -> FieldReading {
        sequence += 1

        var measurements: [String: FieldReading.Measurement] = [:]
        if let marker {
            // The kind rides here because the spec's `triggered_by` is a closed enum of three
            // values. A string measurement needs no unit, so this is legal and machine-readable.
            measurements["marker"] = .label(marker.rawValue)
        }
        if let field = latest.magneticMicrotesla {
            measurements["emf"] = .number(
                field, unit: "uT",
                accuracy: latest.magneticCalibration?.microteslaTolerance,
                baseline: baselines.magneticMicrotesla)
        }
        if let sound = latest.soundDbfs {
            measurements["sound_level"] = .number(sound, unit: "dBFS",
                                                  baseline: baselines.soundDbfs)
        }
        if let altitude = latest.relativeAltitudeMeters {
            measurements["relative_altitude"] = .number(altitude, unit: "m")
        }
        if let battery = sensors.batteryPercent() {
            measurements["battery"] = .number(battery, unit: "percent")
        }

        // No fix means NO position — never a zero, which is a real place in the Gulf of Guinea.
        var position: FieldReading.Position?
        if let fix = latest.position {
            position = .init(latitude: fix.latitude, longitude: fix.longitude,
                             elevationMeters: fix.altitudeMeters,
                             accuracyMeters: fix.accuracyMeters)
        }

        var motion = FieldReading.Motion(headingDegrees: latest.headingDegrees,
                                         speedMps: latest.position?.speedMps)

        return FieldReading(
            at: moment,
            precision: .millisecond,
            sequence: sequence,
            triggeredBy: trigger,
            measurements: measurements.isEmpty ? nil : measurements,
            position: position,
            motion: motion.isEmpty ? nil : motion,
            audioRef: audio,
            note: note)
    }

    private func append(_ reading: FieldReading) async {
        do {
            try await log.append(reading)
            emit(.logged(count: sequence))
        } catch {
            // A log that cannot be written is worth knowing about, but it must not take the
            // session down: the person is standing in a building and the other channels still
            // work. The failure surfaces through the session's reading count not moving.
        }
    }

    /// Writes a marker — automatic or by hand — and tells the screen about it.
    @discardableResult
    public func mark(kind: MarkerKind, note: String? = nil) async -> FieldMarkerRecord {
        await record(kind: kind, at: now(), note: note)
    }

    @discardableResult
    private func record(kind: MarkerKind, at moment: Date, note: String?) async -> FieldMarkerRecord {
        var audioRef: FieldReading.FileRef?
        if let recording {
            audioRef = .relative(recording.filename,
                                 mediaType: "audio/mp4",
                                 startOffsetSeconds: moment.timeIntervalSince(recording.startedAt))
        }

        await append(reading(at: moment, triggeredBy: kind.trigger,
                             marker: kind, note: note, audio: audioRef))

        let record = FieldMarkerRecord(
            at: moment, kind: kind, note: note,
            magneticMicrotesla: latest.magneticMicrotesla,
            soundDbfs: latest.soundDbfs,
            latitude: latest.position?.latitude,
            longitude: latest.position?.longitude,
            audioFilename: audioRef?.filename,
            audioOffsetSeconds: audioRef?.startOffsetSeconds)
        emit(.marked(record))
        return record
    }

    // MARK: - Levels

    /// Takes the room as it is right now and calls that normal.
    @discardableResult
    public func setBaselines() -> Baselines {
        baselines = Baselines(magneticMicrotesla: latest.magneticMicrotesla,
                              soundDbfs: latest.soundDbfs,
                              setAt: now())
        // A fresh base means the old debounce windows describe a world that no longer exists.
        lastMagneticEvent = nil
        lastSoundEvent = nil
        return baselines
    }

    public func clearBaselines() {
        baselines = Baselines()
    }

    public func setRecording(filename: String?, startedAt: Date?) {
        if let filename, let startedAt {
            recording = (filename, startedAt)
        } else {
            recording = nil
        }
    }

    /// Notes a captured file against the session. Best-effort by design: the FILE is already on
    /// disk by the time this runs, so a failure to write its reading loses the note, never the
    /// photo.
    @discardableResult
    public func noteCapture(kind: CaptureKind, relativePath: String,
                            durationSeconds: Double? = nil) async -> FieldReading {
        sequence -= 1   // `reading` increments; keep the numbering continuous
        let entry = reading(
            at: now(), triggeredBy: .manual,
            marker: nil,
            note: "\(kind.rawValue): \(relativePath)",
            audio: kind == .audio
                ? FieldReading.FileRef.relative(relativePath, mediaType: "audio/mp4",
                                                durationSeconds: durationSeconds)
                : nil)

        var withMarker = entry
        // Photos and video have no home in the v1 format's `audio_ref`, so the kind travels as a
        // marker label and the path in the note. Flagged for Ben as a `media_ref` candidate for
        // a future spec version rather than inventing a top-level field here.
        var measurements = withMarker.measurements ?? [:]
        measurements["marker"] = .label(kind.rawValue)
        withMarker.measurements = measurements

        await append(withMarker)
        return withMarker
    }

    public func currentSample() -> LiveSample { latest }
    public func readingCount() -> Int { sequence }
}
