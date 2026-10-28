import Foundation
import CoreGraphics
import ImageIO
import simd

/// A room the simulator can "scan" (LiDAR plan, phase 1).
///
/// The simulator has no LiDAR, so `-scanFakeRoom` hands the scanning screens this instead of the
/// sensor: a four-by-five-metre room with a table and a chair, as a mesh, a room model, a preview
/// and three keyframes. Everything after the scanner — the retention choice, the package, the
/// upload, the viewer — then runs exactly as it would on a phone. The package tests use the same
/// room, so what the UI test sends is what the unit tests read.
public enum ScanFixture {

    /// The room's model: floor, four walls, a table and a chair.
    public static func roomModel() -> ScanRoomModel {
        let width: Float = 4, depth: Float = 5, height: Float = 2.5
        func at(_ x: Float, _ y: Float, _ z: Float, turned: Float = 0) -> simd_float4x4 {
            var m = simd_float4x4(simd_quatf(angle: turned, axis: SIMD3(0, 1, 0)))
            m.columns.3 = SIMD4(x, y, z, 1)
            return m
        }
        // A floor lies flat: its width and length are in the plane, so it is turned onto its back.
        var floor = at(0, 0, 0)
        floor = floor * simd_float4x4(simd_quatf(angle: -.pi / 2, axis: SIMD3(1, 0, 0)))
        return ScanRoomModel(
            surfaces: [
                .init(category: "floor", transform: floor, dimensions: SIMD3(width, depth, 0)),
                .init(category: "wall", transform: at(0, height / 2, -depth / 2), dimensions: SIMD3(width, height, 0)),
                .init(category: "wall", transform: at(0, height / 2, depth / 2), dimensions: SIMD3(width, height, 0)),
                .init(category: "wall", transform: at(-width / 2, height / 2, 0, turned: .pi / 2), dimensions: SIMD3(depth, height, 0)),
                .init(category: "wall", transform: at(width / 2, height / 2, 0, turned: .pi / 2), dimensions: SIMD3(depth, height, 0)),
            ],
            objects: [
                .init(category: "table", transform: at(0.5, 0.375, -1), dimensions: SIMD3(1.2, 0.75, 0.8)),
                .init(category: "chair", transform: at(-0.4, 0.45, -0.6), dimensions: SIMD3(0.45, 0.9, 0.45)),
            ])
    }

    /// Writes the room into a new capture folder and records it, as the scanner does when Done is
    /// tapped. Returns the capture, its folder already in `store`.
    public static func makeCapture(in store: ScanStore, room: String, placeId: UUID?, sessionId: UUID?,
                                   startedAt: Date, scannedAt: Date) throws -> ScanCapture {
        let id = UUID()
        let folder = try store.newCaptureFolder(id)
        let model = roomModel()
        let mesh = model.boxMesh()

        try JSONEncoder().encode(model).write(to: folder.appendingPathComponent(ScanPackage.roomModelEntry))
        try MeshExporter.glb(mesh).write(to: folder.appendingPathComponent(ScanPackage.meshEntry))
        try Data("fixture world map".utf8).write(to: folder.appendingPathComponent(ScanPackage.worldMapEntry))
        try jpeg(width: 128, height: 96, shade: 0.4).write(to: folder.appendingPathComponent(ScanPackage.previewEntry))

        var keyframes: [ScanKeyframe] = []
        let keyframesFolder = folder.appendingPathComponent("keyframes")
        for index in 1...3 {
            let name = String(format: "%04d", index)
            try jpeg(width: 64, height: 48, shade: 0.2 + 0.2 * Double(index)).write(to: keyframesFolder.appendingPathComponent("\(name).jpg"))
            let depth = [Float](repeating: 2.5, count: 16 * 12)
            try DepthCodec.encode(depth).write(to: keyframesFolder.appendingPathComponent("\(name).depth"))
            let pose = simd_float4x4.translation(SIMD3(Float(index) * 0.6 - 1.2, 1.5, 1.5))
            keyframes.append(ScanKeyframe(
                file: "\(name).jpg", depth: "\(name).depth", depthWidth: 16, depthHeight: 12,
                pose: pose.columnMajor, intrinsics: [60, 0, 0, 0, 60, 0, 32, 24, 1],
                imageWidth: 64, imageHeight: 48, at: Double(index) * 20))
        }
        try JSONEncoder().encode(keyframes).write(to: keyframesFolder.appendingPathComponent("index.json"))

        let capture = ScanCapture(
            id: id, room: room, placeId: placeId, sessionId: sessionId, startedAt: startedAt,
            scannedAt: scannedAt, device: "Simulator", scannerVersion: ScanPackage.scannerVersion,
            floorAreaM2: model.floorAreaM2, triangleCount: mesh.triangleCount, keyframeCount: keyframes.count)
        try store.save(capture)
        return capture
    }

    /// A small gray JPEG — enough to be a JPEG by its bytes and a picture to look at.
    public static func jpeg(width: Int, height: Int, shade: Double) throws -> Data {
        let space = CGColorSpaceCreateDeviceRGB()
        guard let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: 0,
                                      space: space, bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue)
        else { throw CocoaError(.fileWriteUnknown) }
        context.setFillColor(CGColor(red: shade, green: shade, blue: shade * 1.1, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        guard let image = context.makeImage() else { throw CocoaError(.fileWriteUnknown) }

        let data = NSMutableData()
        guard let destination = CGImageDestinationCreateWithData(data, "public.jpeg" as CFString, 1, nil)
        else { throw CocoaError(.fileWriteUnknown) }
        CGImageDestinationAddImage(destination, image, [kCGImageDestinationLossyCompressionQuality: 0.8] as CFDictionary)
        guard CGImageDestinationFinalize(destination) else { throw CocoaError(.fileWriteUnknown) }
        return data as Data
    }
}
