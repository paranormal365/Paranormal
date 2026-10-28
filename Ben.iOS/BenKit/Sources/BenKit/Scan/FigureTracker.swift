import Foundation
import simd

/// The seventeen joints ARKit's body detection finds in a picture, in the order the person map's
/// joints file keeps them (LiDAR plan, phase 4; spec 1.2).
///
/// The phone maps ARKit's joints to these by name, so the file's order never depends on ARKit's.
public enum FigureJoint: Int, CaseIterable, Sendable {
    case head, neck
    case rightShoulder, rightElbow, rightHand
    case leftShoulder, leftElbow, leftHand
    case rightHip, rightKnee, rightFoot
    case leftHip, leftKnee, leftFoot
    case rightEye, leftEye
    case root

    /// ARKit's name for the joint in `ARSkeletonDefinition.defaultBody2DSkeletonDefinition`.
    public var arkitName: String {
        switch self {
        case .head: "head_joint"
        case .neck: "neck_1_joint"
        case .rightShoulder: "right_shoulder_1_joint"
        case .rightElbow: "right_forearm_joint"
        case .rightHand: "right_hand_joint"
        case .leftShoulder: "left_shoulder_1_joint"
        case .leftElbow: "left_forearm_joint"
        case .leftHand: "left_hand_joint"
        case .rightHip: "right_upLeg_joint"
        case .rightKnee: "right_leg_joint"
        case .rightFoot: "right_foot_joint"
        case .leftHip: "left_upLeg_joint"
        case .leftKnee: "left_leg_joint"
        case .leftFoot: "left_foot_joint"
        case .rightEye: "right_eye_joint"
        case .leftEye: "left_eye_joint"
        case .root: "root"
        }
    }

    /// The lines of the stick figure. The eyes are dots, not lines.
    public static let bones: [(FigureJoint, FigureJoint)] = [
        (.head, .neck), (.neck, .root),
        (.neck, .rightShoulder), (.rightShoulder, .rightElbow), (.rightElbow, .rightHand),
        (.neck, .leftShoulder), (.leftShoulder, .leftElbow), (.leftElbow, .leftHand),
        (.root, .rightHip), (.rightHip, .rightKnee), (.rightKnee, .rightFoot),
        (.root, .leftHip), (.leftHip, .leftKnee), (.leftKnee, .leftFoot),
    ]
}

/// One joint as the person map keeps it: where it is in the picture, how far it is from the
/// camera, and how sure the depth there was.
public struct FigurePoint: Sendable, Equatable {
    /// 0–1 across the picture, from its left.
    public var x: Float
    /// 0–1 down the picture, from its top.
    public var y: Float
    /// Meters from the camera along its view, from the depth map; nil where the sensor was not
    /// sure enough of the depth.
    public var z: Float?
    /// The share of the depth window around the joint the sensor was at least fairly sure of, 0–1.
    public var c: Float

    public init(x: Float, y: Float, z: Float?, c: Float) {
        self.x = x
        self.y = y
        self.z = z
        self.c = c
    }
}

/// A figure in one frame: its joints in `FigureJoint` order, nil where a joint was not found.
public struct FigurePose: Sendable, Equatable {
    /// Seconds, on whatever clock the frames came with.
    public var time: Double
    public var joints: [FigurePoint?]

    public init(time: Double, joints: [FigurePoint?]) {
        self.time = time
        self.joints = joints
    }

    /// How much of the figure was found with a depth the sensor trusted: the joints that were,
    /// out of all seventeen. A figure half out of the picture, or found by its outline only, is
    /// less sure than one standing in full view.
    public var confidence: Float {
        let trusted = joints.reduce(Float(0)) { sum, joint in
            guard let joint, joint.z != nil else { return sum }
            return sum + joint.c
        }
        return trusted / Float(FigureJoint.allCases.count)
    }

    public subscript(_ joint: FigureJoint) -> FigurePoint? {
        joint.rawValue < joints.count ? joints[joint.rawValue] : nil
    }
}

