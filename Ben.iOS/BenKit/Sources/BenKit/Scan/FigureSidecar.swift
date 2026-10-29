import Foundation
import CoreGraphics
import simd

/// Which way a camera picture is turned to stand upright (LiDAR plan, phase 4).
///
/// The back camera's frames arrive in its own landscape orientation whichever way the phone is
/// held, so the video is written with a turn, as the camera app does, and every point the person
/// map keeps is turned the same way: a joint at (0.3, 0.6) is there in the video as it plays.
public enum PictureTurn: Int, Sendable, Codable, CaseIterable {
    case none = 0
    case clockwise = 90
    case upsideDown = 180
    case counterclockwise = 270

    /// From the way the phone is held: the camera's transform in a room whose y is up, as ARKit
    /// gives it. Up in the room, seen along the picture's own x and y, says which edge is the top.
    public init(cameraTransform: simd_float4x4) {
        let ux = cameraTransform.columns.0.y, uy = cameraTransform.columns.1.y
        if abs(ux) > abs(uy) {
            // Held upright, the picture's x runs down the room.
            self = ux < 0 ? .clockwise : .counterclockwise
        } else {
            self = uy >= 0 ? .none : .upsideDown
        }
    }

    /// A video track's turn, read back from its transform.
    public init(transform: CGAffineTransform) {
        let degrees = (atan2(Double(transform.b), Double(transform.a)) * 180 / .pi).rounded()
        switch (Int(degrees) + 360) % 360 {
        case 90: self = .clockwise
        case 180: self = .upsideDown
        case 270: self = .counterclockwise
        default: self = .none
        }
    }

    /// A point of the picture as captured (0–1 from its top left) in the picture once upright.
    public func upright(_ p: SIMD2<Float>) -> SIMD2<Float> {
        switch self {
        case .none: p
        case .clockwise: SIMD2(1 - p.y, p.x)
        case .upsideDown: SIMD2(1 - p.x, 1 - p.y)
        case .counterclockwise: SIMD2(p.y, 1 - p.x)
        }
    }

    /// The other way: a point of the upright picture in the picture as captured.
    public func captured(_ p: SIMD2<Float>) -> SIMD2<Float> {
        switch self {
        case .none: p
        case .clockwise: SIMD2(p.y, 1 - p.x)
        case .upsideDown: SIMD2(1 - p.x, 1 - p.y)
        case .counterclockwise: SIMD2(1 - p.y, p.x)
        }
    }

    /// Whether the upright picture is the captured one on its side.
    public var swapsSides: Bool { self == .clockwise || self == .counterclockwise }

    /// The size of the upright picture.
    public func upright(width: Int, height: Int) -> (width: Int, height: Int) {
        swapsSides ? (height, width) : (width, height)
    }

    /// The turn as a video track's transform, for a picture `width` × `height` as captured.
    public func transform(width: Double, height: Double) -> CGAffineTransform {
        switch self {
        case .none: .identity
        case .clockwise: CGAffineTransform(a: 0, b: 1, c: -1, d: 0, tx: height, ty: 0)
        case .upsideDown: CGAffineTransform(a: -1, b: 0, c: 0, d: -1, tx: width, ty: height)
        case .counterclockwise: CGAffineTransform(a: 0, b: -1, c: 1, d: 0, tx: 0, ty: width)
        }
    }
}

/// Where a point of the camera's picture is on a screen showing it the way ARKit's view and the
/// camera preview do: turned upright for the screen, then filling the view and cropped to it.
public struct PictureOnScreen: Sendable, Equatable {
    public var turn: PictureTurn
    /// The picture as captured.
    public var width: Int, height: Int
    public var viewWidth: Double, viewHeight: Double

    public init(turn: PictureTurn, width: Int, height: Int, viewWidth: Double, viewHeight: Double) {
        self.turn = turn
        self.width = width
        self.height = height
        self.viewWidth = viewWidth
        self.viewHeight = viewHeight
    }

    /// A point of the captured picture (0–1 from its top left) in the view's points.
    public func point(_ p: SIMD2<Float>) -> CGPoint {
        uprightPoint(turn.upright(p))
    }

    /// A point of the upright picture (0–1 from its top left) in the view's points.
    public func uprightPoint(_ p: SIMD2<Float>) -> CGPoint {
        let (w, h) = turn.upright(width: width, height: height)
        guard w > 0, h > 0 else { return .zero }
        let scale = max(viewWidth / Double(w), viewHeight / Double(h))
        let shownWidth = Double(w) * scale, shownHeight = Double(h) * scale
        return CGPoint(x: (viewWidth - shownWidth) / 2 + Double(p.x) * shownWidth,
                       y: (viewHeight - shownHeight) / 2 + Double(p.y) * shownHeight)
    }

