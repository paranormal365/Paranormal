import Foundation

/// How often a session samples, and what counts as something happening.
///
/// The trade-off this encodes: the gauge wants to move smoothly, and the LOG wants to stay small
/// enough to survive five hours on a phone battery. So the needle sees every sample and the log
/// sees a heartbeat plus anything that crossed a line — which is the spec's `hybrid` trigger, and
/// the reason a gap in the log is interpretable rather than ambiguous.
public struct SamplingPolicy: Sendable, Equatable {

    /// How fast the gauges update. Fast enough to look alive, slow enough not to cook the CPU.
    public var gaugeHz: Double

    /// How often a reading is written no matter what. Under `hybrid` this is what makes silence
    /// distinguishable from a dead device.
    public var heartbeatSeconds: TimeInterval

    /// How far the magnetic field must move from the base level, in MILLIGAUSS, to be worth
    /// recording. The number a person sets as "report at".
    public var reportAtMilligauss: Double

    /// How far sound must rise above the base level, in decibels, to be worth recording.
    public var reportAtDecibels: Double

    /// The quiet period after an event before another can be recorded. Without it, one door
    /// slamming writes forty records and a reviewer cannot tell it was one event.
    public var debounceSeconds: TimeInterval

    public init(gaugeHz: Double = 10,
                heartbeatSeconds: TimeInterval = 2,
                reportAtMilligauss: Double = 20,
                reportAtDecibels: Double = 12,
                debounceSeconds: TimeInterval = 3) {
        self.gaugeHz = gaugeHz
        self.heartbeatSeconds = heartbeatSeconds
        self.reportAtMilligauss = reportAtMilligauss
        self.reportAtDecibels = reportAtDecibels
        self.debounceSeconds = debounceSeconds
    }

    public static let `default` = SamplingPolicy()

    /// What the exported envelope says about how readings came to exist. Written in the
    /// operator's units, because a reviewer reads this sentence, not the code.
    public var triggerDescription: String {
        "magnetic field moves \(Int(reportAtMilligauss)) mG from base, "
        + "or sound rises \(Int(reportAtDecibels)) dB above base"
    }

    public var trigger: DeviceDataEnvelope.Trigger {
        .init(mode: .hybrid,
              intervalSeconds: heartbeatSeconds,
              eventDescription: triggerDescription,
              debounceSeconds: debounceSeconds)
    }
}

/// The reference levels a session measures against.
///
/// A field reading is meaningless in absolute terms — the Earth alone is around 500 mG, and a
/// building's wiring moves that around constantly. What matters is the departure from whatever
/// this room reads when nothing is happening. Setting a base is the act that turns a number into
/// a measurement.
public struct Baselines: Sendable, Equatable, Codable {
    /// Microtesla — the unit the export carries.
    public var magneticMicrotesla: Double?
    /// dBFS.
    public var soundDbfs: Double?
    public var setAt: Date?

    public init(magneticMicrotesla: Double? = nil, soundDbfs: Double? = nil, setAt: Date? = nil) {
        self.magneticMicrotesla = magneticMicrotesla
        self.soundDbfs = soundDbfs
        self.setAt = setAt
    }

    public var magneticMilligauss: Double? { magneticMicrotesla.map { $0 * 10 } }
    public var isSet: Bool { magneticMicrotesla != nil || soundDbfs != nil }
}
