import Foundation
import ARKit
import BenKit

/// The person map's camera (LiDAR plan, phase 4): ARKit with body detection and the depth sensor
/// on a LiDAR phone, or the stand-in room (`PersonMapFixture`) on a simulator started with
/// `-figureFake`.
///
/// While it is on it has the camera. The session's own capture (`FieldCameraSession`) stands down
/// and asks this for its clips, photos and motion instead, so the session's video carries on —
/// written from ARKit's frames — with the figure's joints kept beside it.
@MainActor
@Observable
final class PersonMapCamera {

    /// Whether this DEBUG run stands the stand-in room in for the camera (`-figureFake`).
    static var usesFake: Bool {
        #if DEBUG
        ProcessInfo.processInfo.arguments.contains("-figureFake")
        #else
        false
        #endif
    }

    /// The room's scan, for telling a shape from the room.
    struct Baseline: @unchecked Sendable {
        // @unchecked: the world map is read once, when ARKit is handed it, and never changed.
        var mesh: ScanMesh
        var worldMap: ARWorldMap?
    }

    /// How the search for shapes is going, in words the screen can use.
    enum Shapes: Equatable {
        /// The room has no scan, so only the figure is drawn.
        case noScan
        /// Finding the phone's place in the room's scan.
        case finding
        case watching
        case notFound(String)
    }

    private(set) var isRunning = false
    private(set) var problem: String?
    /// What to draw over the picture, in the picture as captured.
    private(set) var overlay: PersonMapPipeline.Overlay = .empty
    /// The stand-in's picture as captured; nil on a phone, where ARKit's own view shows the camera.
    private(set) var fakePicture: CGImage?
    private(set) var clipStartedAt: Date?
    private(set) var shapes: Shapes = .noScan

    let session: ARSession?
    @ObservationIgnored private let pipeline = PersonMapPipeline()
    @ObservationIgnored private var feeding: Task<Void, Never>?
    @ObservationIgnored private var source: Task<Void, Never>?
    @ObservationIgnored private var relay: FrameRelay?
    @ObservationIgnored private var starting: Task<Void, Never>?
    @ObservationIgnored private var finished: PersonMapPipeline.Clip?
    @ObservationIgnored private var relocalizing: Task<Void, Never>?

    init() {
        session = Self.usesFake ? nil : ARSession()
    }

    /// Starts the camera. `motion` is handed a coarse brightness grid four times a second, for
    /// Watch for Motion.
    func start(motion: @escaping @Sendable ([UInt8]) -> Void) {
        guard !isRunning else { return }
        let (stream, continuation) = AsyncStream.makeStream(of: PersonMapPipeline.Frame.self,
                                                            bufferingPolicy: .bufferingNewest(1))
        if let session {
            guard let configuration = Self.configuration(worldMap: nil) else {
                problem = "This phone can't find a figure in the camera's picture."
                return
            }
            let relay = FrameRelay(continuation: continuation)
            self.relay = relay
            session.delegate = relay
            session.delegateQueue = DispatchQueue(label: "com.ishaunted.person-map")
            session.run(configuration, options: [.resetTracking, .removeExistingAnchors])
        } else {
            // Drawn off the main thread: the stand-in draws its room in software.
            source = Task.detached(priority: .userInitiated) { [weak self] in
                let began = Date()
                while !Task.isCancelled {
                    let t = Date().timeIntervalSince(began)
                    if let shot = PersonMapFixture.shot(at: t, date: Date()) {
                        continuation.yield(shot.frame)
                        await MainActor.run { self?.fakePicture = shot.picture }
                    }
                    // Ten a second: the stand-in draws its room in software, and a simulator is
                    // not a phone.
                    try? await Task.sleep(for: .milliseconds(100))
                }
            }
        }

        let pipeline = pipeline
        feeding = Task.detached(priority: .userInitiated) { [weak self] in
            var lastGrid = Date.distantPast
            for await frame in stream {
                let overlay = await pipeline.process(frame)
                if frame.date.timeIntervalSince(lastGrid) >= 0.25, let grid = LumaGrid.of(frame.image) {
                    lastGrid = frame.date
                    motion(grid)
                }
                await MainActor.run { self?.overlay = overlay }
            }
        }
        isRunning = true
        problem = nil
    }

    func stop() {
        guard isRunning else { return }
        relocalizing?.cancel()
        source?.cancel()
        source = nil
        session?.pause()
        relay?.finish()
        relay = nil
        feeding?.cancel()
        feeding = nil
        isRunning = false
        overlay = .empty
        fakePicture = nil
        if clipStartedAt != nil {
            clipStartedAt = nil
            let pipeline = pipeline
            Task { await pipeline.cancelClip() }
        }
    }

