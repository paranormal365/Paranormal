import Foundation
import SwiftData

/// The field sessions on this device, and the one that is recording right now.
///
/// Lives in `AppDependencies` rather than being made per-screen like the other stores, because a
/// recording session has to outlive whatever screen started it: somebody starts a session, walks
/// to another room, checks the feed, and comes back — the session was never watching the screen.
@Observable
@MainActor
public final class FieldSessionStore {

    /// A store that could not open its database says so. It does not crash, and it does not
    /// pretend to be empty — an empty list would tell somebody their sessions were gone.
    public enum State: Equatable, Sendable {
        case ready
        case unavailable(reason: String)
    }

    public private(set) var state: State = .ready
    public private(set) var sessions: [FieldSessionSummary] = []
    /// The session currently recording, if any.
    public private(set) var activeSessionId: UUID?
    /// The live instruments of that session — nil when nothing is recording.
    public private(set) var active: ActiveFieldSession?

    /// How the store builds the instruments. Injected so a test can hand it scripted streams
    /// and the app can hand it either the real sensors or the fake ones.
    private var makeSensors: @Sendable () -> SensorSuite = { SensorSuite() }

    private let database: FieldSessionDatabase?
    public let files: SessionFileStore
    private let deviceModel: String
    private let now: @Sendable () -> Date

    private var context: ModelContext? {
        database.map { ModelContext($0.container) }
    }

    public init(database: FieldSessionDatabase?,
                files: SessionFileStore,
                deviceModel: String,
                unavailableReason: String? = nil,
                sensors: @escaping @Sendable () -> SensorSuite = { SensorSuite() },
                now: @escaping @Sendable () -> Date = Date.init) {
        self.makeSensors = sensors
        self.database = database
        self.files = files
        self.deviceModel = deviceModel
        self.now = now
        if let unavailableReason {
            state = .unavailable(reason: unavailableReason)
        } else if database == nil {
            state = .unavailable(reason: "Field sessions can't be stored on this device.")
        }
    }

    /// Builds the real store, degrading to an explained refusal rather than throwing into a
    /// crash if the database will not open.
    public static func live(sensors: @escaping @Sendable () -> SensorSuite,
                            now: @escaping @Sendable () -> Date = Date.init) -> FieldSessionStore {
        let model = DeviceModel.identifier()
        do {
            return FieldSessionStore(database: try .onDisk(),
                                     files: try .applicationSupport(),
                                     deviceModel: model, sensors: sensors, now: now)
        } catch {
            let fallback = SessionFileStore(
                root: FileManager.default.temporaryDirectory
                    .appendingPathComponent("FieldSessions", isDirectory: true))
            return FieldSessionStore(
                database: nil, files: fallback, deviceModel: model,
                unavailableReason: "Field sessions can't be stored on this device: \(error.localizedDescription)",
                sensors: sensors, now: now)
        }
    }

    // ── The running session ───────────────────────────────────────────────────

    /// Brings the instruments up for a session and starts reading them.
    public func activate(_ id: UUID, policy: SamplingPolicy = .default,
                         channels: CaptureChannels = .default) async {
        guard active?.sessionId != id else { return }
        await active?.end()

        guard let summary = summary(for: id) else { return }
        let log = ReadingLog(fileURL: files.readingLogURL(for: id))
        let sensors = makeSensors()
        let engine = FieldSessionEngine(sessionId: id, log: log, sensors: sensors,
                                        policy: policy, channels: channels, now: now)
        let session = ActiveFieldSession(sessionId: id, startedAt: summary.startedAt,
                                         engine: engine, sensors: sensors, files: files,
                                         policy: policy, channels: channels, now: now)
        active = session
        activeSessionId = id
        await session.begin()
    }

