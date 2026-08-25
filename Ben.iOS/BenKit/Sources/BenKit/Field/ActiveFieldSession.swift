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

    private let engine: FieldSessionEngine
    private var pump: Task<Void, Never>?
    private let sensors: SensorSuite

    public init(sessionId: UUID, startedAt: Date, engine: FieldSessionEngine,
                sensors: SensorSuite, policy: SamplingPolicy, channels: CaptureChannels) {
        self.sessionId = sessionId
        self.startedAt = startedAt
        self.engine = engine
        self.sensors = sensors
        self.policy = policy
        self.channels = channels
    }

    public func begin() async {
        if channels.contains(.location), let location = sensors.location {
            locationAuthorization = await location.authorizationState()
        }
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
        self.channels = channels
        await engine.setChannels(channels)
        if channels.contains(.location), locationAuthorization == .notDetermined {
            await requestLocation()
        }
    }

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
