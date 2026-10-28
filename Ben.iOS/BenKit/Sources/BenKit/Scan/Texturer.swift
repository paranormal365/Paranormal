import Foundation
import CoreGraphics
import ImageIO
import simd

/// Where a point in the room lands in a keyframe's picture (LiDAR plan, phase 2).
///
/// ARKit's camera looks down −z with +y up; a picture's rows run down. Points behind the camera,
/// or nearer than five centimeters, are not seen.
public struct KeyframeProjection: Sendable {
    public let keyframe: ScanKeyframe
    private let worldToCamera: simd_float4x4
    private let fx, fy, cx, cy: Float

    public init(_ keyframe: ScanKeyframe) {
        self.keyframe = keyframe
        worldToCamera = simd_float4x4(columnMajor: keyframe.pose).inverse
        let k = keyframe.intrinsics.count == 9 ? keyframe.intrinsics : [1, 0, 0, 0, 1, 0, 0, 0, 1]
        fx = k[0]; fy = k[4]; cx = k[6]; cy = k[7]
    }

    /// The camera's position in the room.
    public var cameraPosition: SIMD3<Float> { simd_float4x4(columnMajor: keyframe.pose).translation }

    /// The point in the camera's own frame.
    public func cameraSpace(_ p: SIMD3<Float>) -> SIMD3<Float> {
        let c = worldToCamera * SIMD4(p.x, p.y, p.z, 1)
        return SIMD3(c.x, c.y, c.z)
    }

    /// The pixel a point lands on and how far in front of the camera it is, or nil when it is not
    /// in the picture.
    public func project(_ p: SIMD3<Float>) -> (pixel: SIMD2<Float>, depth: Float)? {
        let c = cameraSpace(p)
        let depth = -c.z
        guard depth > 0.05 else { return nil }
        let pixel = SIMD2(fx * c.x / depth + cx, cy - fy * c.y / depth)
        guard pixel.x >= 0, pixel.y >= 0,
              pixel.x < Float(keyframe.imageWidth), pixel.y < Float(keyframe.imageHeight) else { return nil }
        return (pixel, depth)
    }
}

/// Paints the scan's mesh from its keyframes (LiDAR plan, phase 2, §5).
///
/// **Projective texturing**, chosen over Object Capture until phase 0 compares the two on Ben's
/// kitchen table: each triangle takes its color from the keyframe that saw it most squarely and
/// from nearest, checked against that keyframe's depth so a wall does not paint what was behind
/// it. Each keyframe a triangle chose contributes the part of its picture those triangles cover —
/// one rectangle per keyframe, packed into a single 4096-pixel atlas and scaled down together if
/// they do not fit. Triangles no keyframe saw are gray.
///
/// Pictures are decoded one at a time while the atlas is drawn: 150 decoded keyframes would be a
/// gigabyte and a half.
public enum Texturer {

    public static let atlasSize = 4096