/// A depth map as the sensor gave it, in the camera picture's own orientation: meters, and how
/// sure the sensor was of each — 0 low, 1 medium, 2 high, ARKit's `ARConfidenceLevel`.
public struct DepthFrame: Sendable {
    public var width: Int
    public var height: Int
    /// Row by row from the top left.
    public var depth: [Float]
    public var confidence: [UInt8]

    public init(width: Int, height: Int, depth: [Float], confidence: [UInt8]) {
        self.width = width
        self.height = height
        self.depth = depth
        self.confidence = confidence
    }

    /// Medium, in ARKit's terms: the least the person map believes.
    public static let trusted: UInt8 = 1

    /// The depth at a point of the picture (0–1 from its top left): the median of the window
    /// around it, counting only what the sensor was at least fairly sure of; and the share of the
    /// window that was. A median rather than the pixel itself, because a joint's pixel is as often
    /// the wall just past an arm as the arm.
    public func lift(_ point: SIMD2<Float>, window: Int = 5) -> (depth: Float?, share: Float) {
        guard width > 0, height > 0, depth.count == width * height, confidence.count == width * height,
              point.x.isFinite, point.y.isFinite else { return (nil, 0) }
        let column = Int((point.x * Float(width)).rounded(.down))
        let row = Int((point.y * Float(height)).rounded(.down))
        let reach = window / 2
        var found: [Float] = []
        found.reserveCapacity(window * window)
        for r in (row - reach)...(row + reach) where r >= 0 && r < height {
            for c in (column - reach)...(column + reach) where c >= 0 && c < width {
                let index = r * width + c
                let value = depth[index]
                if confidence[index] >= Self.trusted, value.isFinite, value > 0 { found.append(value) }
            }
        }
        let share = Float(found.count) / Float(window * window)
        guard !found.isEmpty else { return (nil, share) }
        found.sort()
        let middle = found.count / 2
        let median = found.count % 2 == 1 ? found[middle] : (found[middle - 1] + found[middle]) / 2
        return (median, share)
    }
}

/// Five frames of each joint averaged (LiDAR plan, phase 4): enough to stop the stick figure
/// trembling, few enough that it keeps up with somebody walking.
///
/// An average of what was there, so a smoothed joint never lands outside where the last five
/// frames put it. A joint missing from a frame is averaged over the frames that had it; one
/// missing from all five is missing.
public struct FigureSmoother: Sendable {
    public static let frames = 5

    private var history: [[FigurePoint?]] = []

    public init() {}

    public mutating func smooth(_ joints: [FigurePoint?]) -> [FigurePoint?] {
        history.append(joints)
        if history.count > Self.frames { history.removeFirst(history.count - Self.frames) }

        return joints.indices.map { index in
            let seen = history.compactMap { index < $0.count ? $0[index] : nil }
            guard !seen.isEmpty else { return nil }
            let count = Float(seen.count)
            let depths = seen.compactMap(\.z)
            return FigurePoint(
                x: seen.reduce(0) { $0 + $1.x } / count,
                y: seen.reduce(0) { $0 + $1.y } / count,
                z: depths.isEmpty ? nil : depths.reduce(0, +) / Float(depths.count),
                c: seen.reduce(0) { $0 + $1.c } / count)
        }
    }

    public mutating func reset() {
        history.removeAll()
    }
}

/// From ARKit's body detection to the figure the person map draws and keeps (LiDAR plan, phase 4):
/// each joint lifted to 3D with the depth map at its pixel, then smoothed over five frames.
public struct FigureTracker: Sendable {
    /// After this long without a figure, the next one is a figure of its own, not averaged with
    /// where the last one was.
    public static let forget: Double = 0.5

    private var smoother = FigureSmoother()
    private var lastSeen: Double?

    public init() {}

