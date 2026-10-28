import Foundation
import CoreGraphics
import CoreVideo
import simd

/// A camera for the person map on a simulator (LiDAR plan, phase 4).
///
/// The simulator has no camera and no LiDAR, so `-figureFake` hands the person map this instead:
/// the stand-in room (`ScanFixture`) seen from its door with the phone held upright, a person
/// walking across it, and later a person-sized box standing in it that the room's scan does not
/// have — each frame a picture, the joints ARKit would find, and the depth map the sensor would
/// give. Everything after the camera runs as it would on a phone. The unit tests use the same
/// frames, so what the UI test records is what they check.
public enum PersonMapFixture {

    /// One turn of the script, in seconds; it repeats.
    public static let period: Double = 14
    /// When the person walks across the room.
    public static let walk: ClosedRange<Double> = 1.0...5.0
    /// When the box stands in the room.
    public static let box: ClosedRange<Double> = 7.5...11.0
    /// The box: 45 × 160 × 30 cm, standing on the floor in front of the table.
    public static let boxCenter = SIMD3<Float>(0.7, 0.8, -0.2)
    public static let boxSize = SIMD3<Float>(0.45, 1.6, 0.3)

    /// The room's scan the box is not in.
    public static var baseline: ScanMesh { ScanFixture.mesh(.asScanned) }

    /// The phone at the room's near wall, chest high, held upright, looking into the room.
    public static let camera = PinholeCamera(
        transform: simd_float4x4(columns: (
            SIMD4(0, -1, 0, 0),     // the picture's x runs down the room: held upright
            SIMD4(1, 0, 0, 0),
            SIMD4(0, 0, 1, 0),      // looking along −z, into the room
            SIMD4(0, 1.2, 2.3, 1))),
        fx: 500, fy: 500, cx: 320, cy: 240, width: 640, height: 480)

    static let depthWidth = 128, depthHeight = 96

    /// A frame of the script and the picture to show for it.
    public struct Shot: @unchecked Sendable {
        // @unchecked: the picture and the buffer are never changed once made.
        public var frame: PersonMapPipeline.Frame
        /// The picture as captured, for a preview.
        public var picture: CGImage
    }

    /// The frame at `t` seconds into the script.
    public static func shot(at t: Double, date: Date = Date()) -> Shot? {
        let local = t.truncatingRemainder(dividingBy: period)
        let person = walk.contains(local) ? personJoints(at: local - walk.lowerBound) : nil
        let boxThere = box.contains(local)

        var solids: [(min: SIMD3<Float>, max: SIMD3<Float>, shade: Float)] = [
            box(center: SIMD3(0.5, 0.375, -1), size: SIMD3(1.2, 0.75, 0.8), shade: 0.35),   // the table
            box(center: SIMD3(-0.4, 0.45, -0.6), size: SIMD3(0.45, 0.9, 0.45), shade: 0.3), // the chair
        ]
        if let person {
            let root = person[FigureJoint.root.rawValue]
            solids.append(box(center: SIMD3(root.x, 0.875, root.z), size: SIMD3(0.45, 1.75, 0.25), shade: 0.55))
        }
        if boxThere { solids.append(box(center: boxCenter, size: boxSize, shade: 0.5)) }

        let depth = raycast(solids: solids)
        guard let (buffer, picture) = draw(depth: depth, person: person) else { return nil }
        // What ARKit would report: the joints inside the picture, and no body at all with none.
        let joints = person.map { joints in
            joints.map { joint -> SIMD2<Float>? in
                guard let at = camera.project(joint)?.at, (0...1).contains(at.x), (0...1).contains(at.y) else { return nil }
                return at
            }
        }.flatMap { $0.contains { $0 != nil } ? $0 : nil }
        let frame = PersonMapPipeline.Frame(time: t, date: date, image: buffer, camera: camera,
                                            joints: joints, depth: depth.frame)
        return Shot(frame: frame, picture: picture)
    }

    // MARK: - The person

