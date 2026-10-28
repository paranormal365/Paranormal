import Foundation
import CoreGraphics
import CoreImage
import CoreVideo
import ImageIO
import UniformTypeIdentifiers
import simd

/// The figure and the shapes drawn into pictures: a sighting's picture, and the copy of a video
/// with the figure burned in (LiDAR plan, phase 4). The screens draw their own with SwiftUI, in the
/// same colors.
public enum FigureDrawing {
    /// The app's ecto violet, as it is in the dark.
    public static let figureColor = CGColor(srgbRed: 0.486, green: 0.361, blue: 1.0, alpha: 1)
    /// The app's warning amber: a shape is outlined, never jointed.
    public static let shapeColor = CGColor(srgbRed: 0.949, green: 0.690, blue: 0.216, alpha: 1)

    /// Draws the stick figure into a context whose origin is at the bottom left, as Core Graphics'
    /// own is. Points are 0–1 from the picture's top left.
    public static func drawFigure(_ joints: [FigurePoint?], in context: CGContext, width: Double, height: Double) {
        func point(_ joint: FigureJoint) -> CGPoint? {
            guard joint.rawValue < joints.count, let p = joints[joint.rawValue] else { return nil }
            return CGPoint(x: Double(p.x) * width, y: (1 - Double(p.y)) * height)
        }
        let line = max(2, min(width, height) / 90)
        context.saveGState()
        context.setLineCap(.round)
        context.setLineJoin(.round)
        context.setLineWidth(line)
        context.setStrokeColor(figureColor)
        context.setFillColor(figureColor)
        for (from, to) in FigureJoint.bones {
            guard let a = point(from), let b = point(to) else { continue }
            context.move(to: a)
            context.addLine(to: b)
        }
        context.strokePath()
        for joint in FigureJoint.allCases {
            guard let p = point(joint) else { continue }
            let radius = joint == .head ? line * 2.5 : line
            context.fillEllipse(in: CGRect(x: p.x - radius, y: p.y - radius, width: radius * 2, height: radius * 2))
        }
        context.restoreGState()
    }

    /// Outlines a shape: a rectangle 0–1 from the picture's top left.
    public static func drawShape(_ rect: CGRect, in context: CGContext, width: Double, height: Double) {
        let line = max(2, min(width, height) / 120)
        context.saveGState()
        context.setStrokeColor(shapeColor)
        context.setLineWidth(line)
        context.setLineDash(phase: 0, lengths: [line * 3, line * 2])
        context.stroke(CGRect(x: rect.minX * width, y: (1 - rect.maxY) * height,
                              width: rect.width * width, height: rect.height * height))
        context.restoreGState()
    }

    private static let images = CIContext(options: [.cacheIntermediates: false])

    /// A frame turned upright and made small, with the figure and any shapes drawn on it, as a JPEG.
    ///
    /// - Parameters:
    ///   - figure: joints 0–1 in the picture as captured.
    ///   - shapes: rectangles 0–1 in the picture as captured.
    public static func picture(of buffer: CVPixelBuffer, turn: PictureTurn, figure: [FigurePoint?]?,
                               shapes: [CGRect] = [], longestSide: Double = 480) -> Data? {
        let width = Double(CVPixelBufferGetWidth(buffer)), height = Double(CVPixelBufferGetHeight(buffer))
        guard width > 0, height > 0 else { return nil }
        let scale = longestSide / max(width, height)
        let upright = CIImage(cvPixelBuffer: buffer)
            .oriented(orientation(turn))
            .transformed(by: CGAffineTransform(scaleX: scale, y: scale))
        let extent = upright.extent.integral
        guard let image = images.createCGImage(upright, from: extent),
              let context = CGContext(data: nil, width: image.width, height: image.height, bitsPerComponent: 8,
                                      bytesPerRow: 0, space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                      bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue)
        else { return nil }
        let w = Double(image.width), h = Double(image.height)
        context.draw(image, in: CGRect(x: 0, y: 0, width: w, height: h))
        for shape in shapes { drawShape(uprightRect(shape, turn), in: context, width: w, height: h) }
        if let figure {
            drawFigure(figure.map { joint in
                guard let joint else { return nil }
                let p = turn.upright(SIMD2(joint.x, joint.y))
                return FigurePoint(x: p.x, y: p.y, z: joint.z, c: joint.c)
            }, in: context, width: w, height: h)
        }
        guard let drawn = context.makeImage() else { return nil }
        return jpeg(drawn)
    }

