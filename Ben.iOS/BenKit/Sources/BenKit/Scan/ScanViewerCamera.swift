import Foundation
import simd

/// Where somebody stands and looks inside a scanned room (LiDAR plan, phase 1).
///
/// The viewer's camera, kept as numbers so the rules can be tested without RealityKit: eye height,
/// which way the person faces (yaw), how far up or down they look (pitch), and a walk that stays
/// inside the room. Look-around feeds yaw and pitch from the phone's own orientation; the joystick
/// walks.
public struct ScanViewerCamera: Sendable, Equatable {

    /// A standing person's eyes, in metres above the floor.
    public static let eyeHeight: Float = 1.6

    /// How far up or down a person can look before the room turns upside down.
    public static let mostPitch: Float = 80 * .pi / 180

    /// Kept this far from the walls, so a step never puts the camera inside one.
    public static let wallMargin: Float = 0.25

    public var position: SIMD3<Float>
    /// Radians about the vertical; 0 faces −z, ARKit's "forward" when the scan began.
    public var yaw: Float
    /// Radians; positive looks up.
    public var pitch: Float
    /// The room's extent on the floor, to stay inside.
    public var floorMin: SIMD2<Float>
    public var floorMax: SIMD2<Float>

    /// Stands in the middle of the room at eye height, facing the way the scan began.
    public init(roomMin: SIMD3<Float>, roomMax: SIMD3<Float>) {
        let centre = (roomMin + roomMax) / 2
        position = SIMD3(centre.x, roomMin.y + Self.eyeHeight, centre.z)
        yaw = 0
        pitch = 0
        floorMin = SIMD2(roomMin.x, roomMin.z)
        floorMax = SIMD2(roomMax.x, roomMax.z)
        position = clamped(position)
    }

    /// Turns the head by a drag or a tilt.
    public mutating func look(yawBy deltaYaw: Float, pitchBy deltaPitch: Float) {
        yaw = Self.wrapped(yaw + deltaYaw)
        pitch = min(Self.mostPitch, max(-Self.mostPitch, pitch + deltaPitch))
    }

    /// Faces exactly this way — the phone's own orientation, in look-around.
    public mutating func face(yaw newYaw: Float, pitch newPitch: Float) {
        yaw = Self.wrapped(newYaw)
        pitch = min(Self.mostPitch, max(-Self.mostPitch, newPitch))
    }

    /// Steps `forward` metres the way the person faces and `right` metres to their right, on the
    /// floor — looking up does not make anybody walk into the ceiling — and never out of the room.
    public mutating func walk(forward: Float, right: Float) {
        let facing = SIMD2<Float>(-sin(yaw), -cos(yaw))
        let rightHand = SIMD2<Float>(cos(yaw), -sin(yaw))
        let step = facing * forward + rightHand * right
        position = clamped(position + SIMD3(step.x, 0, step.y))
    }

    /// The camera's transform: where it is, turned by yaw then tilted by pitch.
    public var transform: simd_float4x4 {
        let turn = simd_float4x4(simd_quatf(angle: yaw, axis: SIMD3(0, 1, 0)))
        let tilt = simd_float4x4(simd_quatf(angle: pitch, axis: SIMD3(1, 0, 0)))
        var m = turn * tilt
        m.columns.3 = SIMD4(position.x, position.y, position.z, 1)
        return m
    }

    /// The way the camera looks, as a unit vector.
    public var forward: SIMD3<Float> {
        let f = transform * SIMD4<Float>(0, 0, -1, 0)
        return simd_normalize(SIMD3(f.x, f.y, f.z))
    }

    /// Which way a phone's back camera points, from its attitude in a frame whose z is up (CoreMotion's
    /// `xArbitraryZVertical`): yaw counter-clockwise seen from above, pitch up from the horizon.
    /// Look-around turns the room by the change in this since it was switched on.
    public static func facing(attitude q: simd_quatd) -> (yaw: Float, pitch: Float) {
        let back = q.act(SIMD3<Double>(0, 0, -1))
        let pitch = asin(min(1, max(-1, back.z)))
        let yaw = atan2(back.y, back.x)
        return (Float(yaw), Float(pitch))
    }

    private func clamped(_ p: SIMD3<Float>) -> SIMD3<Float> {
        let low = floorMin + Self.wallMargin
        let high = floorMax - Self.wallMargin
        // A room narrower than two margins keeps the camera on its middle line.
        let x = low.x <= high.x ? min(high.x, max(low.x, p.x)) : (floorMin.x + floorMax.x) / 2
        let z = low.y <= high.y ? min(high.y, max(low.y, p.z)) : (floorMin.y + floorMax.y) / 2
        return SIMD3(x, p.y, z)
    }

    private static func wrapped(_ angle: Float) -> Float {
        var a = angle.truncatingRemainder(dividingBy: 2 * .pi)
        if a > .pi { a -= 2 * .pi }
        if a < -.pi { a += 2 * .pi }
        return a
    }
}