    // MARK: - Shapes

    /// Starts looking for shapes against the room's scan. On a phone the scan's world map is loaded
    /// first and the phone has to recognize the room, so "the box by the door" is the same door.
    func use(_ baseline: Baseline?) {
        relocalizing?.cancel()
        let pipeline = pipeline
        guard let baseline else {
            shapes = .noScan
            Task { await pipeline.watchShapes(against: nil) }
            return
        }
        guard let session else {
            shapes = .watching
            Task { await pipeline.watchShapes(against: baseline.mesh) }
            return
        }
        guard let map = baseline.worldMap, let configuration = Self.configuration(worldMap: map) else {
            shapes = .notFound("This room's scan has no map to find the room by, so only the figure is drawn.")
            return
        }
        shapes = .finding
        session.run(configuration, options: [.resetTracking, .removeExistingAnchors])
        relocalizing = Task { [weak self] in
            var watch = RelocalizationWatch(startedAt: Date())
            while !Task.isCancelled, watch.outcome == .looking {
                if let tracking = session.currentFrame?.camera.trackingState {
                    watch.note(Self.tracking(tracking), at: Date())
                }
                try? await Task.sleep(for: .milliseconds(250))
            }
            guard !Task.isCancelled, let self else { return }
            switch watch.outcome {
            case .recognized:
                await pipeline.watchShapes(against: baseline.mesh)
                self.shapes = .watching
            case .failed:
                self.shapes = .notFound("The room wasn't recognized, so shapes can't be checked against its scan. The figure is still drawn.")
            case .looking:
                break
            }
        }
    }

    /// The room's scan from its file: the mesh, and the world map to find the room by.
    nonisolated static func baseline(from url: URL) -> Baseline? {
        guard let reader = try? ScanPackage.Reader(url: url),
              let glb = try? reader.data(ScanPackage.meshEntry),
              let mesh = try? MeshExporter.read(glb).mesh else { return nil }
        let map = (try? reader.data(ScanPackage.worldMapEntry)).flatMap { $0 }
            .flatMap { try? ($0 as NSData).decompressed(using: .zlib) as Data }
            .flatMap { try? NSKeyedUnarchiver.unarchivedObject(ofClass: ARWorldMap.self, from: $0) }
        return Baseline(mesh: mesh, worldMap: map)
    }

    // MARK: - Clips and photos

    func startClip() throws {
        guard isRunning else { throw FieldCameraError.notRunning }
        guard clipStartedAt == nil else { return }
        let folder = FileManager.default.temporaryDirectory
            .appendingPathComponent("person-map-\(UUID().uuidString)", isDirectory: true)
        clipStartedAt = Date()
        let pipeline = pipeline
        starting = Task { await pipeline.startClip(in: folder) }
    }

    /// Ends the clip and hands back its video. What the person map saw in it waits in `takeClip`.
    func finishClip() async throws -> URL {
        guard clipStartedAt != nil else { throw FieldCameraError.noClipRunning }
        await starting?.value
        clipStartedAt = nil
        do {
            guard let clip = try await pipeline.finishClip() else { throw FieldCameraError.noClipRunning }
            finished = clip
            return clip.video
        } catch let error as VideoClipWriter.Problem {
            throw FieldCameraError.clipFailed(error.errorDescription ?? "The video couldn't be saved.")
        }
    }

    /// The figures and shapes of the clip `finishClip` just closed, once.
    func takeClip() -> PersonMapPipeline.Clip? {
        defer { finished = nil }
        return finished
    }

    /// The picture now, as a photo: silent, since no shutter is pressed.
    func still() async throws -> URL {
        guard isRunning, let data = await pipeline.still() else { throw FieldCameraError.photoFailed }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("field-\(UUID().uuidString).jpg")
        try data.write(to: url)
        return url
    }

    // MARK: - ARKit

    /// World tracking with body detection and the depth sensor, as many of them as the phone does
    /// together, at thirty frames a second in the camera's full 4:3 picture.
    private static func configuration(worldMap: ARWorldMap?) -> ARWorldTrackingConfiguration? {
        let configuration = ARWorldTrackingConfiguration()
        let wanted: [ARConfiguration.FrameSemantics] = [[.bodyDetection, .sceneDepth], [.bodyDetection]]
        guard let semantics = wanted.first(where: ARWorldTrackingConfiguration.supportsFrameSemantics) else { return nil }
        configuration.frameSemantics = semantics
        if let format = ARWorldTrackingConfiguration.supportedVideoFormats.first(where: {
            $0.framesPerSecond == 30 && abs($0.imageResolution.width / $0.imageResolution.height - 4.0 / 3.0) < 0.01
        }) {
            configuration.videoFormat = format
        }
        configuration.initialWorldMap = worldMap
        return configuration
    }

