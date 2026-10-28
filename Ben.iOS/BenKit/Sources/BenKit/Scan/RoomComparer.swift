import Foundation
import simd

/// One thing a comparison found (LiDAR plan, phase 2) — a row of `changes.json`, a `scan_change`
/// reading, and on the server a `PlaceScanChanges` row.
public struct ScanChange: Codable, Sendable, Equatable {
    public enum Kind: String, Codable, Sendable {
        case moved, missing, new
    }

    public var kind: Kind
    /// RoomPlan's category (`chair`, `table`…) or `unclassified` for a shape the mesh alone found.
    public var object: String
    /// How far it moved, for `moved`.
    public var distanceCm: Double?
    /// How far it turned about the vertical, for `moved`.
    public var rotationDeg: Double?
    /// Width, height, depth, for `new` and `missing`.
    public var sizeCm: [Double]?
    /// Where, in meters, in the place scan's frame.
    public var where_: [Double]
    /// `changes/01.jpg` inside the comparison scan, when a keyframe saw it.
    public var picture: String?

    private enum CodingKeys: String, CodingKey {
        case kind, object, distanceCm, rotationDeg, sizeCm, picture
        case where_ = "where"
    }

    public init(kind: Kind, object: String, distanceCm: Double? = nil, rotationDeg: Double? = nil,
                sizeCm: [Double]? = nil, where_: [Double], picture: String? = nil) {
        self.kind = kind
        self.object = object
        self.distanceCm = distanceCm
        self.rotationDeg = rotationDeg
        self.sizeCm = sizeCm
        self.where_ = where_
        self.picture = picture
    }

    /// In words, for the timeline and for reading aloud: "The chair moved 42 cm."
    public var sentence: String {
        let thing = object == "unclassified" ? "Something" : "The \(object)"
        switch kind {
        case .moved:
            var parts: [String] = []
            if let d = distanceCm, d >= 1 { parts.append("moved \(Int(d.rounded())) cm") }
            if let r = rotationDeg, abs(r) >= 1 { parts.append("turned \(Int(abs(r).rounded()))°") }
            return "\(thing) \(parts.isEmpty ? "moved" : parts.joined(separator: " and "))."
        case .missing:
            return object == "unclassified" ? "Something is gone\(Self.size(sizeCm))." : "\(thing) is gone."
        case .new:
            return object == "unclassified" ? "Something is new\(Self.size(sizeCm))." : "\(thing) is new."
        }
    }

    private static func size(_ cm: [Double]?) -> String {
        guard let cm, cm.count == 3 else { return "" }
        return " (\(cm.map { String(Int($0.rounded())) }.joined(separator: " × ")) cm)"
    }
}

/// Finds what moved, what is missing and what is new between a room's scan and a later scan of
/// the same room, both in the scan's frame (LiDAR plan, phase 2, §5).
///
/// **Objects first.** RoomPlan names the furniture, so each kind of thing is matched to the same
/// kind nearest it — an optimal assignment, not first-come — and a pair that moved more than 3 cm
/// or turned more than 5° is a move. A thing with no partner is missing or new.
///
/// **Then the surfaces.** Both meshes are dropped into 5 cm cells; cells one has and the other
/// does not, grouped, and at least the size of a shoebox (30 × 20 × 15 cm), are something new or
/// gone that RoomPlan did not name — a box on the floor, a picture off the wall. A cell counts only
/// where the other scan looked — it found floor or wall in the same spot seen from above — so a
/// corner the second visit never pointed at is not "missing".
///
/// iPhone LiDAR is about a centimetre up close and a few across a room, so these thresholds say
/// "moved" only where something did. A petal that turned is not a finding.
public enum RoomComparer {

    public static let moveThresholdMeters: Float = 0.03
    public static let turnThresholdDegrees: Float = 5
    /// Further apart than this, two things of one kind are not one thing that moved.
    public static let farthestMatchMeters: Float = 3
    public static let voxelMeters: Float = 0.05
    /// The smallest cluster worth saying: a shoebox, sorted largest side first.
    public static let shoeboxMeters = SIMD3<Float>(0.30, 0.20, 0.15)

