import Foundation

/// A device left in a room, watching.
///
/// The point of arming is that nobody is holding the phone — so every trigger has to be worth
/// waking up for, and the record it leaves has to be enough to judge it by later. A sentry that
/// cries wolf forty times a night is a sentry nobody reviews.
public struct SentryConfig: Sendable, Equatable, Codable {

    /// Magnetic field departure from base, in milligauss.
    public var watchMagnetic: Bool
    /// Sound above base, in decibels.
    public var watchSound: Bool
    /// The device itself being moved — a bump, a knock, somebody picking it up. Different from
    /// anything in view moving, and the one that matters if a tripod gets disturbed.
    public var watchDeviceMovement: Bool
    /// Something moving in the camera's view.
    public var watchSceneMotion: Bool

    /// How hard a shove counts, in g above resting. 0.05 g is a firm nudge to a table; 0.02 is
    /// somebody walking heavily past.
    public var deviceMovementThresholdG: Double
    /// How much of the frame must change, 0…1. Frame differencing is crude by nature — a cloud
    /// crossing a window moves a lot of pixels — so this is deliberately blunt and adjustable.
    public var sceneMotionThreshold: Double

    /// Record a clip automatically when something triggers, and for how long.
    public var recordOnTrigger: Bool
    public var recordSeconds: TimeInterval

    public init(watchMagnetic: Bool = true,
                watchSound: Bool = true,
                watchDeviceMovement: Bool = false,
                watchSceneMotion: Bool = false,
                deviceMovementThresholdG: Double = 0.05,
                sceneMotionThreshold: Double = 0.08,
                recordOnTrigger: Bool = false,
                recordSeconds: TimeInterval = 20) {
        self.watchMagnetic = watchMagnetic
        self.watchSound = watchSound
        self.watchDeviceMovement = watchDeviceMovement
        self.watchSceneMotion = watchSceneMotion
        self.deviceMovementThresholdG = deviceMovementThresholdG
        self.sceneMotionThreshold = sceneMotionThreshold
        self.recordOnTrigger = recordOnTrigger
        self.recordSeconds = recordSeconds
    }

    public static let `default` = SentryConfig()

    public var watchesAnything: Bool {
        watchMagnetic || watchSound || watchDeviceMovement || watchSceneMotion
    }

    /// What the exported session says causes a record to exist. A reviewer reads this to know
    /// what a gap means, so it lists what was actually being watched.
    public func eventDescription(policy: SamplingPolicy) -> String {
        var parts: [String] = []
        if watchMagnetic {
            parts.append("magnetic field moves \(Int(policy.reportAtMilligauss)) mG from base")
        }
        if watchSound {
            parts.append("sound rises \(Int(policy.reportAtDecibels)) dB above base")
        }
        if watchDeviceMovement {
            parts.append(String(format: "the device is moved (%.2f g)", deviceMovementThresholdG))
        }
        if watchSceneMotion {
            parts.append(String(format: "%.0f%% of the camera's view changes",
                                sceneMotionThreshold * 100))
        }
        return parts.isEmpty ? "nothing is being watched" : parts.joined(separator: ", or ")
    }
}

/// How hard the device was shaken, jolted or moved.
public struct DeviceMovementSample: Sendable, Equatable {
    public var at: Date
    /// User acceleration in g, gravity already removed — so a phone sitting still reads ~0
    /// however it is propped up.
    public var magnitudeG: Double

    public init(at: Date, magnitudeG: Double) {
        self.at = at
        self.magnitudeG = magnitudeG
    }
}

/// How much of the camera's view changed since the last frame, 0…1.
public struct SceneMotionSample: Sendable, Equatable {
    public var at: Date
    public var changedFraction: Double

    public init(at: Date, changedFraction: Double) {
        self.at = at
        self.changedFraction = changedFraction
    }
}

public protocol DeviceMovementSource: Sendable {
    var isAvailable: Bool { get }
    func movements(hz: Double) -> AsyncStream<DeviceMovementSample>
}

public protocol SceneMotionSource: Sendable {
    var isAvailable: Bool { get }
    func sceneMotion() -> AsyncStream<SceneMotionSample>
}