    private static func tracking(_ state: ARCamera.TrackingState) -> RelocalizationWatch.Tracking {
        switch state {
        case .notAvailable: .notAvailable
        case .normal: .normal
        case .limited(.relocalizing): .relocalizing
        case .limited: .limited
        }
    }
}

/// ARKit's frames, turned into the person map's as they arrive — on ARKit's queue, and never kept:
/// an ARFrame held on to holds the camera's buffers with it.
private final class FrameRelay: NSObject, ARSessionDelegate, @unchecked Sendable {
    private let continuation: AsyncStream<PersonMapPipeline.Frame>.Continuation
    private let names = ARSkeletonDefinition.defaultBody2D.jointNames

    init(continuation: AsyncStream<PersonMapPipeline.Frame>.Continuation) {
        self.continuation = continuation
    }

    func finish() { continuation.finish() }

    func session(_ session: ARSession, didUpdate frame: ARFrame) {
        let image = frame.capturedImage
        let camera = PinholeCamera(transform: frame.camera.transform, intrinsics: frame.camera.intrinsics,
                                   width: CVPixelBufferGetWidth(image), height: CVPixelBufferGetHeight(image))
        continuation.yield(PersonMapPipeline.Frame(
            time: frame.timestamp, date: Date(), image: image, camera: camera,
            joints: joints(frame.detectedBody), depth: frame.sceneDepth.flatMap(Self.depth)))
    }

    /// ARKit's 2D joints in the person map's order, by name; nil with no body or no joint tracked.
    private func joints(_ body: ARBody2D?) -> [SIMD2<Float>?]? {
        guard let skeleton = body?.skeleton else { return nil }
        let landmarks = skeleton.jointLandmarks
        var found = [SIMD2<Float>?](repeating: nil, count: FigureJoint.allCases.count)
        for joint in FigureJoint.allCases {
            guard let index = names.firstIndex(of: joint.arkitName), index < landmarks.count,
                  skeleton.isJointTracked(index) else { continue }
            let point = landmarks[index]
            if point.x.isFinite, point.y.isFinite { found[joint.rawValue] = point }
        }
        return found.contains { $0 != nil } ? found : nil
    }

    /// The depth map and its confidence, copied out of ARKit's buffers.
    private static func depth(_ data: ARDepthData) -> DepthFrame? {
        let map = data.depthMap
        let width = CVPixelBufferGetWidth(map), height = CVPixelBufferGetHeight(map)
        guard CVPixelBufferGetPixelFormatType(map) == kCVPixelFormatType_DepthFloat32 else { return nil }
        CVPixelBufferLockBaseAddress(map, .readOnly)
        defer { CVPixelBufferUnlockBaseAddress(map, .readOnly) }
        guard let base = CVPixelBufferGetBaseAddress(map) else { return nil }
        let rowBytes = CVPixelBufferGetBytesPerRow(map)
        var depth = [Float](repeating: 0, count: width * height)
        for row in 0..<height {
            let line = (base + row * rowBytes).assumingMemoryBound(to: Float32.self)
            for column in 0..<width { depth[row * width + column] = line[column] }
        }

        // Medium everywhere when ARKit gives no confidence, which a LiDAR phone always does.
        var confidence = [UInt8](repeating: DepthFrame.trusted, count: width * height)
        if let sure = data.confidenceMap, CVPixelBufferGetWidth(sure) == width, CVPixelBufferGetHeight(sure) == height {
            CVPixelBufferLockBaseAddress(sure, .readOnly)
            defer { CVPixelBufferUnlockBaseAddress(sure, .readOnly) }
            if let sureBase = CVPixelBufferGetBaseAddress(sure) {
                let sureRow = CVPixelBufferGetBytesPerRow(sure)
                for row in 0..<height {
                    let line = (sureBase + row * sureRow).assumingMemoryBound(to: UInt8.self)
                    for column in 0..<width { confidence[row * width + column] = line[column] }
                }
            }
        }
        return DepthFrame(width: width, height: height, depth: depth, confidence: confidence)
    }
}