    /// The changes, objects first then surfaces, nearest the room's middle first within each.
    public static func compare(baseline: ScanRoomModel, baselineMesh: ScanMesh,
                               comparison: ScanRoomModel, comparisonMesh: ScanMesh) -> [ScanChange] {
        var changes = objectChanges(baseline: baseline.objects, comparison: comparison.objects)

        // The furniture already accounted for is left out of the surfaces, so a moved chair is
        // one change rather than a chair, a gap and a new shape.
        let explained = (baseline.objects + comparison.objects).map(Box.init)
        changes += surfaceChanges(baselineMesh: baselineMesh, comparisonMesh: comparisonMesh, explained: explained)
        return changes
    }

    // MARK: Objects

    static func objectChanges(baseline: [ScanRoomModel.Object], comparison: [ScanRoomModel.Object]) -> [ScanChange] {
        var changes: [ScanChange] = []
        let categories = Set(baseline.map(\.category)).union(comparison.map(\.category)).sorted()

        for category in categories {
            let before = baseline.filter { $0.category == category }
            let after = comparison.filter { $0.category == category }
            let cost = before.map { b in after.map { a in Double(simd_distance(position(b), position(a))) } }
            let pairs = Hungarian.assign(cost: cost, rows: before.count, columns: after.count)

            var matchedAfter = Set<Int>()
            for (i, b) in before.enumerated() {
                guard let j = pairs[i], Float(cost[i][j]) <= farthestMatchMeters else {
                    changes.append(ScanChange(kind: .missing, object: category, sizeCm: sizeCm(b), where_: whereOf(b)))
                    continue
                }
                matchedAfter.insert(j)
                let a = after[j]
                let moved = simd_distance(position(b), position(a))
                let turned = turnDegrees(from: b, to: a)
                if moved > moveThresholdMeters || abs(turned) > turnThresholdDegrees {
                    changes.append(ScanChange(kind: .moved, object: category,
                                              distanceCm: (Double(moved) * 1000).rounded() / 10,
                                              rotationDeg: (Double(turned) * 10).rounded() / 10,
                                              where_: whereOf(a)))
                }
            }
            for (j, a) in after.enumerated() where !matchedAfter.contains(j) {
                changes.append(ScanChange(kind: .new, object: category, sizeCm: sizeCm(a), where_: whereOf(a)))
            }
        }
        return changes
    }

    private static func position(_ o: ScanRoomModel.Object) -> SIMD3<Float> {
        simd_float4x4(columnMajor: o.transform).translation
    }

    private static func whereOf(_ o: ScanRoomModel.Object) -> [Double] {
        let p = position(o)
        return [p.x, p.y, p.z].map { (Double($0) * 1000).rounded() / 1000 }
    }

    private static func sizeCm(_ o: ScanRoomModel.Object) -> [Double] {
        o.dimensions.map { (Double($0) * 1000).rounded() / 10 }
    }

    /// The turn about the vertical between two placements, in degrees, between −180 and 180,
    /// counter-clockwise seen from above. Phase 0 is to check on a real room whether RoomPlan keeps
    /// a symmetric table's axes from one scan to the next; if it does not, a table would read as
    /// turned half way round.
    static func turnDegrees(from a: ScanRoomModel.Object, to b: ScanRoomModel.Object) -> Float {
        func heading(_ o: ScanRoomModel.Object) -> Float {
            let m = simd_float4x4(columnMajor: o.transform)
            return atan2(-m.columns.0.z, m.columns.0.x)
        }
        var degrees = (heading(b) - heading(a)) * 180 / .pi
        while degrees > 180 { degrees -= 360 }
        while degrees < -180 { degrees += 360 }
        return degrees
    }

