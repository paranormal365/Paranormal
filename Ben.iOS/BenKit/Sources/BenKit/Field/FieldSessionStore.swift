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
        let markers = session.markers
        let captures = session.captures
        let readings = session.readingCount
        await session.end()
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
                stored.session = row
                context.insert(stored)
            }
            for marker in markers where !row.markers.contains(where: { $0.id == marker.id }) {
                let stored = FieldMarker(
                    id: marker.id, at: marker.at, kind: marker.kind, note: marker.note,
                    audioFilename: marker.audioFilename,
                    audioOffsetSeconds: marker.audioOffsetSeconds,
                    emfMicrotesla: marker.magneticMicrotesla, soundDbfs: marker.soundDbfs,
                    latitude: marker.latitude, longitude: marker.longitude)
                stored.session = row
                context.insert(stored)
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