    /// - Parameters:
    ///   - joints: the frame's joints in `FigureJoint` order, 0–1 from the top left of the camera
    ///     picture as the camera gave it; nil, or nothing found, when no body was detected.
    ///   - depth: the frame's depth map, in the same orientation, when the phone has one.
    public mutating func track(time: Double, joints: [SIMD2<Float>?]?, depth: DepthFrame?) -> FigurePose? {
        guard let joints, joints.contains(where: { $0 != nil }) else { return nil }
        if let lastSeen, time - lastSeen > Self.forget { smoother.reset() }
        lastSeen = time

        let lifted = joints.map { joint -> FigurePoint? in
            guard let joint, joint.x.isFinite, joint.y.isFinite else { return nil }
            let (z, share) = depth?.lift(joint) ?? (nil, 0)
            return FigurePoint(x: joint.x, y: joint.y, z: z, c: share)
        }
        return FigurePose(time: time, joints: smoother.smooth(lifted))
    }
}

/// A camera as the person map uses it: where it is in the room, and the pinhole that maps the room
/// onto its picture — ARKit's intrinsics for a picture `width` × `height`.
public struct PinholeCamera: Sendable, Equatable {
    /// Camera to room, ARKit's convention: the camera looks along its own −z, with +y up the picture.
    public var transform: simd_float4x4
    public var fx: Float, fy: Float, cx: Float, cy: Float
    public var width: Int, height: Int

    public init(transform: simd_float4x4, fx: Float, fy: Float, cx: Float, cy: Float, width: Int, height: Int) {
        self.transform = transform
        self.fx = fx
        self.fy = fy
        self.cx = cx
        self.cy = cy
        self.width = width
        self.height = height
    }

    /// From ARKit's 3 × 3 intrinsics.
    public init(transform: simd_float4x4, intrinsics k: simd_float3x3, width: Int, height: Int) {
        self.init(transform: transform, fx: k[0][0], fy: k[1][1], cx: k[2][0], cy: k[2][1], width: width, height: height)
    }

    public var position: SIMD3<Float> { transform.translation }

    /// The same camera for a picture of another size: the depth map's, say.
    public func scaled(toWidth newWidth: Int, height newHeight: Int) -> PinholeCamera {
        let sx = Float(newWidth) / Float(width), sy = Float(newHeight) / Float(height)
        return PinholeCamera(transform: transform, fx: fx * sx, fy: fy * sy, cx: cx * sx, cy: cy * sy,
                             width: newWidth, height: newHeight)
    }

    /// Where a point of the room lands in the picture, 0–1 from its top left, and how far in front
    /// of the camera it is; nil behind it.
    public func project(_ point: SIMD3<Float>) -> (at: SIMD2<Float>, depth: Float)? {
        let c = transform.inverse * SIMD4(point.x, point.y, point.z, 1)
        let depth = -c.z
        guard depth > 0.05 else { return nil }
        let pixel = SIMD2(fx * c.x / depth + cx, cy - fy * c.y / depth)
        return (SIMD2(pixel.x / Float(width), pixel.y / Float(height)), depth)
    }

    /// The point of the room seen at a place in the picture (0–1 from its top left), `depth` meters
    /// in front of the camera.
    public func unproject(_ at: SIMD2<Float>, depth: Float) -> SIMD3<Float> {
        let u = at.x * Float(width), v = at.y * Float(height)
        let local = SIMD4((u - cx) * depth / fx, (cy - v) * depth / fy, -depth, 1)
        let world = transform * local
        return SIMD3(world.x, world.y, world.z)
    }

    /// The room points a depth map saw, every `step`-th pixel each way: only what the sensor was at
    /// least fairly sure of, and nothing beyond `farthest` meters.
    public func points(from depth: DepthFrame, step: Int = 2, farthest: Float = 5) -> [SIMD3<Float>] {
        guard depth.width > 0, depth.height > 0, depth.depth.count == depth.width * depth.height else { return [] }
        var points: [SIMD3<Float>] = []
        points.reserveCapacity((depth.width / step) * (depth.height / step))
        for row in stride(from: 0, to: depth.height, by: step) {
            for column in stride(from: 0, to: depth.width, by: step) {
                let index = row * depth.width + column
                let meters = depth.depth[index]
                guard depth.confidence[index] >= DepthFrame.trusted, meters.isFinite, meters > 0, meters <= farthest else { continue }
                let at = SIMD2((Float(column) + 0.5) / Float(depth.width), (Float(row) + 0.5) / Float(depth.height))
                points.append(unproject(at, depth: meters))
            }
        }
        return points
    }
}