    /// A person walking left to right across the room, 2.3 m from the phone, `t` seconds in.
    static func personJoints(at t: Double) -> [SIMD3<Float>] {
        let span = walk.upperBound - walk.lowerBound
        let x = Float(-1.5 + 3.0 * t / span)
        let z: Float = 0
        let swing = Float(sin(t * 2 * .pi)) * 0.12
        var joints = [SIMD3<Float>](repeating: .zero, count: FigureJoint.allCases.count)
        func set(_ joint: FigureJoint, _ dx: Float, _ y: Float, _ dz: Float = 0) {
            joints[joint.rawValue] = SIMD3(x + dx, y, z + dz)
        }
        set(.head, 0, 1.62)
        set(.rightEye, -0.03, 1.66, 0.09)
        set(.leftEye, 0.03, 1.66, 0.09)
        set(.neck, 0, 1.45)
        set(.rightShoulder, -0.2, 1.42)
        set(.leftShoulder, 0.2, 1.42)
        set(.rightElbow, -0.24, 1.15, swing / 2)
        set(.leftElbow, 0.24, 1.15, -swing / 2)
        set(.rightHand, -0.26, 0.9, swing)
        set(.leftHand, 0.26, 0.9, -swing)
        set(.root, 0, 0.95)
        set(.rightHip, -0.1, 0.92)
        set(.leftHip, 0.1, 0.92)
        set(.rightKnee, -0.1 + swing / 2, 0.5)
        set(.leftKnee, 0.1 - swing / 2, 0.5)
        set(.rightFoot, -0.1 + swing, 0.06)
        set(.leftFoot, 0.1 - swing, 0.06)
        return joints
    }

    // MARK: - Depth

    struct Depth {
        var frame: DepthFrame
        var shade: [Float]
    }

    static func box(center: SIMD3<Float>, size: SIMD3<Float>, shade: Float) -> (min: SIMD3<Float>, max: SIMD3<Float>, shade: Float) {
        (center - size / 2, center + size / 2, shade)
    }

    /// What the sensor would see along each pixel: the nearest solid, or the room's walls, floor
    /// and ceiling around it.
    static func raycast(solids: [(min: SIMD3<Float>, max: SIMD3<Float>, shade: Float)]) -> Depth {
        let room = (min: SIMD3<Float>(-2, 0, -2.5), max: SIMD3<Float>(2, 2.5, 2.5))
        let lens = camera.scaled(toWidth: depthWidth, height: depthHeight)
        let origin = camera.position
        var depths = [Float](repeating: 0, count: depthWidth * depthHeight)
        var shades = [Float](repeating: 0, count: depthWidth * depthHeight)
        for row in 0..<depthHeight {
            for column in 0..<depthWidth {
                let u = Float(column) + 0.5, v = Float(row) + 0.5
                // One meter in front of the camera along this pixel: a direction whose depth is 1.
                let ahead = lens.unproject(SIMD2(u / Float(depthWidth), v / Float(depthHeight)), depth: 1)
                let direction = ahead - origin

                // Inside the room: where the ray leaves it.
                var nearest = Float.infinity
                for axis in 0..<3 where direction[axis] != 0 {
                    let bound = direction[axis] > 0 ? room.max[axis] : room.min[axis]
                    let along = (bound - origin[axis]) / direction[axis]
                    if along > 0 { nearest = min(nearest, along) }
                }
                let hitPoint = origin + direction * nearest
                var shade: Float = hitPoint.y < 0.01 ? 0.22 : 0.16
                for solid in solids {
                    var enter: Float = 0, leave = Float.infinity
                    var misses = false
                    for axis in 0..<3 {
                        if direction[axis] == 0 {
                            if origin[axis] < solid.min[axis] || origin[axis] > solid.max[axis] { misses = true }
                            continue
                        }
                        let a = (solid.min[axis] - origin[axis]) / direction[axis]
                        let b = (solid.max[axis] - origin[axis]) / direction[axis]
                        enter = max(enter, min(a, b))
                        leave = min(leave, max(a, b))
                    }
                    if !misses, enter <= leave, enter > 0, enter < nearest {
                        nearest = enter
                        shade = solid.shade
                    }
                }
                depths[row * depthWidth + column] = nearest
                // Farther is darker, as a dim room is.
                shades[row * depthWidth + column] = shade / max(1, nearest / 2)
            }
        }
        return Depth(frame: DepthFrame(width: depthWidth, height: depthHeight, depth: depths,
                                       confidence: [UInt8](repeating: 2, count: depths.count)),
                     shade: shades)
    }