    /// Stops the instruments and records what the session ended up holding.
    public func deactivate() async {
        guard let session = active else { return }
        let id = session.sessionId

        // END FIRST, then read what the session holds. Ending closes any open recording, and
        // closing it ADDS a capture — so reading the list beforehand persisted everything
        // except the recording somebody had just been making.
        await session.end()

        let markers = session.markers
        let captures = session.captures
        let readings = session.readingCount
        active = nil

        guard let context else { return }
        if let row = try? fetch(id, in: context) {
            row.readingCount = readings
            row.markerCount = markers.count
            row.captureCount = captures.count
            row.baselineEmfMicrotesla = session.baselines.magneticMicrotesla
            row.baselineSoundDbfs = session.baselines.soundDbfs
            for capture in captures where !row.captures.contains(where: { $0.id == capture.id }) {
                let stored = FieldCapture(
                    id: capture.id, at: capture.at, kind: capture.kind,
                    relativePath: capture.relativePath, byteCount: capture.byteCount,
                    durationSeconds: capture.durationSeconds,
                    latitude: capture.latitude, longitude: capture.longitude,
                    headingDegrees: capture.headingDegrees)
                // Insert BEFORE wiring the relationship: SwiftData is unreliable about an
                // inverse set on an object the context has not yet adopted, and the row simply
                // does not come back.
                context.insert(stored)
                stored.session = row
            }
            for marker in markers where !row.markers.contains(where: { $0.id == marker.id }) {
                let stored = FieldMarker(
                    id: marker.id, at: marker.at, kind: marker.kind, note: marker.note,
                    audioFilename: marker.audioFilename,
                    audioOffsetSeconds: marker.audioOffsetSeconds,
                    emfMicrotesla: marker.magneticMicrotesla, soundDbfs: marker.soundDbfs,
                    latitude: marker.latitude, longitude: marker.longitude)
                context.insert(stored)
                stored.session = row
            }
            try? context.save()
        }
        load()
    }

    // ── Reading ───────────────────────────────────────────────────────────────

    public func load() {
        guard let context else { return }
        let descriptor = FetchDescriptor<FieldSession>(
            sortBy: [SortDescriptor(\.startedAt, order: .reverse)])
        do {
            sessions = try context.fetch(descriptor).map(FieldSessionSummary.init)
            activeSessionId = sessions.first(where: \.isRecording)?.id
        } catch {
            state = .unavailable(reason: "Your sessions couldn't be read: \(error.localizedDescription)")
        }
    }

    /// Everything a finished session needs to be replayed: what was marked, what was captured,
    /// and the base levels it was measured against.
    public func replayData(for id: UUID) -> ReplaySource? {
        guard let context, let session = try? fetch(id, in: context) else { return nil }

        let markers = session.markers
            .sorted { $0.at < $1.at }
            .map { marker in
                FieldMarkerRecord(
                    id: marker.id, at: marker.at, kind: marker.kind, note: marker.note,
                    magneticMicrotesla: marker.emfMicrotesla, soundDbfs: marker.soundDbfs,
                    latitude: marker.latitude, longitude: marker.longitude,
                    audioFilename: marker.audioFilename,
                    audioOffsetSeconds: marker.audioOffsetSeconds)
            }

        // Only timed media can sit on a timeline. A photo is an instant, not a stretch, so it
        // is a pin on the track rather than something the playhead runs through.
        let media = session.captures
            .filter { $0.kind != .photo }
            .compactMap { capture -> MediaSegment? in
                guard let duration = capture.durationSeconds, duration > 0 else { return nil }
                return MediaSegment(id: capture.id, kind: capture.kind,
                                    relativePath: capture.relativePath,
                                    startedAt: capture.at, duration: duration)
            }
            .sorted { $0.startedAt < $1.startedAt }

        let stills = session.captures
            .filter { $0.kind == .photo }
            .map { CaptureMark(id: $0.id, at: $0.at, kind: $0.kind,
                               relativePath: $0.relativePath,
                               latitude: $0.latitude, longitude: $0.longitude) }
            .sorted { $0.at < $1.at }

        return ReplaySource(
            sessionId: id,
            startedAt: session.startedAt,
            endedAt: session.endedAt,
            markers: markers,
            media: media,
            stills: stills,
            baselines: Baselines(magneticMicrotesla: session.baselineEmfMicrotesla,
                                 soundDbfs: session.baselineSoundDbfs),
            log: ReadingLog(fileURL: files.readingLogURL(for: id)))
    }

    public func summary(for id: UUID) -> FieldSessionSummary? {
        sessions.first { $0.id == id }
    }

    // ── Writing ───────────────────────────────────────────────────────────────

    /// Starts a session. The directory and log exist before the row does, so a session that
    /// appears in the list always has somewhere to write.
    @discardableResult
    public func startSession(locationLabel: String?,
                             investigationId: UUID? = nil,
                             investigationTitle: String? = nil,
                             batteryPercent: Double? = nil) throws -> UUID {
        guard let context else { throw FieldSessionError.unavailable }

        let id = UUID()
        try files.createDirectories(for: id)

        let session = FieldSession(
            id: id,
            startedAt: now(),
            locationLabel: locationLabel?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfEmpty,
            investigationId: investigationId,
            investigationTitle: investigationTitle,
            batteryPercentAtStart: batteryPercent,
            deviceModel: deviceModel)
        context.insert(session)
        try context.save()

        activeSessionId = id
        load()
        return id
    }

