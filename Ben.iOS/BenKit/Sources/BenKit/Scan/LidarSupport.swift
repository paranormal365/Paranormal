import Foundation
#if canImport(ARKit) && os(iOS)
import ARKit
#endif

/// Whether this phone can scan a room (LiDAR plan, phase 1).
///
/// Ben, 10/28/2026: "Check if the phone being used has LiDAR. If it does, I would like to offer a
/// new feature." Every scanning control is shown only when this says so — absent, not disabled,
/// on a phone that cannot use it, because a greyed-out button teaches people the feature is
/// broken rather than that their phone lacks a sensor.
@MainActor
public enum LidarSupport {

    /// Set by a DEBUG build's stand-ins (`-scanFakeRoom`, `-figureFake`) so a simulator — which has
    /// no LiDAR — shows the scanning screens with a fixture in place of the sensor. Nothing in a
    /// release build sets it.
    public static var simulated = false

    /// Whether the hardware can reconstruct a room: iPhone 12 Pro and every Pro since, iPad Pro
    /// from 2020. The plain, Air and e models cannot.
    public static var hasScanner: Bool {
        #if canImport(ARKit) && os(iOS)
        ARWorldTrackingConfiguration.supportsSceneReconstruction(.mesh)
        #else
        false
        #endif
    }

    /// Whether to offer scanning at all.
    public static var isAvailable: Bool { simulated || hasScanner }

    /// What to say on a phone without the sensor, when anything is said at all.
    public static let reason = "This iPhone has no LiDAR scanner. iPhone 12 Pro and later Pro models do."
}
