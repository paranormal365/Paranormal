import Foundation
import simd

/// Where somebody stands and looks inside a scanned room (LiDAR plan, phase 1).
///
/// The viewer's camera, kept as numbers so the rules can be tested without RealityKit: eye height,
/// which way the person faces (yaw), how far up or down they look (pitch), and a walk that stays
/// inside the room. Look-around feeds yaw and pitch from the phone's own orientation; the joystick
/// walks.
public struct ScanViewerCamera: Sendable, Equatable {

    /// A standing person's eyes, in meters above the floor.
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
        let center = (roomMin + roomMax) / 2
        position = SIMD3(center.x, roomMin.y + Self.eyeHeight, center.z)
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

    /// Steps `forward` meters the way the person faces and `right` meters to their right, on the
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

    // MARK: Walking by walking (phase 2)

    /// Which way an ARKit camera faces: yaw counter-clockwise from −z seen from above, pitch up.
    public static func facing(pose: simd_float4x4) -> (yaw: Float, pitch: Float) {
        let forward = -SIMD3(pose.columns.2.x, pose.columns.2.y, pose.columns.2.z)
        let pitch = asin(min(1, max(-1, forward.y)))
        let yaw = atan2(-forward.x, -forward.z)
        return (yaw, pitch)
    }

    /// Where walking began: the phone's pose in its own room, and the camera's in the scan's.
    public struct WalkStart: Sendable, Equatable {
        public var devicePosition: SIMD3<Float>
        public var deviceYaw: Float
        public var cameraPosition: SIMD3<Float>
        public var cameraYaw: Float

        public init(device pose: simd_float4x4, camera: ScanViewerCamera) {
            devicePosition = pose.translation
            deviceYaw = ScanViewerCamera.facing(pose: pose).yaw
            cameraPosition = camera.position
            cameraYaw = camera.yaw
        }
    }

    /// Moves the camera as the phone moved since walking began: a step forward in the person's
    /// own room is a step forward in the scan, a turn is a turn. A step that would put the camera
    /// in a wall or out of the room is not taken; turning still is.
    public mutating func follow(device pose: simd_float4x4, from start: WalkStart,
                                blocked: (SIMD3<Float>) -> Bool = { _ in false }) {
        let facing = Self.facing(pose: pose)
        let turn = start.cameraYaw - start.deviceYaw
        let moved = pose.translation - start.devicePosition
        let rotated = simd_quatf(angle: turn, axis: SIMD3(0, 1, 0)).act(moved)
        let target = SIMD3(start.cameraPosition.x + rotated.x, start.cameraPosition.y + rotated.y, start.cameraPosition.z + rotated.z)

        face(yaw: facing.yaw + turn, pitch: facing.pitch)
        let inside = clamped(target)
        if inside.x == target.x, inside.z == target.z, !blocked(target) {
            position = target
        }
    }

    /// The keyframe taken from about where the camera stands and facing about the same way — a
    /// close-up of what the person is looking at, from the scan's own picture.
    public func closestKeyframe(_ keyframes: [ScanKeyframe], within meters: Float = 0.4,
                                facingWithin degrees: Float = 30) -> Int? {
        let look = forward
        return keyframes.indices
            .compactMap { index -> (Int, Float)? in
                let pose = simd_float4x4(columnMajor: keyframes[index].pose)
                let distance = simd_distance(pose.translation, position)
                guard distance <= meters else { return nil }
                let theirs = -SIMD3(pose.columns.2.x, pose.columns.2.y, pose.columns.2.z)
                let angle = acos(min(1, max(-1, simd_dot(simd_normalize(theirs), look)))) * 180 / .pi
                return angle <= degrees ? (index, distance) : nil
            }
            .min { $0.1 < $1.1 }?.0
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

/// What a walk through a scan must not pass through: the room's surfaces, as cells.
public struct ScanOccupancy: Sendable {
    private let voxels: Voxels

    public init(_ mesh: ScanMesh) {
        voxels = Voxels(mesh)
    }

    /// Whether a person whose eyes are here would be standing in something — a wall, a table —
    /// anywhere from knee height to just above the eyes, within fifteen centimeters.
    public func blocks(eye: SIMD3<Float>) -> Bool {
        let size = RoomComparer.voxelMeters
        let column = SIMD3<Int32>(Int32((eye.x / size).rounded(.down)), 0, Int32((eye.z / size).rounded(.down)))
        let top = Int32(((eye.y + 0.1) / size).rounded(.down))
        let knee = Int32(((eye.y - ScanViewerCamera.eyeHeight + 0.5) / size).rounded(.down))
        guard knee <= top else { return false }
        for y in knee...top {
            for dx: Int32 in -3...3 {
                for dz: Int32 in -3...3 where voxels.cells.contains(SIMD3(column.x + dx, y, column.z + dz)) {
                    return true
                }
            }
        }
        return false
    }
}
