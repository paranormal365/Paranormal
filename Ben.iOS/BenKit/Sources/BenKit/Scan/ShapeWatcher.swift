import Foundation
import simd

/// Something person-sized that is not in the room's scan (LiDAR plan, phase 4).
///
/// Never jointed and never called a figure: it is a cluster of depth points standing where the
/// room's scan has nothing, with its size and how far away it was, and that is all that is claimed
/// about it. The sensor sees one side of it, so its size is the size of what was seen.
public struct ShapeSighting: Sendable, Equatable {
    /// Seconds into the video.
    public var from: Double
    public var to: Double
    /// When it was first seen, for the reading.
    public var startedAt: Date
    /// Across, up and through, in centimeters, along the room scan's axes.
    public var sizeCm: SIMD3<Float>
    /// From the camera to its middle when it was seen most completely.
    public var distanceM: Float
    /// Its middle, in the room scan's frame.
    public var center: SIMD3<Float>
    /// The frame it was seen most completely in, with it outlined, when one was kept.
    public var picture: URL?

    /// "45 × 160 × 30 cm".
    public var words: String {
        let cm = sizeCm.map { Int($0.rounded()) }
        return "\(cm[0]) × \(cm[1]) × \(cm[2]) cm"
    }
}

private extension SIMD3 where Scalar == Float {
    func map(_ transform: (Float) -> Int) -> [Int] { [transform(x), transform(y), transform(z)] }
}

/// The room's scan as the shape watcher tests against it: which 5 cm cells lie within 10 cm of
/// its surfaces, and which columns of the room it looked into at all (LiDAR plan, phase 4).
///
/// A dense grid over the scan's bounds, worked out once, so the twelve thousand points of each
/// frame cost a lookup each.
public struct ScanSurroundings: Sendable {
    public let cell: Float
    let origin: SIMD3<Float>
    let size: SIMD3<Int32>
    private let near: [Bool]
    private let looked: [Bool]

    /// - Parameters:
    ///   - tolerance: a point this close to the scan's surfaces is the room, not something in it.
    public init(mesh: ScanMesh, cell: Float = ShapeWatcher.cell, tolerance: Float = ShapeWatcher.offScan) {
        self.cell = cell
        let reach = Int32((tolerance / cell).rounded(.up))
        let bounds = mesh.bounds ?? (SIMD3(repeating: 0), SIMD3(repeating: 0))
        let margin = SIMD3<Float>(repeating: Float(reach + 1) * cell)
        // Snapped to whole cells, so a cell here is the same cell `Voxels` means.
        let shift = SIMD3<Int32>((bounds.min - margin) / cell, rounding: .down)
        origin = SIMD3<Float>(shift) * cell
        let extent = (bounds.max + margin) - origin
        let size = SIMD3<Int32>(extent / cell, rounding: .up)
        self.size = size

        let surface = Voxels(mesh, size: cell)
        var near = [Bool](repeating: false, count: Int(size.x) * Int(size.y) * Int(size.z))
        var looked = [Bool](repeating: false, count: Int(size.x) * Int(size.z))
        for meshCell in surface.cells {
            let c = meshCell &- shift
            for dx in -reach...reach {
                for dz in -reach...reach {
                    let x = c.x + dx, z = c.z + dz
                    guard x >= 0, x < size.x, z >= 0, z < size.z else { continue }
                    looked[Int(z) * Int(size.x) + Int(x)] = true
                    for dy in -reach...reach {
                        let y = c.y + dy
                        guard y >= 0, y < size.y else { continue }
                        near[(Int(z) * Int(size.y) + Int(y)) * Int(size.x) + Int(x)] = true
                    }
                }
            }
        }
        self.near = near
        self.looked = looked
    }

    func cellOf(_ p: SIMD3<Float>) -> SIMD3<Int32> {
        SIMD3<Int32>((p - origin) / cell, rounding: .down)
    }

    /// Whether a point stands where the scan has nothing within the tolerance — inside the room the
    /// scan covers, in a column of it the scan looked into, and away from all its surfaces.
    public func isOffScan(_ p: SIMD3<Float>) -> Bool {
        let c = cellOf(p)
        guard c.x >= 0, c.y >= 0, c.z >= 0, c.x < size.x, c.y < size.y, c.z < size.z else { return false }
        guard looked[Int(c.z) * Int(size.x) + Int(c.x)] else { return false }
        return !near[(Int(c.z) * Int(size.y) + Int(c.y)) * Int(size.x) + Int(c.x)]
    }
}

/// Watches the depth for something person-sized that is not in the room's scan (LiDAR plan,
/// phase 4): depth points more than 10 cm off the scan, grouped per frame, a group at least
/// 40 cm across and 100 cm tall that is there for at least a second.
///
/// A group with three of the figure's joints in it is the figure: drawn as one, and not called a
/// shape as well. One in the dark, where body detection finds nobody and the depth still sees, is
/// exactly what this is for.
public final class ShapeWatcher: @unchecked Sendable {
    public static let cell: Float = 0.05
    /// Further than this from the scan's surfaces, a point is something in the room.
    public static let offScan: Float = 0.10
    /// The smallest shape worth naming: 40 cm across, 100 cm tall.
    public static let across: Float = 0.40
    public static let tall: Float = 1.00
    /// It has to be there this long before it is a shape.
    public static let persists: Double = 1
    /// A gap longer than this ends it, as for a figure.
    public static let gap: Double = 2
    /// Groups in consecutive frames closer than this are the same shape.
    static let sameShape: Float = 0.6

    /// A shape being followed, for the outline on the screen.
    public struct Outline: Sendable, Equatable {
        public var min: SIMD3<Float>
        public var max: SIMD3<Float>
        public var words: String
        public var distanceM: Float
    }

