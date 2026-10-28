#if canImport(RoomPlan) && canImport(ARKit) && os(iOS)
import ARKit
import CoreImage
import Foundation
import RoomPlan
import simd

/// Scans one room with the phone's LiDAR: the room model, the mesh, the world map and the
/// keyframes, into a capture folder (LiDAR plan, phase 1, §5).
///
/// **One camera stream.** A single `ARSession` is handed to RoomPlan (`RoomCaptureSession(arSession:)`,
/// iOS 17), which configures and runs it; this reads the same session's frames for keyframes and
/// its mesh anchors at the end. RoomPlan sets the session up itself, so nothing here replaces its
/// delegate: frames are read by polling `currentFrame`, ten times a second, which is plenty for a
/// keyframe every half meter.
///
/// **What phase 0 still has to measure** on a real phone: whether RoomPlan's own configuration
/// carries scene depth and mesh anchors. When it does not, the keyframes keep their pictures
/// without depth and the mesh is drawn from the room model's boxes — the scan still uploads and
/// still opens, and the manifest says what it holds.
@MainActor
@Observable
public final class RoomScanner {

    public enum Phase: Equatable, Sendable {
        case idle
        case scanning
        case finishing
        case finished
        case failed(String)
    }

    /// The session everything is captured from; a view shows its camera.
    public let arSession: ARSession

    public private(set) var phase: Phase = .idle
    /// RoomPlan's coaching, in words, while there is something to say.
    public private(set) var instruction: String?
    /// How much of the room has been found, 0 to 1 — walls, floor and pictures together.
    public private(set) var coverage: Double = 0
    public private(set) var wallsFound = 0
    public private(set) var keyframeCount = 0

    private let roomSession: RoomCaptureSession
    private let relay: Relay
    private let folder: URL
    private var policy = KeyframePolicy()
    private var keyframes: [ScanKeyframe] = []
    private var latestRoom: CapturedRoom?
    private var startedAt = Date()
    private var poll: Task<Void, Never>?
    private var ended: CheckedContinuation<CapturedRoomData?, Never>?
    private var endedData: CapturedRoomData??
    private let imageContext = CIContext()

    public static var isSupported: Bool { RoomCaptureSession.isSupported }

    /// A scanner writing into `folder`, which must exist with a `keyframes` folder inside.
    ///
    /// A comparison hands in the session that has already found its place in the room's scan
    /// (phase 2), so what this captures is in that scan's frame.
    public init(folder: URL, session: ARSession = ARSession()) {
        self.folder = folder
        self.arSession = session
        self.roomSession = RoomCaptureSession(arSession: session)
        self.relay = Relay()
        relay.owner = self
        roomSession.delegate = relay
    }

    public func start() {
        guard phase == .idle else { return }
        startedAt = Date()
        phase = .scanning
        var configuration = RoomCaptureSession.Configuration()
        configuration.isCoachingEnabled = true
        roomSession.run(configuration: configuration)
        poll = Task { [weak self] in
            while !Task.isCancelled {
                self?.considerFrame()
                try? await Task.sleep(for: .milliseconds(100))
            }
        }
    }