    /// A rectangle of the captured picture, in the upright one.
    public static func uprightRect(_ rect: CGRect, _ turn: PictureTurn) -> CGRect {
        let a = turn.upright(SIMD2(Float(rect.minX), Float(rect.minY)))
        let b = turn.upright(SIMD2(Float(rect.maxX), Float(rect.maxY)))
        return CGRect(x: Double(min(a.x, b.x)), y: Double(min(a.y, b.y)),
                      width: Double(abs(b.x - a.x)), height: Double(abs(b.y - a.y)))
    }

    /// The image orientation that stands a captured picture upright.
    public static func orientation(_ turn: PictureTurn) -> CGImagePropertyOrientation {
        switch turn {
        case .none: .up
        case .clockwise: .right
        case .upsideDown: .down
        case .counterclockwise: .left
        }
    }

    public static func jpeg(_ image: CGImage, quality: Double = 0.8) -> Data? {
        let data = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(data, UTType.jpeg.identifier as CFString, 1, nil) else { return nil }
        CGImageDestinationAddImage(destination, image, [kCGImageDestinationLossyCompressionQuality: quality] as CFDictionary)
        return CGImageDestinationFinalize(destination) ? data as Data : nil
    }

    /// A whole frame, upright, as a JPEG: a photo taken while the person map has the camera.
    public static func still(of buffer: CVPixelBuffer, turn: PictureTurn) -> Data? {
        let upright = CIImage(cvPixelBuffer: buffer).oriented(orientation(turn))
        guard let image = images.createCGImage(upright, from: upright.extent.integral) else { return nil }
        return jpeg(image, quality: 0.9)
    }
}

/// The brightness of a frame on a coarse grid, for Watch for Motion while the person map has the
/// camera: the same 32 × 24 cells the session's own camera compares.
public enum LumaGrid {
    public static func of(_ buffer: CVPixelBuffer, columns: Int = 32, rows: Int = 24) -> [UInt8]? {
        CVPixelBufferLockBaseAddress(buffer, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(buffer, .readOnly) }

        let format = CVPixelBufferGetPixelFormatType(buffer)
        let planar = CVPixelBufferIsPlanar(buffer)
        // The camera's own frames are YCbCr: the first plane is the brightness already.
        let base = planar ? CVPixelBufferGetBaseAddressOfPlane(buffer, 0) : CVPixelBufferGetBaseAddress(buffer)
        let width = planar ? CVPixelBufferGetWidthOfPlane(buffer, 0) : CVPixelBufferGetWidth(buffer)
        let height = planar ? CVPixelBufferGetHeightOfPlane(buffer, 0) : CVPixelBufferGetHeight(buffer)
        let bytesPerRow = planar ? CVPixelBufferGetBytesPerRowOfPlane(buffer, 0) : CVPixelBufferGetBytesPerRow(buffer)
        guard let base, width > columns, height > rows else { return nil }
        let bytes = base.assumingMemoryBound(to: UInt8.self)
        let bgra = !planar && format == kCVPixelFormatType_32BGRA

        var grid = [UInt8](repeating: 0, count: columns * rows)
        for row in 0..<rows {
            let y = height * row / rows + height / (rows * 2)
            for column in 0..<columns {
                let x = width * column / columns + width / (columns * 2)
                if bgra {
                    let offset = y * bytesPerRow + x * 4
                    grid[row * columns + column] = UInt8((Int(bytes[offset + 2]) * 2 + Int(bytes[offset + 1]) * 5 + Int(bytes[offset])) / 8)
                } else {
                    grid[row * columns + column] = bytes[y * bytesPerRow + x]
                }
            }
        }
        return grid
    }
}