    private struct Track {
        var from: Double
        var last: Double
        var startedAt: Date
        var box: (min: SIMD3<Float>, max: SIMD3<Float>)
        var best: (min: SIMD3<Float>, max: SIMD3<Float>)
        var bestDistance: Float
        var center: SIMD3<Float>
        var picture: URL?
        var pictureTall: Float = 0

        var lasted: Double { last - from }
        var bestSize: SIMD3<Float> { (best.max - best.min) * 100 }
    }

    private let surroundings: ScanSurroundings
    private var tracks: [Track] = []
    private var ended: [ShapeSighting] = []

    public init(baseline: ScanMesh) {
        surroundings = ScanSurroundings(mesh: baseline)
    }

    /// One frame's look at the room.
    ///
    /// - Parameters:
    ///   - points: what the depth saw, in the room scan's frame.
    ///   - camera: where the camera was.
    ///   - figure: the joints of any figure in view, in the room scan's frame.
    ///   - t: seconds into the video, or since the watching began.
    /// - Returns: the shapes in view now that have lasted long enough to outline.
    @discardableResult
    public func note(points: [SIMD3<Float>], camera: SIMD3<Float>, figure: [SIMD3<Float>] = [],
                     at t: Double, date: Date) -> [Outline] {
        var cells = Set<SIMD3<Int32>>()
        for p in points where surroundings.isOffScan(p) { cells.insert(surroundings.cellOf(p)) }

        var seen: [(min: SIMD3<Float>, max: SIMD3<Float>)] = []
        for group in Voxels.clusters(of: cells) {
            var low = group[0], high = group[0]
            for c in group {
                low = simd_min(low, c)
                high = simd_max(high, c)
            }
            let box = (min: surroundings.origin + SIMD3<Float>(low) * surroundings.cell,
                       max: surroundings.origin + SIMD3<Float>(high &+ 1) * surroundings.cell)
            let extent = box.max - box.min
            guard max(extent.x, extent.z) >= Self.across - 0.001, extent.y >= Self.tall - 0.001 else { continue }
            let grown = (min: box.min - 0.15, max: box.max + 0.15)
            let joints = figure.filter { all($0 .>= grown.min) && all($0 .<= grown.max) }.count
            if joints >= 3 { continue }
            seen.append(box)
        }

        // Each group goes with the nearest shape already followed, or starts one.
        var matched = Set<Int>()
        for box in seen {
            let center = (box.min + box.max) / 2
            let distance = simd_distance(center, camera)
            let nearest = tracks.indices
                .filter { !matched.contains($0) }
                .min { simd_distance(tracks[$0].center, center) < simd_distance(tracks[$1].center, center) }
            if let nearest, simd_distance(tracks[nearest].center, center) < Self.sameShape {
                matched.insert(nearest)
                tracks[nearest].last = t
                tracks[nearest].box = box
                tracks[nearest].center = center
                if (box.max.y - box.min.y) >= (tracks[nearest].best.max.y - tracks[nearest].best.min.y) {
                    tracks[nearest].best = box
                    tracks[nearest].bestDistance = distance
                }
            } else {
                tracks.append(Track(from: t, last: t, startedAt: date, box: box, best: box,
                                    bestDistance: distance, center: center))
                matched.insert(tracks.count - 1)
            }
        }

        // Shapes gone longer than the gap are over: the ones that lasted are kept.
        var still: [Track] = []
        for track in tracks {
            if t - track.last > Self.gap {
                keep(track)
            } else {
                still.append(track)
            }
        }
        tracks = still

        return tracks
            .filter { $0.lasted >= Self.persists && $0.last == t }
            .map { Outline(min: $0.box.min, max: $0.box.max, words: Self.words($0.bestSize),
                           distanceM: simd_distance(($0.box.min + $0.box.max) / 2, camera)) }
    }

    /// Whether a shape being outlined now is in its clearest view yet: worth a picture.
    public func wantsPicture(for outline: Outline) -> Bool {
        guard let track = tracks.first(where: { $0.box.min == outline.min && $0.box.max == outline.max }) else { return false }
        return (track.box.max.y - track.box.min.y) > track.pictureTall + 0.05
    }

    /// Keeps a JPEG of the frame `wantsPicture` asked for.
    public func keepPicture(_ url: URL, for outline: Outline) {
        guard let index = tracks.firstIndex(where: { $0.box.min == outline.min && $0.box.max == outline.max }) else { return }
        if let old = tracks[index].picture, old != url { try? FileManager.default.removeItem(at: old) }
        tracks[index].picture = url
        tracks[index].pictureTall = outline.max.y - outline.min.y
    }

    /// The shapes that have ended since the last call.
    public func takeEnded() -> [ShapeSighting] {
        defer { ended.removeAll() }
        return ended
    }

    /// Ends every shape being followed — the clip is over — and returns those not yet taken.
    public func finish() -> [ShapeSighting] {
        for track in tracks { keep(track) }
        tracks.removeAll()
        return takeEnded()
    }

    private func keep(_ track: Track) {
        guard track.lasted >= Self.persists else {
            if let picture = track.picture { try? FileManager.default.removeItem(at: picture) }
            return
        }
        ended.append(ShapeSighting(from: track.from, to: track.last, startedAt: track.startedAt,
                                   sizeCm: track.bestSize, distanceM: track.bestDistance,
                                   center: (track.best.min + track.best.max) / 2, picture: track.picture))
    }

    static func words(_ sizeCm: SIMD3<Float>) -> String {
        "\(Int(sizeCm.x.rounded())) × \(Int(sizeCm.y.rounded())) × \(Int(sizeCm.z.rounded())) cm"
    }
}
