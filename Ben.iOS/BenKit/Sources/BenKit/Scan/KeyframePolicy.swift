import Foundation
import simd

/// When a scan keeps a picture of the room (LiDAR plan, phase 1, §5).
///
/// A keyframe is a JPEG, a depth map and the camera's pose at that moment — what phase 2 paints
/// the mesh from, and what a walkthrough shows close up. Kept whenever the camera has moved at
/// least half a metre or turned at least fifteen degrees since the last one, and only while
/// tracking is good: a frame taken while ARKit is unsure where the phone is would paint the wall
/// with the picture of a different wall. An ordinary room comes to about 150.
public struct KeyframePolicy: Sendable {

    public var minimumDistanceMeters: Float
    public var minimumTurnDegrees: Float
    /// The server keeps at most this many; past it, the scan stops taking pictures rather than
    /// making a file that would be refused.
    public var mostKeyframes: Int

    public private(set) var kept = 0
    private var last: simd_float4x4?

    public init(minimumDistanceMeters: Float = 0.5, minimumTurnDegrees: Float = 15,
                mostKeyframes: Int = ScanPackage.mostKeyframes) {
        self.minimumDistanceMeters = minimumDistanceMeters
        self.minimumTurnDegrees = minimumTurnDegrees
        self.mostKeyframes = mostKeyframes
    }

    /// Whether to keep the frame taken at `pose` (the camera's transform in the scan's frame).
    /// Keeping it is recorded, so the next answer is measured from here.
    public mutating func shouldKeep(pose: simd_float4x4, trackingIsNormal: Bool) -> Bool {
        guard trackingIsNormal, kept < mostKeyframes else { return false }
        if let last {
            let moved = simd_distance(pose.translation, last.translation)
            let turned = Self.degreesBetween(last, pose)
            guard moved >= minimumDistanceMeters || turned >= minimumTurnDegrees else { return false }
        }
        last = pose
        kept += 1
        return true
    }

    /// How far the camera turned between two poses, in degrees — the angle of the rotation that
    /// takes one orientation to the other, whichever axis it was about.
    public static func degreesBetween(_ a: simd_float4x4, _ b: simd_float4x4) -> Float {
        let qa = simd_quatf(rotationOf: a)
        let qb = simd_quatf(rotationOf: b)
        // |dot| folds q and -q, which are the same rotation, onto one answer.
        let dot = min(1, abs(simd_dot(qa.vector, qb.vector)))
        return 2 * acos(dot) * 180 / .pi
    }
}

extension simd_quatf {
    /// The rotation part of a pose.
    public init(rotationOf m: simd_float4x4) {
        self.init(simd_float3x3(SIMD3(m.columns.0.x, m.columns.0.y, m.columns.0.z),
                                SIMD3(m.columns.1.x, m.columns.1.y, m.columns.1.z),
                                SIMD3(m.columns.2.x, m.columns.2.y, m.columns.2.z)))
    }
}
