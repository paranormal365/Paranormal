import Foundation
import CoreGraphics
import CoreVideo
import simd

/// Everything the person map does with a camera frame (LiDAR plan, phase 4), whichever source it
/// came from: ARKit on a LiDAR phone, or the stand-in on a simulator (`PersonMapFixture`).
///
/// Every frame: the figure is tracked and smoothed, and — when the room has a scan and the phone
/// has found its place in it — the depth is checked for anything person-sized the scan does not
/// have. While a clip is open the frame also goes into the video, and the figure into its joints
/// file. What comes back is what the screen draws.
public actor PersonMapPipeline {

    /// One camera frame, with what ARKit found in it.
    public struct Frame: @unchecked Sendable {
        // @unchecked: a camera's pixel buffer is never written once it has been delivered.
        /// Seconds on the camera's clock.
        public var time: Double
        public var date: Date
        public var image: CVPixelBuffer
        public var camera: PinholeCamera
        /// The figure's joints in `FigureJoint` order, 0–1 in `image`; nil when no body was found.
        public var joints: [SIMD2<Float>?]?
        public var depth: DepthFrame?

        public init(time: Double, date: Date, image: CVPixelBuffer, camera: PinholeCamera,
                    joints: [SIMD2<Float>?]?, depth: DepthFrame?) {
            self.time = time
            self.date = date
            self.image = image
            self.camera = camera
            self.joints = joints
            self.depth = depth
        }
    }

    /// What the screen draws over the camera, in the picture as captured (0–1 from its top left).
    public struct Overlay: Sendable, Equatable {
        public var figure: [FigurePoint?]?
        public var shapes: [Shape]
        public var width: Int
        public var height: Int

        public struct Shape: Sendable, Equatable {
            public var rect: CGRect
            public var words: String
            public var distanceM: Float
        }

        public static let empty = Overlay(figure: nil, shapes: [], width: 0, height: 0)
    }

    /// A clip, once closed: its video, and the figures and shapes seen while it ran, still in the
    /// clip's own folder until the session takes them.
    public struct Clip: Sendable {
        public var folder: URL
        public var video: URL
        public var figures: [FigureSighting]
        public var shapes: [ShapeSighting]
    }

    private struct OpenClip {
        var folder: URL
        var writer: VideoClipWriter?
        var figures: FigureSightings?
        var shapes: [ShapeSighting] = []
        var shapePictures = 0
    }

    private var tracker = FigureTracker()
    private var watcher: ShapeWatcher?
    private var clip: OpenClip?
    private var latest: (image: CVPixelBuffer, turn: PictureTurn)?

    public init() {}

    /// Whether shapes are being looked for: only once there is a scan to compare against.
    public var isWatchingShapes: Bool { watcher != nil }

    /// Starts — or, with nil, stops — looking for shapes against the room's scan. Its frame must be
    /// the frame the camera's transform is in: the phone has found its place in the scan.
    public func watchShapes(against baseline: ScanMesh?) {
        watcher = baseline.map(ShapeWatcher.init(baseline:))
    }

    /// Starts writing a clip into `folder`: the video, and the figures and shapes seen in it.
    public func startClip(in folder: URL) {
        guard clip == nil else { return }
        try? FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        clip = OpenClip(folder: folder)
        // Whatever was being followed before the clip began belongs to no video.
        _ = watcher?.finish()
    }

    public var isRecording: Bool { clip != nil }

    /// Closes the clip: the video file, every sighting still open, every shape still in view.
    public func finishClip() async throws -> Clip? {
        guard let open = clip else { return nil }
        clip = nil
        let figures = open.figures?.finish() ?? []
        var shapes = open.shapes
        if let watcher, let start = open.writer?.startTime {
            shapes += watcher.finish().map { Self.inVideo($0, start: start) }
        }
        guard let writer = open.writer else {
            try? FileManager.default.removeItem(at: open.folder)
            throw VideoClipWriter.Problem.nothingWritten
        }
        let video = try await writer.finish()
        return Clip(folder: open.folder, video: video, figures: figures, shapes: shapes)
    }

    /// Throws a clip away: the app is going and there is no time to finish it.
    public func cancelClip() {
        guard let open = clip else { return }
        clip = nil
        open.writer?.cancel()
        try? FileManager.default.removeItem(at: open.folder)
    }

    /// The latest frame, upright, as a JPEG: a photo while the person map has the camera.
    public func still() -> Data? {
        guard let latest else { return nil }
        return FigureDrawing.still(of: latest.image, turn: latest.turn)
    }

    public func process(_ frame: Frame) -> Overlay {
        let turn = PictureTurn(cameraTransform: frame.camera.transform)
        latest = (frame.image, turn)
        let pose = tracker.track(time: frame.time, joints: frame.joints, depth: frame.depth)
        let width = CVPixelBufferGetWidth(frame.image), height = CVPixelBufferGetHeight(frame.image)

        // The video first, so the clip's clock starts with its first frame.
        if clip != nil, clip?.writer == nil {
            let url = clip!.folder.appendingPathComponent("video.mov")
            clip!.writer = try? VideoClipWriter(url: url, width: width, height: height, turn: turn)
            clip!.figures = FigureSightings(folder: clip!.folder, turn: turn)
        }
        if let writer = clip?.writer { writer.append(frame.image, at: frame.time) }
        let videoTime = clip?.writer?.videoTime(frame.time)

        // The figure's joints, kept while a clip runs.
        if let figures = clip?.figures, let t = videoTime {
            if figures.note(pose, at: t, date: frame.date), let pose,
               let jpeg = FigureDrawing.picture(of: frame.image, turn: turn, figure: pose.joints) {
                figures.keepPicture(jpeg, at: t, confidence: pose.confidence)
            }
        }

        // Shapes: the depth in the scan's frame, less what the figure explains.
        var outlines: [ShapeWatcher.Outline] = []
        if let watcher, let depth = frame.depth {
            // About 128 points across whatever the depth map is (ARKit's is 256 × 192): close enough
            // together at the far wall that a person's points touch, few enough to check every frame.
            let depthCamera = frame.camera.scaled(toWidth: depth.width, height: depth.height)
            let points = depthCamera.points(from: depth, step: max(1, depth.width / 128))
            let figure: [SIMD3<Float>] = pose?.joints.compactMap { joint in
                guard let joint, let z = joint.z else { return nil }
                return frame.camera.unproject(SIMD2(joint.x, joint.y), depth: z)
            } ?? []
            outlines = watcher.note(points: points, camera: frame.camera.position, figure: figure,
                                    at: frame.time, date: frame.date)
            if var open = clip, let start = open.writer?.startTime {
                for outline in outlines where watcher.wantsPicture(for: outline) {
                    let rect = Self.rect(of: outline, seenBy: frame.camera)
                    guard let rect, let jpeg = FigureDrawing.picture(of: frame.image, turn: turn, figure: nil, shapes: [rect])
                    else { continue }
                    open.shapePictures += 1
                    let url = open.folder.appendingPathComponent(String(format: "shape-%03d.jpg", open.shapePictures))
                    if (try? jpeg.write(to: url)) != nil { watcher.keepPicture(url, for: outline) }
                }
                open.shapes += watcher.takeEnded().map { Self.inVideo($0, start: start) }
                clip = open
            } else {
                // Shapes that came and went with no video running belong to nothing.
                for ended in watcher.takeEnded() {
                    if let picture = ended.picture { try? FileManager.default.removeItem(at: picture) }
                }
            }
        }

        return Overlay(
            figure: pose?.joints,
            shapes: outlines.compactMap { outline in
                Self.rect(of: outline, seenBy: frame.camera).map {
                    Overlay.Shape(rect: $0, words: outline.words, distanceM: outline.distanceM)
                }
            },
            width: width, height: height)
    }

    /// A shape's box as it falls in the picture: the rectangle around its eight corners.
    static func rect(of outline: ShapeWatcher.Outline, seenBy camera: PinholeCamera) -> CGRect? {
        var low = SIMD2<Float>(repeating: .infinity), high = SIMD2<Float>(repeating: -.infinity)
        for corner in 0..<8 {
            let p = SIMD3(corner & 1 == 0 ? outline.min.x : outline.max.x,
                          corner & 2 == 0 ? outline.min.y : outline.max.y,
                          corner & 4 == 0 ? outline.min.z : outline.max.z)
            guard let seen = camera.project(p) else { continue }
            low = simd_min(low, seen.at)
            high = simd_max(high, seen.at)
        }
        guard low.x < high.x, low.y < high.y else { return nil }
        let a = simd_clamp(low, SIMD2(repeating: 0), SIMD2(repeating: 1))
        let b = simd_clamp(high, SIMD2(repeating: 0), SIMD2(repeating: 1))
        guard b.x > a.x, b.y > a.y else { return nil }
        return CGRect(x: Double(a.x), y: Double(a.y), width: Double(b.x - a.x), height: Double(b.y - a.y))
    }

    /// A shape's times, from the camera's clock to seconds into the video.
    static func inVideo(_ shape: ShapeSighting, start: Double) -> ShapeSighting {
        var shape = shape
        shape.from = max(0, shape.from - start)
        shape.to = max(shape.from, shape.to - start)
        return shape
    }
}