    /// The same, fitted whole inside the view rather than filling it — as a video player shows a video.
    public static func fitted(uprightWidth w: Double, uprightHeight h: Double,
                              viewWidth: Double, viewHeight: Double, _ p: SIMD2<Float>) -> CGPoint {
        guard w > 0, h > 0 else { return .zero }
        let scale = min(viewWidth / w, viewHeight / h)
        let shownWidth = w * scale, shownHeight = h * scale
        return CGPoint(x: (viewWidth - shownWidth) / 2 + Double(p.x) * shownWidth,
                       y: (viewHeight - shownHeight) / 2 + Double(p.y) * shownHeight)
    }
}

/// The person map's joints file (LiDAR plan, phase 4; spec 1.2): `figures/figure-001.json`, one
/// for each sighting, written only while a figure is in view.
///
/// ```
/// [{ "t": 12.4, "joints": [[x, y, z, c], …17] }, …]
/// ```
/// `t` is seconds into the video. `x` and `y` are where the joint is in the video as it plays,
/// 0–1 from its top left; `z` meters from the camera, or null where the depth was not trusted;
/// `c` how sure the depth was, 0–1. A joint not found is null. The order is `FigureJoint`'s.
public enum FigureSidecar {
    /// At most this many entries a second: about six kilobytes a second, the plan's figure, and
    /// close enough together to draw thirty frames a second between them.
    public static let entriesPerSecond: Double = 15

    public struct Entry: Sendable, Equatable {
        public var t: Double
        public var joints: [FigurePoint?]

        public init(t: Double, joints: [FigurePoint?]) {
            self.t = t
            self.joints = joints
        }
    }

    /// One entry as it is written: short numbers, since an hour of somebody in view is an hour of
    /// these.
    public static func encode(_ entry: Entry) -> String {
        let joints = entry.joints.map { joint -> String in
            guard let joint else { return "null" }
            let z = joint.z.map { String(format: "%.2f", $0) } ?? "null"
            return "[" + String(format: "%.3f,%.3f,", joint.x, joint.y) + z + String(format: ",%.2f]", joint.c)
        }
        return String(format: "{\"t\":%.3f,\"joints\":[", entry.t) + joints.joined(separator: ",") + "]}"
    }

    public enum ReadError: Error, Equatable {
        case notAJointsFile
    }

    public static func read(_ data: Data) throws -> [Entry] {
        guard let array = try JSONSerialization.jsonObject(with: data) as? [Any] else { throw ReadError.notAJointsFile }
        return array.compactMap { item in
            guard let object = item as? [String: Any], let t = (object["t"] as? NSNumber)?.doubleValue,
                  let joints = object["joints"] as? [Any] else { return nil }
            return Entry(t: t, joints: joints.map { joint in
                guard let values = joint as? [Any], values.count == 4,
                      let x = (values[0] as? NSNumber)?.floatValue, let y = (values[1] as? NSNumber)?.floatValue
                else { return nil }
                return FigurePoint(x: x, y: y, z: (values[2] as? NSNumber)?.floatValue,
                                   c: (values[3] as? NSNumber)?.floatValue ?? 0)
            })
        }
    }

    /// The figure at a moment of the video, in between the entries either side of it; nil when no
    /// entry is within a quarter of a second, which is before the sighting, after it, or a gap in it.
    public static func pose(at t: Double, in entries: [Entry]) -> [FigurePoint?]? {
        guard let after = entries.firstIndex(where: { $0.t >= t }) else {
            guard let last = entries.last, t - last.t <= 0.25 else { return nil }
            return last.joints
        }
        let next = entries[after]
        guard after > 0 else { return next.t - t <= 0.25 ? next.joints : nil }
        let previous = entries[after - 1]
        guard next.t - previous.t <= 0.5 else {
            // A gap: whichever side is near enough, or nothing.
            if t - previous.t <= 0.25 { return previous.joints }
            if next.t - t <= 0.25 { return next.joints }
            return nil
        }
        let span = next.t - previous.t
        let f = Float(span > 0 ? (t - previous.t) / span : 0)
        return zip(previous.joints, next.joints).map { a, b -> FigurePoint? in
            guard let a, let b else { return f < 0.5 ? a : b }
            let z: Float? = if let za = a.z, let zb = b.z { za + (zb - za) * f } else { f < 0.5 ? a.z : b.z }
            return FigurePoint(x: a.x + (b.x - a.x) * f, y: a.y + (b.y - a.y) * f, z: z, c: a.c + (b.c - a.c) * f)
        }
    }

    /// A joints file written as the figure is seen, so an hour of it is never held in memory.
    public final class Writer: @unchecked Sendable {
        public let url: URL
        private let handle: FileHandle
        private var wroteAny = false
        private var lastT: Double?
        public private(set) var byteCount = 0