    /// Stops and writes everything into the folder. The capture is returned, not yet saved.
    public func finish(room: String, placeId: UUID?, sessionId: UUID?) async throws -> ScanCapture {
        guard phase == .scanning else { throw ScanPackage.PackageError.missingMesh }
        phase = .finishing
        poll?.cancel()

        // The AR session keeps running while RoomPlan stops, so the world map and the mesh can
        // still be read from it.
        roomSession.stop(pauseARSession: false)
        let anchors = arSession.currentFrame?.anchors.compactMap { $0 as? ARMeshAnchor } ?? []
        let worldMap = await currentWorldMapData()
        let data = await roomData()
        arSession.pause()

        let scannedAt = Date()
        var model: ScanRoomModel?
        if let data, let room = try? await RoomBuilder(options: [.beautifyObjects]).capturedRoom(from: data) {
            model = Self.model(of: room)
            try? room.export(to: folder.appendingPathComponent(ScanPackage.roomPlanEntry), exportOptions: .parametric)
        } else if let latestRoom {
            model = Self.model(of: latestRoom)
        }
        if let model {
            try JSONEncoder().encode(model).write(to: folder.appendingPathComponent(ScanPackage.roomModelEntry))
        }

        var mesh = ScanMesh()
        for anchor in anchors { Self.append(anchor, to: &mesh) }
        mesh = mesh.welded()
        if mesh.triangleCount == 0, let model { mesh = model.boxMesh() }
        guard mesh.triangleCount > 0 else {
            phase = .failed("Nothing of the room was found. Scan it again, slowly, with the lights on.")
            throw ScanPackage.PackageError.missingMesh
        }
        // Painted from the keyframes when there are some (phase 2); the plain mesh otherwise.
        let keyframesFolder = folder.appendingPathComponent("keyframes")
        if let painted = Texturer.texture(mesh: mesh, keyframes: keyframes, folder: keyframesFolder) {
            try MeshExporter.glb(painted.mesh, texture: painted.atlas).write(to: folder.appendingPathComponent(ScanPackage.meshEntry))
        } else {
            try MeshExporter.glb(mesh).write(to: folder.appendingPathComponent(ScanPackage.meshEntry))
        }

        if let worldMap {
            try worldMap.write(to: folder.appendingPathComponent(ScanPackage.worldMapEntry))
        }

        try JSONEncoder().encode(keyframes).write(to: keyframesFolder.appendingPathComponent("index.json"))
        if let middle = keyframes.isEmpty ? nil : keyframes[keyframes.count / 2],
           let preview = Self.preview(from: keyframesFolder.appendingPathComponent(middle.file), context: imageContext) {
            try preview.write(to: folder.appendingPathComponent(ScanPackage.previewEntry))
        }

        phase = .finished
        return ScanCapture(
            id: UUID(uuidString: folder.lastPathComponent) ?? UUID(), room: room, placeId: placeId,
            sessionId: sessionId, startedAt: startedAt, scannedAt: scannedAt,
            device: DeviceModel.identifier(), scannerVersion: ScanPackage.scannerVersion,
            floorAreaM2: model?.floorAreaM2, triangleCount: mesh.triangleCount, keyframeCount: keyframes.count)
    }

    /// Stops without keeping anything.
    public func cancel() {
        poll?.cancel()
        if phase == .scanning { roomSession.stop() }
        arSession.pause()
        phase = .idle
    }

    // MARK: Frames

    private func considerFrame() {
        guard phase == .scanning, let frame = arSession.currentFrame else { return }
        let normal: Bool = if case .normal = frame.camera.trackingState { true } else { false }
        guard policy.shouldKeep(pose: frame.camera.transform, trackingIsNormal: normal) else { return }

        let index = keyframes.count + 1
        let name = String(format: "%04d", index)
        let keyframesFolder = folder.appendingPathComponent("keyframes")

        let image = CIImage(cvPixelBuffer: frame.capturedImage)
        guard let jpeg = imageContext.jpegRepresentation(
                of: image, colorSpace: CGColorSpace(name: CGColorSpace.sRGB)!,
                options: [CIImageRepresentationOption(rawValue: kCGImageDestinationLossyCompressionQuality as String): 0.8]),
              (try? jpeg.write(to: keyframesFolder.appendingPathComponent("\(name).jpg"))) != nil
        else { return }

        var depthName: String?
        var depthSize: (Int, Int)?
        if let depth = frame.sceneDepth ?? frame.smoothedSceneDepth,
           let meters = Self.meters(depth.depthMap, confidence: depth.confidenceMap),
           let encoded = try? DepthCodec.encode(meters.values),
           (try? encoded.write(to: keyframesFolder.appendingPathComponent("\(name).depth"))) != nil {
            depthName = "\(name).depth"
            depthSize = (meters.width, meters.height)
        }

        let k = frame.camera.intrinsics
        keyframes.append(ScanKeyframe(
            file: "\(name).jpg", depth: depthName, depthWidth: depthSize?.0, depthHeight: depthSize?.1,
            pose: frame.camera.transform.columnMajor,
            intrinsics: [k.columns.0.x, k.columns.0.y, k.columns.0.z,
                         k.columns.1.x, k.columns.1.y, k.columns.1.z,
                         k.columns.2.x, k.columns.2.y, k.columns.2.z],
            imageWidth: Int(frame.camera.imageResolution.width), imageHeight: Int(frame.camera.imageResolution.height),
            at: Date().timeIntervalSince(startedAt)))
        keyframeCount = keyframes.count
        updateCoverage()
    }