    public func endSession(_ id: UUID) async throws {
        if active?.sessionId == id { await deactivate() }
        guard let context else { throw FieldSessionError.unavailable }
        guard let session = try fetch(id, in: context) else { return }
        session.endedAt = now()
        session.outcome = .ended
        try context.save()
        if activeSessionId == id { activeSessionId = nil }
        load()
    }

    /// At launch: a session still marked `recording` means the app went away mid-session — the
    /// phone died, the system reclaimed memory, somebody force-quit. Its log is recovered, and
    /// it is closed as INTERRUPTED with no `endedAt`, because the honest answer to "when did it
    /// stop" is that nobody knows. Pretending it ended at relaunch would invent a fact.
    public func recoverInterruptedSessions() async {
        guard let context else { return }
        let descriptor = FetchDescriptor<FieldSession>(
            predicate: #Predicate { $0.outcomeRaw == "recording" })
        guard let stranded = try? context.fetch(descriptor), !stranded.isEmpty else {
            load(); return
        }

        for session in stranded {
            let log = ReadingLog(fileURL: files.readingLogURL(for: session.id))
            let survived = (try? await log.recover()) ?? session.readingCount
            session.readingCount = survived
            session.outcome = .interrupted
        }
        try? context.save()
        activeSessionId = nil
        load()
    }

    public func rename(_ id: UUID, locationLabel: String?) throws {
        guard let context else { throw FieldSessionError.unavailable }
        guard let session = try fetch(id, in: context) else { return }
        session.locationLabel = locationLabel?
            .trimmingCharacters(in: .whitespacesAndNewlines).nilIfEmpty
        try context.save()
        load()
    }

    /// Links a session to one of the user's investigations after the fact — the common case,
    /// since in the field you rarely stop to pick from a list.
    public func link(_ id: UUID, investigationId: UUID?, investigationTitle: String?) throws {
        guard let context else { throw FieldSessionError.unavailable }
        guard let session = try fetch(id, in: context) else { return }
        session.investigationId = investigationId
        session.investigationTitle = investigationTitle
        try context.save()
        load()
    }

    /// Deletes the row AND the files. A session that vanished from the list while its recordings
    /// stayed on disk would quietly fill the phone.
    public func delete(_ id: UUID) throws {
        guard let context else { throw FieldSessionError.unavailable }
        if let session = try fetch(id, in: context) { context.delete(session) }
        try context.save()
        try? files.delete(sessionId: id)
        if activeSessionId == id { activeSessionId = nil }
        load()
    }

    private func fetch(_ id: UUID, in context: ModelContext) throws -> FieldSession? {
        try context.fetch(FetchDescriptor<FieldSession>(
            predicate: #Predicate { $0.id == id })).first
    }
}

/// A photo, as a moment on the timeline.
public struct CaptureMark: Sendable, Equatable, Identifiable {
    public var id: UUID
    public var at: Date
    public var kind: CaptureKind
    public var relativePath: String
    public var latitude: Double?
    public var longitude: Double?

    public init(id: UUID, at: Date, kind: CaptureKind, relativePath: String,
                latitude: Double? = nil, longitude: Double? = nil) {
        self.id = id
        self.at = at
        self.kind = kind
        self.relativePath = relativePath
        self.latitude = latitude
        self.longitude = longitude
    }
}

/// Everything needed to replay one finished session.
public struct ReplaySource: Sendable {
    public var sessionId: UUID
    public var startedAt: Date
    public var endedAt: Date?
    public var markers: [FieldMarkerRecord]
    public var media: [MediaSegment]
    public var stills: [CaptureMark]
    public var baselines: Baselines
    public var log: ReadingLog
}

public enum FieldSessionError: Error, LocalizedError {
    case unavailable

    public var errorDescription: String? {
        switch self {
        case .unavailable: "Field sessions can't be stored on this device."
        }
    }
}

/// The hardware identifier — `iPhone17,1`. It is what `device.model` carries in an exported
/// bundle, because "iPhone" would not let anyone assess a reading for known quirks, and every
/// meter has some.
public enum DeviceModel {
    public static func identifier() -> String {
        var info = utsname()
        uname(&info)
        let machine = withUnsafeBytes(of: &info.machine) { raw in
            raw.prefix { $0 != 0 }.map { CChar(bitPattern: $0) }
        }
        let text = String(cString: machine + [0])
        return text.isEmpty ? "unknown" : text
    }
}

extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }
}