    // MARK: Surfaces

    /// An object's box, to leave out of the surface comparison.
    struct Box {
        let inverse: simd_float4x4
        let half: SIMD3<Float>

        init(_ o: ScanRoomModel.Object) {
            inverse = simd_float4x4(columnMajor: o.transform).inverse
            // A little larger than RoomPlan's box: the mesh of a chair sticks out of its box.
            half = SIMD3(o.dimensions[0], o.dimensions[1], o.dimensions[2]) / 2 + 0.1
        }

        func contains(_ p: SIMD3<Float>) -> Bool {
            let local = inverse * SIMD4(p.x, p.y, p.z, 1)
            return abs(local.x) <= half.x && abs(local.y) <= half.y && abs(local.z) <= half.z
        }
    }

    static func surfaceChanges(baselineMesh: ScanMesh, comparisonMesh: ScanMesh, explained: [Box]) -> [ScanChange] {
        let before = Voxels(baselineMesh)
        let after = Voxels(comparisonMesh)
        var changes: [ScanChange] = []

        // New: cells the second visit has that the first did not, where the first looked.
        let appeared = after.cells.filter { !before.near($0, within: 1) && before.looked(above: $0) }
        // Missing: cells the first visit had that the second did not, where the second looked.
        let vanished = before.cells.filter { !after.near($0, within: 1) && after.looked(above: $0) }

        for (cells, kind) in [(appeared, ScanChange.Kind.new), (vanished, .missing)] {
            for cluster in Voxels.clusters(of: Set(cells)) {
                let low = cluster.reduce(SIMD3<Int32>(repeating: .max)) { simd_min($0, $1) }
                let high = cluster.reduce(SIMD3<Int32>(repeating: .min)) { simd_max($0, $1) }
                let size = SIMD3<Float>(high &- low &+ 1) * voxelMeters
                let sorted = [size.x, size.y, size.z].sorted(by: >)
                guard sorted[0] >= shoeboxMeters.x, sorted[1] >= shoeboxMeters.y, sorted[2] >= shoeboxMeters.z else { continue }

                let center = (SIMD3<Float>(low) + SIMD3<Float>(high)) / 2 * voxelMeters
                guard !explained.contains(where: { $0.contains(center) }) else { continue }
                changes.append(ScanChange(
                    kind: kind, object: "unclassified",
                    sizeCm: [size.x, size.y, size.z].map { (Double($0) * 1000).rounded() / 10 },
                    where_: [center.x, center.y, center.z].map { (Double($0) * 1000).rounded() / 1000 }))
            }
        }
        return changes
    }
}

/// A mesh's surfaces as 5 cm cells.
struct Voxels {
    let cells: Set<SIMD3<Int32>>

    init(_ mesh: ScanMesh, size: Float = RoomComparer.voxelMeters) {
        var cells = Set<SIMD3<Int32>>()
        func mark(_ p: SIMD3<Float>) {
            cells.insert(SIMD3(Int32((p.x / size).rounded(.down)), Int32((p.y / size).rounded(.down)), Int32((p.z / size).rounded(.down))))
        }
        // Each triangle sampled finely enough that a large wall triangle fills its cells rather
        // than marking its three corners.
        for t in stride(from: 0, to: mesh.indices.count - 2, by: 3) {
            let a = mesh.positions[Int(mesh.indices[t])]
            let b = mesh.positions[Int(mesh.indices[t + 1])]
            let c = mesh.positions[Int(mesh.indices[t + 2])]
            let longest = max(simd_distance(a, b), simd_distance(b, c), simd_distance(c, a))
            let steps = max(1, Int((longest / (size / 2)).rounded(.up)))
            for i in 0...steps {
                for j in 0...(steps - i) {
                    let u = Float(i) / Float(steps), v = Float(j) / Float(steps)
                    mark(a + (b - a) * u + (c - a) * v)
                }
            }
        }
        self.cells = cells
        self.columns = Set(cells.map { SIMD2($0.x, $0.z) })
    }