    private func updateCoverage() {
        let walls = min(1, Double(wallsFound) / 4)
        let floor = (latestRoom?.floors.isEmpty == false) ? 1.0 : 0
        let pictures = min(1, Double(keyframeCount) / 60)
        coverage = 0.55 * walls + 0.15 * floor + 0.3 * pictures
    }

    // MARK: RoomPlan's answers

    fileprivate func roomDidUpdate(_ room: CapturedRoom) {
        latestRoom = room
        wallsFound = room.walls.count
        updateCoverage()
    }

    fileprivate func provided(_ instruction: String?) {
        self.instruction = instruction
    }

    /// RoomPlan's coaching, in plain words. Nil when there is nothing to correct.
    nonisolated static func words(for instruction: RoomCaptureSession.Instruction) -> String? {
        switch instruction {
        case .moveCloseToWall: "Move closer to the wall."
        case .moveAwayFromWall: "Step back from the wall."
        case .slowDown: "Slow down."
        case .turnOnLight: "It's too dark to scan. Turn on a light."
        case .lowTexture: "Point at something with more detail."
        case .normal: nil
        @unknown default: nil
        }
    }

    fileprivate func ended(with data: CapturedRoomData?, error: Error?) {
        if let error, phase == .scanning {
            phase = .failed(error.localizedDescription)
        }
        if let ended {
            self.ended = nil
            ended.resume(returning: data)
        } else {
            endedData = .some(data)
        }
    }

    private func roomData() async -> CapturedRoomData? {
        if let endedData { return endedData }
        return await withCheckedContinuation { continuation in
            ended = continuation
            // RoomPlan always answers a stop; this only guards against it never doing so.
            Task { [weak self] in
                try? await Task.sleep(for: .seconds(10))
                guard let self, let waiting = self.ended else { return }
                self.ended = nil
                waiting.resume(returning: nil)
            }
        }
    }

    /// The world map a later phone relocalizes against, archived and compressed — as bytes, so
    /// nothing that is not safe to share crosses threads.
    private func currentWorldMapData() async -> Data? {
        await withCheckedContinuation { continuation in
            arSession.getCurrentWorldMap { map, _ in
                let archived = map.flatMap { try? NSKeyedArchiver.archivedData(withRootObject: $0, requiringSecureCoding: true) }
                continuation.resume(returning: archived.flatMap { try? ($0 as NSData).compressed(using: .zlib) as Data })
            }
        }
    }

    // MARK: Conversions

    private static func model(of room: CapturedRoom) -> ScanRoomModel {
        func category(_ surface: CapturedRoom.Surface.Category) -> String {
            switch surface {
            case .wall: "wall"
            case .floor: "floor"
            case .door: "door"
            case .window: "window"
            case .opening: "opening"
            @unknown default: "surface"
            }
        }
        func confidence(_ c: CapturedRoom.Confidence) -> String {
            switch c {
            case .high: "high"
            case .medium: "medium"
            case .low: "low"
            @unknown default: "low"
            }
        }
        let surfaces = (room.walls + room.floors + room.doors + room.windows + room.openings).map {
            ScanRoomModel.Surface(id: $0.identifier, category: category($0.category),
                                  transform: $0.transform, dimensions: $0.dimensions)
        }
        let objects = room.objects.map {
            ScanRoomModel.Object(id: $0.identifier, category: String(describing: $0.category),
                                 transform: $0.transform, dimensions: $0.dimensions,
                                 confidence: confidence($0.confidence))
        }
        return ScanRoomModel(surfaces: surfaces, objects: objects)
    }