        public init(url: URL) throws {
            self.url = url
            FileManager.default.createFile(atPath: url.path, contents: Data("[".utf8))
            handle = try FileHandle(forWritingTo: url)
            handle.seekToEndOfFile()
            byteCount = 1
        }

        /// Adds an entry, unless one was written less than a fifteenth of a second ago.
        @discardableResult
        public func append(_ entry: Entry) -> Bool {
            if let lastT, entry.t - lastT < 1 / FigureSidecar.entriesPerSecond - 0.001 { return false }
            lastT = entry.t
            let text = (wroteAny ? "," : "") + FigureSidecar.encode(entry)
            let data = Data(text.utf8)
            handle.write(data)
            byteCount += data.count
            wroteAny = true
            return true
        }

        public func finish() {
            handle.write(Data("]".utf8))
            byteCount += 1
            try? handle.close()
        }
    }
}

/// One continuous sighting of a figure (LiDAR plan, phase 4): from the first frame it was in view
/// to the last, with no gap of more than two seconds between.
public struct FigureSighting: Sendable, Equatable {
    /// The joints file, in the folder the clip is being written to until the session takes it.
    public var file: URL
    /// The frame it was clearest in, with the figure drawn, when one was kept.
    public var picture: URL?
    /// Seconds into the video.
    public var from: Double
    public var to: Double
    /// When it was first seen, for the reading.
    public var startedAt: Date
    public var peakConfidence: Float
}

/// Cuts the frames of a clip into sightings and writes each one's joints file (LiDAR plan,
/// phase 4). A frame with nobody in it writes nothing: a clip nobody walked through leaves no file.
public final class FigureSightings: @unchecked Sendable {
    /// A gap longer than this ends a sighting.
    public static let gap: Double = 2
    /// A sighting's picture is taken again at most this often, and only for a clearer frame.
    public static let pictureInterval: Double = 0.5

    private let folder: URL
    private let turn: PictureTurn
    private var number: Int
    private var open: Open?
    private var ended: [FigureSighting] = []

    private struct Open {
        var writer: FigureSidecar.Writer
        var from: Double
        var last: Double
        var startedAt: Date
        var peak: Float
        var picture: URL?
        var pictureTakenAt: Double?
        var pictureConfidence: Float = -1
    }

    /// - Parameters:
    ///   - folder: where the joints files go until the session takes them.
    ///   - startingAt: the number of this clip's first file — `figure-001.json` for 1.
    public init(folder: URL, turn: PictureTurn, startingAt number: Int = 1) {
        self.folder = folder
        self.turn = turn
        self.number = number
    }

    /// Notes one frame of the video, `t` seconds in. Returns true when this frame is the clearest
    /// view yet of the figure in view and its picture is worth keeping (`keepPicture`).
    @discardableResult
    public func note(_ pose: FigurePose?, at t: Double, date: Date) -> Bool {
        if let current = open, t - current.last > Self.gap { close() }
        guard let pose else { return false }

        if open == nil {
            let url = folder.appendingPathComponent(String(format: "figure-%03d.json", number))
            number += 1
            guard let writer = try? FigureSidecar.Writer(url: url) else { return false }
            open = Open(writer: writer, from: t, last: t, startedAt: date, peak: 0)
        }
        guard var current = open else { return false }
        current.last = t
        let confidence = pose.confidence
        current.peak = max(current.peak, confidence)
        current.writer.append(FigureSidecar.Entry(t: t, joints: pose.joints.map { joint in
            guard let joint else { return nil }
            let upright = turn.upright(SIMD2(joint.x, joint.y))
            return FigurePoint(x: upright.x, y: upright.y, z: joint.z, c: joint.c)
        }))

        let wantsPicture = confidence > current.pictureConfidence + 0.05
            && (current.pictureTakenAt.map { t - $0 >= Self.pictureInterval } ?? true)
        open = current
        return wantsPicture
    }

    /// Keeps a JPEG of the frame `note` asked for as the open sighting's picture.
    public func keepPicture(_ jpeg: Data, at t: Double, confidence: Float) {
        guard var current = open else { return }
        let url = current.writer.url.deletingPathExtension().appendingPathExtension("jpg")
        guard (try? jpeg.write(to: url)) != nil else { return }
        current.picture = url
        current.pictureTakenAt = t
        current.pictureConfidence = confidence
        open = current
    }

    /// The sightings that have ended since the last call.
    public func takeEnded() -> [FigureSighting] {
        defer { ended.removeAll() }
        return ended
    }

    /// Ends any sighting still open — the clip is over — and returns every sighting not yet taken.
    public func finish() -> [FigureSighting] {
        close()
        return takeEnded()
    }

    private func close() {
        guard let current = open else { return }
        open = nil
        current.writer.finish()
        ended.append(FigureSighting(file: current.writer.url, picture: current.picture,
                                    from: current.from, to: current.last,
                                    startedAt: current.startedAt, peakConfidence: current.peak))
    }
}