    /// Where this scan found anything, seen from above.
    let columns: Set<SIMD2<Int32>>

    /// Whether this scan saw the spot a cell is in — anything at all within ten centimetres of it,
    /// seen from above: the floor under a box, the wall behind a picture.
    func looked(above cell: SIMD3<Int32>) -> Bool {
        for dx: Int32 in -2...2 {
            for dz: Int32 in -2...2 where columns.contains(SIMD2(cell.x + dx, cell.z + dz)) {
                return true
            }
        }
        return false
    }

    /// Whether any cell lies within `within` cells of this one.
    func near(_ cell: SIMD3<Int32>, within: Int32) -> Bool {
        for dx in -within...within {
            for dy in -within...within {
                for dz in -within...within where cells.contains(cell &+ SIMD3(dx, dy, dz)) {
                    return true
                }
            }
        }
        return false
    }

    /// Cells that touch, face, edge or corner, as groups.
    static func clusters(of cells: Set<SIMD3<Int32>>) -> [[SIMD3<Int32>]] {
        var unseen = cells
        var groups: [[SIMD3<Int32>]] = []
        while let start = unseen.first {
            unseen.remove(start)
            var group = [start]
            var queue = [start]
            while let cell = queue.popLast() {
                for dx: Int32 in -1...1 {
                    for dy: Int32 in -1...1 {
                        for dz: Int32 in -1...1 {
                            let next = cell &+ SIMD3(dx, dy, dz)
                            if unseen.remove(next) != nil {
                                group.append(next)
                                queue.append(next)
                            }
                        }
                    }
                }
            }
            groups.append(group)
        }
        // Largest first, so the most noticeable change is said first.
        return groups.sorted { $0.count > $1.count }
    }
}

/// The cheapest one-to-one pairing of rows to columns (Kuhn–Munkres), for a few dozen things.
enum Hungarian {
    /// For each row, the column it is paired with, or nil when there were fewer columns than rows.
    static func assign(cost: [[Double]], rows: Int, columns: Int) -> [Int?] {
        guard rows > 0 else { return [] }
        guard columns > 0 else { return Array(repeating: nil, count: rows) }
        let n = max(rows, columns)
        // Squared up with a cost no real pairing reaches, so missing partners are left over.
        let big = (cost.flatMap { $0 }.max() ?? 0) * 10 + 1
        var a = Array(repeating: Array(repeating: big, count: n + 1), count: n + 1)
        for i in 0..<rows { for j in 0..<columns { a[i + 1][j + 1] = cost[i][j] } }

        var u = [Double](repeating: 0, count: n + 1), v = [Double](repeating: 0, count: n + 1)
        var p = [Int](repeating: 0, count: n + 1), way = [Int](repeating: 0, count: n + 1)
        for i in 1...n {
            p[0] = i
            var j0 = 0
            var minv = [Double](repeating: .infinity, count: n + 1)
            var used = [Bool](repeating: false, count: n + 1)
            repeat {
                used[j0] = true
                let i0 = p[j0]
                var delta = Double.infinity
                var j1 = 0
                for j in 1...n where !used[j] {
                    let cur = a[i0][j] - u[i0] - v[j]
                    if cur < minv[j] { minv[j] = cur; way[j] = j0 }
                    if minv[j] < delta { delta = minv[j]; j1 = j }
                }
                for j in 0...n {
                    if used[j] { u[p[j]] += delta; v[j] -= delta } else { minv[j] -= delta }
                }
                j0 = j1
            } while p[j0] != 0
            repeat {
                let j1 = way[j0]
                p[j0] = p[j1]
                j0 = j1
            } while j0 != 0
        }

        var result = [Int?](repeating: nil, count: rows)
        for j in 1...n where p[j] >= 1 && p[j] <= rows && j <= columns {
            result[p[j] - 1] = j - 1
        }
        return result
    }
}
