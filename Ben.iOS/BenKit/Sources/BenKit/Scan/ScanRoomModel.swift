import Foundation
import simd

/// A room as walls, openings and furniture — the scan's `room.json` (LiDAR plan, §3).
///
/// RoomPlan describes a room parametrically: a wall is a plane with a size, a chair is a box with a
/// category. That is what a comparison diffs ("the chair moved 42 cm") and what a viewer can draw
/// when the full mesh is missing. Kept in our own shape rather than RoomPlan's `CapturedRoom`
/// encoding, so the format is written down here, readable by anything, and testable on a Mac,
/// where RoomPlan does not exist.
///
/// Transforms are 4×4, column-major, in metres, in the scan's frame (ARKit's world: y up).
public struct ScanRoomModel: Codable, Sendable, Equatable {

    public struct Surface: Codable, Sendable, Equatable, Identifiable {
        public var id: UUID
        /// `wall`, `floor`, `door`, `window` or `opening`.
        public var category: String
        public var transform: [Float]
        /// Width, height and depth in metres; a wall's depth is zero.
        public var dimensions: [Float]

        public init(id: UUID = UUID(), category: String, transform: simd_float4x4, dimensions: SIMD3<Float>) {
            self.id = id
            self.category = category
            self.transform = transform.columnMajor
            self.dimensions = [dimensions.x, dimensions.y, dimensions.z]
        }
    }

    public struct Object: Codable, Sendable, Equatable, Identifiable {
        public var id: UUID
        /// RoomPlan's category — `chair`, `table`, `bed`, `sofa`, `storage`… — or `unclassified`.
        public var category: String
        public var transform: [Float]
        public var dimensions: [Float]
        /// `high`, `medium` or `low`, as RoomPlan judged it.
        public var confidence: String

        public init(id: UUID = UUID(), category: String, transform: simd_float4x4,
                    dimensions: SIMD3<Float>, confidence: String = "high") {
            self.id = id
            self.category = category
            self.transform = transform.columnMajor
            self.dimensions = [dimensions.x, dimensions.y, dimensions.z]
            self.confidence = confidence
        }
    }

    public var version: Int
    public var surfaces: [Surface]
    public var objects: [Object]

    public init(surfaces: [Surface], objects: [Object]) {
        self.version = 1
        self.surfaces = surfaces
        self.objects = objects
    }

    /// The floor's area in square metres, from the floor surfaces RoomPlan found — or nil when it
    /// found none, which is better said than guessed.
    public var floorAreaM2: Double? {
        let floors = surfaces.filter { $0.category == "floor" }
        guard !floors.isEmpty else { return nil }
        return floors.reduce(0) { $0 + Double($1.dimensions[0]) * Double($1.dimensions[1]) }
    }

    /// The room drawn as boxes — walls, floor and furniture — for when there is no LiDAR mesh to
    /// show: a scan whose mesh could not be read, or the simulator's stand-in room.
    public func boxMesh() -> ScanMesh {
        var mesh = ScanMesh()
        for surface in surfaces {
            let d = surface.dimensions
            // A surface has no thickness; give it a centimetre so it has two sides to see.
            mesh.appendBox(transform: simd_float4x4(columnMajor: surface.transform),
                           size: SIMD3(max(d[0], 0.01), max(d[1], 0.01), max(d[2], 0.01)))
        }
        for object in objects {
            mesh.appendBox(transform: simd_float4x4(columnMajor: object.transform),
                           size: SIMD3(object.dimensions[0], object.dimensions[1], object.dimensions[2]))
        }
        return mesh
    }

    public static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys]
        return encoder
    }()
}

extension simd_float4x4 {
    /// Sixteen numbers, a column at a time — the order glTF and the scan's JSON use.
    public var columnMajor: [Float] {
        [columns.0, columns.1, columns.2, columns.3].flatMap { [$0.x, $0.y, $0.z, $0.w] }
    }

    /// The inverse of `columnMajor`. Anything but sixteen numbers is the identity.
    public init(columnMajor values: [Float]) {
        guard values.count == 16 else { self = matrix_identity_float4x4; return }
        self.init(SIMD4(values[0], values[1], values[2], values[3]),
                  SIMD4(values[4], values[5], values[6], values[7]),
                  SIMD4(values[8], values[9], values[10], values[11]),
                  SIMD4(values[12], values[13], values[14], values[15]))
    }

    /// Where this transform puts its origin.
    public var translation: SIMD3<Float> { SIMD3(columns.3.x, columns.3.y, columns.3.z) }

    /// A transform that only moves.
    public static func translation(_ t: SIMD3<Float>) -> simd_float4x4 {
        var m = matrix_identity_float4x4
        m.columns.3 = SIMD4(t.x, t.y, t.z, 1)
        return m
    }
}