    /// The mesh with texture coordinates — every triangle its own three corners — and the atlas
    /// as a JPEG. Nil when no keyframe saw any of it.
    public static func texture(mesh: ScanMesh, keyframes: [ScanKeyframe], folder: URL,
                               atlasSize: Int = Texturer.atlasSize) -> (mesh: ScanMesh, atlas: Data)? {
        let projections = keyframes.map(KeyframeProjection.init)
        guard !projections.isEmpty, mesh.triangleCount > 0 else { return nil }

        var depths: [Int: [Float]] = [:]
        func depthMap(_ index: Int) -> [Float]? {
            if let cached = depths[index] { return cached }
            guard let name = keyframes[index].depth,
                  let data = try? Data(contentsOf: folder.appendingPathComponent(name)),
                  let values = try? DepthCodec.decode(data) else { return nil }
            depths[index] = values
            return values
        }

        // ── Which keyframe paints each triangle ──
        var chosen = [Int](repeating: -1, count: mesh.triangleCount)
        var pixels = [[SIMD2<Float>]](repeating: [], count: mesh.triangleCount)
        for t in 0..<mesh.triangleCount {
            let a = mesh.positions[Int(mesh.indices[t * 3])]
            let b = mesh.positions[Int(mesh.indices[t * 3 + 1])]
            let c = mesh.positions[Int(mesh.indices[t * 3 + 2])]
            let normal = simd_cross(b - a, c - a)
            guard simd_length(normal) > 0 else { continue }
            let n = simd_normalize(normal)
            let center = (a + b + c) / 3

            var best: (score: Float, index: Int, pixels: [SIMD2<Float>])?
            for (index, projection) in projections.enumerated() {
                guard let pa = projection.project(a), let pb = projection.project(b), let pc = projection.project(c),
                      let pm = projection.project(center) else { continue }
                let toCamera = projection.cameraPosition - center
                let distance = simd_length(toCamera)
                // Both sides of a surface can be seen from inside a room; square-on is what counts.
                let squareness = abs(simd_dot(n, toCamera / distance))
                guard squareness > 0.2 else { continue }
                if let map = depthMap(index), let w = keyframes[index].depthWidth, let h = keyframes[index].depthHeight {
                    let x = Int(pm.pixel.x / Float(keyframes[index].imageWidth) * Float(w))
                    let y = Int(pm.pixel.y / Float(keyframes[index].imageHeight) * Float(h))
                    let seen = map[min(h - 1, max(0, y)) * w + min(w - 1, max(0, x))]
                    // Something nearer stood between the camera and this triangle.
                    if seen > 0, pm.depth > seen + 0.1 { continue }
                }
                let score = squareness / max(distance, 0.3)
                if best == nil || score > best!.score { best = (score, index, [pa.pixel, pb.pixel, pc.pixel]) }
            }
            if let best {
                chosen[t] = best.index
                pixels[t] = best.pixels
            }
        }
        guard chosen.contains(where: { $0 >= 0 }) else { return nil }

        // ── One rectangle of each keyframe's picture ──
        var rects: [Int: CGRect] = [:]
        for t in 0..<mesh.triangleCount where chosen[t] >= 0 {
            let p = pixels[t]
            let box = CGRect(x: CGFloat(p.map(\.x).min()!), y: CGFloat(p.map(\.y).min()!),
                             width: CGFloat(p.map(\.x).max()! - p.map(\.x).min()!),
                             height: CGFloat(p.map(\.y).max()! - p.map(\.y).min()!))
            rects[chosen[t]] = rects[chosen[t]].map { $0.union(box) } ?? box
        }
        for (index, rect) in rects {
            let bounds = CGRect(x: 0, y: 0, width: keyframes[index].imageWidth, height: keyframes[index].imageHeight)
            rects[index] = rect.insetBy(dx: -2, dy: -2).integral.intersection(bounds)
        }

        // ── Packed into the atlas, scaled down together until they fit ──
        let gray = CGRect(x: 0, y: 0, width: 8, height: 8)
        let order = rects.keys.sorted { rects[$0]!.height > rects[$1]!.height }
        let totalArea = rects.values.reduce(0) { $0 + $1.width * $1.height }
        var scale = min(1, sqrt(CGFloat(atlasSize * atlasSize) * 0.8 / max(totalArea, 1)))
        var placed: [Int: CGRect] = [:]
        while true {
            placed = [:]
            var x: CGFloat = gray.maxX + 2, y: CGFloat = 0, shelf: CGFloat = gray.height + 2
            var fits = true
            for index in order {
                let size = CGSize(width: (rects[index]!.width * scale).rounded(.up), height: (rects[index]!.height * scale).rounded(.up))
                if x + size.width > CGFloat(atlasSize) { x = 0; y += shelf + 2; shelf = 0 }
                if y + size.height > CGFloat(atlasSize) || size.width > CGFloat(atlasSize) { fits = false; break }
                placed[index] = CGRect(origin: CGPoint(x: x, y: y), size: size)
                x += size.width + 2
                shelf = max(shelf, size.height)
            }
            if fits { break }
            scale *= 0.9
        }

        // ── The atlas drawn, a picture at a time ──
        guard let context = CGContext(data: nil, width: atlasSize, height: atlasSize, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)
        else { return nil }
        // Rows from the top, as glTF's texture coordinates count them.
        context.translateBy(x: 0, y: CGFloat(atlasSize))
        context.scaleBy(x: 1, y: -1)
        context.setFillColor(CGColor(gray: 0.55, alpha: 1))
        context.fill(gray)
        for (index, target) in placed {
            let url = folder.appendingPathComponent(keyframes[index].file)
            guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
                  let image = CGImageSourceCreateImageAtIndex(source, 0, nil),
                  let crop = image.cropping(to: rects[index]!) else { continue }
            context.saveGState()
            // Drawn upright into a flipped context: flip it back for this one picture.
            context.translateBy(x: target.minX, y: target.maxY)
            context.scaleBy(x: 1, y: -1)
            context.draw(crop, in: CGRect(origin: .zero, size: target.size))
            context.restoreGState()
        }
        guard let atlasImage = context.makeImage(), let atlas = jpeg(atlasImage) else { return nil }

        // ── Every triangle its own corners, pointing into the atlas ──
        var textured = ScanMesh()
        var uvs: [SIMD2<Float>] = []
        let size = Float(atlasSize)
        let grayUV = SIMD2<Float>(Float(gray.midX) / size, Float(gray.midY) / size)
        for t in 0..<mesh.triangleCount {
            let corners = (0..<3).map { Int(mesh.indices[t * 3 + $0]) }
            let base = UInt32(textured.positions.count)
            for (k, vertex) in corners.enumerated() {
                textured.positions.append(mesh.positions[vertex])
                textured.normals.append(vertex < mesh.normals.count ? mesh.normals[vertex] : SIMD3(0, 1, 0))
                if chosen[t] >= 0, let source = rects[chosen[t]], let target = placed[chosen[t]] {
                    let p = pixels[t][k]
                    let u = Float(target.minX) + (p.x - Float(source.minX)) * Float(target.width / max(source.width, 1))
                    let v = Float(target.minY) + (p.y - Float(source.minY)) * Float(target.height / max(source.height, 1))
                    uvs.append(SIMD2(u / size, v / size))
                } else {
                    uvs.append(grayUV)
                }
            }
            textured.indices.append(contentsOf: [base, base + 1, base + 2])
        }
        textured.uvs = uvs
        return (textured, atlas)
    }