    /// One ARKit mesh chunk, read out of its Metal buffers and moved into the scan's frame.
    private static func append(_ anchor: ARMeshAnchor, to mesh: inout ScanMesh) {
        let geometry = anchor.geometry
        func vectors(_ source: ARGeometrySource) -> [SIMD3<Float>] {
            let base = source.buffer.contents().advanced(by: source.offset)
            return (0..<source.count).map { index in
                let p = base.advanced(by: index * source.stride).assumingMemoryBound(to: Float.self)
                return SIMD3(p[0], p[1], p[2])
            }
        }
        let faces = geometry.faces
        guard faces.indexCountPerPrimitive == 3 else { return }
        let base = faces.buffer.contents()
        var indices: [UInt32] = []
        indices.reserveCapacity(faces.count * 3)
        for i in 0..<(faces.count * 3) {
            let value: UInt32 = faces.bytesPerIndex == 2
                ? UInt32(base.advanced(by: i * 2).assumingMemoryBound(to: UInt16.self).pointee)
                : base.advanced(by: i * 4).assumingMemoryBound(to: UInt32.self).pointee
            indices.append(value)
        }
        mesh.append(positions: vectors(geometry.vertices), normals: vectors(geometry.normals),
                    indices: indices, transform: anchor.transform)
    }

    /// The depth map in meters, with the points ARKit was not confident of written as unknown.
    private static func meters(_ depth: CVPixelBuffer, confidence: CVPixelBuffer?) -> (values: [Float], width: Int, height: Int)? {
        CVPixelBufferLockBaseAddress(depth, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(depth, .readOnly) }
        if let confidence { CVPixelBufferLockBaseAddress(confidence, .readOnly) }
        defer { if let confidence { CVPixelBufferUnlockBaseAddress(confidence, .readOnly) } }

        guard CVPixelBufferGetPixelFormatType(depth) == kCVPixelFormatType_DepthFloat32,
              let base = CVPixelBufferGetBaseAddress(depth) else { return nil }
        let width = CVPixelBufferGetWidth(depth), height = CVPixelBufferGetHeight(depth)
        let rowBytes = CVPixelBufferGetBytesPerRow(depth)
        let confidenceBase = confidence.flatMap(CVPixelBufferGetBaseAddress)
        let confidenceRow = confidence.map(CVPixelBufferGetBytesPerRow) ?? 0

        var values = [Float](repeating: 0, count: width * height)
        for y in 0..<height {
            let row = base.advanced(by: y * rowBytes).assumingMemoryBound(to: Float.self)
            for x in 0..<width {
                let sure = confidenceBase.map {
                    $0.advanced(by: y * confidenceRow + x).assumingMemoryBound(to: UInt8.self).pointee
                        >= UInt8(ARConfidenceLevel.medium.rawValue)
                } ?? true
                values[y * width + x] = sure ? row[x] : 0
            }
        }
        return (values, width, height)
    }

    /// One keyframe, 1280 wide, for lists.
    private static func preview(from jpeg: URL, context: CIContext) -> Data? {
        guard let image = CIImage(contentsOf: jpeg) else { return nil }
        let scale = 1280 / max(image.extent.width, 1)
        let scaled = scale < 1 ? image.transformed(by: CGAffineTransform(scaleX: scale, y: scale)) : image
        return context.jpegRepresentation(
            of: scaled, colorSpace: CGColorSpace(name: CGColorSpace.sRGB)!,
            options: [CIImageRepresentationOption(rawValue: kCGImageDestinationLossyCompressionQuality as String): 0.8])
    }
}

/// RoomPlan's delegate, kept off the main actor because RoomPlan does not promise which thread it
/// answers on; every answer is carried to the scanner on the main actor.
private final class Relay: RoomCaptureSessionDelegate, @unchecked Sendable {
    weak var owner: RoomScanner?

    func captureSession(_ session: RoomCaptureSession, didUpdate room: CapturedRoom) {
        Task { @MainActor [weak owner] in owner?.roomDidUpdate(room) }
    }

    func captureSession(_ session: RoomCaptureSession, didProvide instruction: RoomCaptureSession.Instruction) {
        let words = RoomScanner.words(for: instruction)
        Task { @MainActor [weak owner] in owner?.provided(words) }
    }

    func captureSession(_ session: RoomCaptureSession, didEndWith data: CapturedRoomData, error: (any Error)?) {
        let message = error?.localizedDescription
        Task { @MainActor [weak owner] in
            owner?.ended(with: data, error: message.map { ScanFailure(message: $0) })
        }
    }
}

private struct ScanFailure: LocalizedError, Sendable {
    let message: String
    var errorDescription: String? { message }
}
#endif