    // MARK: - The picture

    /// The picture as the camera would capture it: the depth's shading, smoothed up to 640 × 480,
    /// and the person's outline over it.
    static func draw(depth: Depth, person: [SIMD3<Float>]?) -> (CVPixelBuffer, CGImage)? {
        let width = camera.width, height = camera.height
        var made: CVPixelBuffer?
        let attributes: [CFString: Any] = [
            kCVPixelBufferCGImageCompatibilityKey: true,
            kCVPixelBufferCGBitmapContextCompatibilityKey: true,
            kCVPixelBufferIOSurfacePropertiesKey: [:] as CFDictionary,
        ]
        guard CVPixelBufferCreate(kCFAllocatorDefault, width, height, kCVPixelFormatType_32BGRA,
                                  attributes as CFDictionary, &made) == kCVReturnSuccess,
              let buffer = made else { return nil }

        CVPixelBufferLockBaseAddress(buffer, [])
        defer { CVPixelBufferUnlockBaseAddress(buffer, []) }
        guard let space = CGColorSpace(name: CGColorSpace.sRGB),
              let context = CGContext(data: CVPixelBufferGetBaseAddress(buffer), width: width, height: height,
                                      bitsPerComponent: 8, bytesPerRow: CVPixelBufferGetBytesPerRow(buffer), space: space,
                                      bitmapInfo: CGImageAlphaInfo.premultipliedFirst.rawValue | CGBitmapInfo.byteOrder32Little.rawValue)
        else { return nil }

        // The shading at the depth's size, then stretched over the whole picture.
        var small = [UInt8](repeating: 255, count: depthWidth * depthHeight * 4)
        for index in 0..<(depthWidth * depthHeight) {
            let level = UInt8(max(0, min(255, depth.shade[index] * 255)))
            small[index * 4] = level
            small[index * 4 + 1] = level
            small[index * 4 + 2] = UInt8(min(255, Int(level) + 6))
            small[index * 4 + 3] = 255
        }
        if let provider = CGDataProvider(data: Data(small) as CFData),
           let shading = CGImage(width: depthWidth, height: depthHeight, bitsPerComponent: 8, bitsPerPixel: 32,
                                 bytesPerRow: depthWidth * 4, space: space,
                                 bitmapInfo: CGBitmapInfo(rawValue: CGImageAlphaInfo.noneSkipLast.rawValue),
                                 provider: provider, decode: nil, shouldInterpolate: true, intent: .defaultIntent) {
            context.interpolationQuality = .high
            context.draw(shading, in: CGRect(x: 0, y: 0, width: width, height: height))
        }

        if let person {
            context.setStrokeColor(CGColor(gray: 0.62, alpha: 1))
            context.setLineCap(.round)
            context.setLineWidth(16)
            for (from, to) in FigureJoint.bones {
                guard let a = camera.project(person[from.rawValue])?.at, let b = camera.project(person[to.rawValue])?.at else { continue }
                context.move(to: CGPoint(x: Double(a.x) * Double(width), y: (1 - Double(a.y)) * Double(height)))
                context.addLine(to: CGPoint(x: Double(b.x) * Double(width), y: (1 - Double(b.y)) * Double(height)))
            }
            context.strokePath()
        }
        guard let picture = context.makeImage() else { return nil }
        return (buffer, picture)
    }
}