    static func jpeg(_ image: CGImage, quality: Double = 0.8) -> Data? {
        let data = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(data, "public.jpeg" as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(destination, image, [kCGImageDestinationLossyCompressionQuality: quality] as CFDictionary)
        return CGImageDestinationFinalize(destination) ? data as Data : nil
    }

    /// A square of a keyframe around a point in the room, about `meters` across where the point is
    /// — the picture of one change. Nil when the keyframe's picture cannot be read.
    public static func crop(_ projection: KeyframeProjection, around point: SIMD3<Float>, meters: Float = 0.8,
                            folder: URL) -> Data? {
        guard let seen = projection.project(point) else { return nil }
        let fx = projection.keyframe.intrinsics.first ?? 1000
        let side = CGFloat(min(1000, max(200, fx * meters / seen.depth)))
        let url = folder.appendingPathComponent(projection.keyframe.file)
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              let image = CGImageSourceCreateImageAtIndex(source, 0, nil) else { return nil }
        let rect = CGRect(x: CGFloat(seen.pixel.x) - side / 2, y: CGFloat(seen.pixel.y) - side / 2, width: side, height: side)
            .intersection(CGRect(x: 0, y: 0, width: image.width, height: image.height)).integral
        guard !rect.isEmpty, let crop = image.cropping(to: rect) else { return nil }
        return jpeg(crop)
    }

    /// The keyframe that saw a point best: in the picture, nearest, and most nearly in the middle.
    public static func bestView(of point: SIMD3<Float>, among projections: [KeyframeProjection]) -> KeyframeProjection? {
        projections
            .compactMap { projection -> (KeyframeProjection, Float)? in
                guard let seen = projection.project(point) else { return nil }
                let center = SIMD2(Float(projection.keyframe.imageWidth), Float(projection.keyframe.imageHeight)) / 2
                let offCenter = simd_length(seen.pixel - center) / simd_length(center)
                return (projection, seen.depth * (1 + offCenter))
            }
            .min { $0.1 < $1.1 }?.0
    }
}
