import Foundation
import simd

/// A triangle mesh in the scan's frame: meters, y up (LiDAR plan, phase 1).
public struct ScanMesh: Sendable, Equatable {
    public var positions: [SIMD3<Float>] = []
    public var normals: [SIMD3<Float>] = []
    /// Three per triangle, counter-clockwise seen from the front.
    public var indices: [UInt32] = []
    /// Texture coordinates, one per position, once the mesh is textured (phase 2).
    public var uvs: [SIMD2<Float>]? = nil

    public init() {}

    public var triangleCount: Int { indices.count / 3 }

    /// The smallest box holding every point, or nil for an empty mesh.
    public var bounds: (min: SIMD3<Float>, max: SIMD3<Float>)? {
        guard var low = positions.first else { return nil }
        var high = low
        for p in positions {
            low = simd_min(low, p)
            high = simd_max(high, p)
        }
        return (low, high)
    }

    /// Adds a chunk of mesh — one `ARMeshAnchor`'s worth — moved into the scan's frame.
    public mutating func append(positions chunk: [SIMD3<Float>], normals chunkNormals: [SIMD3<Float>],
                                indices chunkIndices: [UInt32], transform: simd_float4x4) {
        let base = UInt32(positions.count)
        let rotation = simd_float3x3(SIMD3(transform.columns.0.x, transform.columns.0.y, transform.columns.0.z),
                                     SIMD3(transform.columns.1.x, transform.columns.1.y, transform.columns.1.z),
                                     SIMD3(transform.columns.2.x, transform.columns.2.y, transform.columns.2.z))
        for (index, p) in chunk.enumerated() {
            let world = transform * SIMD4(p.x, p.y, p.z, 1)
            positions.append(SIMD3(world.x, world.y, world.z))
            let n = index < chunkNormals.count ? chunkNormals[index] : SIMD3<Float>(0, 1, 0)
            normals.append(simd_normalize(rotation * n))
        }
        indices.append(contentsOf: chunkIndices.map { $0 + base })
    }

    /// Adds a box centerd on `transform`'s origin — walls and furniture as RoomPlan sizes them.
    public mutating func appendBox(transform: simd_float4x4, size: SIMD3<Float>) {
        let h = size / 2
        // Six faces, four corners each, so every face keeps its own flat normal.
        let faces: [(normal: SIMD3<Float>, corners: [SIMD3<Float>])] = [
            (SIMD3(1, 0, 0), [SIMD3(h.x, -h.y, -h.z), SIMD3(h.x, h.y, -h.z), SIMD3(h.x, h.y, h.z), SIMD3(h.x, -h.y, h.z)]),
            (SIMD3(-1, 0, 0), [SIMD3(-h.x, -h.y, h.z), SIMD3(-h.x, h.y, h.z), SIMD3(-h.x, h.y, -h.z), SIMD3(-h.x, -h.y, -h.z)]),
            (SIMD3(0, 1, 0), [SIMD3(-h.x, h.y, -h.z), SIMD3(-h.x, h.y, h.z), SIMD3(h.x, h.y, h.z), SIMD3(h.x, h.y, -h.z)]),
            (SIMD3(0, -1, 0), [SIMD3(-h.x, -h.y, h.z), SIMD3(-h.x, -h.y, -h.z), SIMD3(h.x, -h.y, -h.z), SIMD3(h.x, -h.y, h.z)]),
            (SIMD3(0, 0, 1), [SIMD3(h.x, -h.y, h.z), SIMD3(h.x, h.y, h.z), SIMD3(-h.x, h.y, h.z), SIMD3(-h.x, -h.y, h.z)]),
            (SIMD3(0, 0, -1), [SIMD3(-h.x, -h.y, -h.z), SIMD3(-h.x, h.y, -h.z), SIMD3(h.x, h.y, -h.z), SIMD3(h.x, -h.y, -h.z)]),
        ]
        for face in faces {
            append(positions: face.corners, normals: Array(repeating: face.normal, count: 4),
                   indices: [0, 1, 2, 0, 2, 3], transform: transform)
        }
    }

    /// The same mesh with points closer than `tolerance` merged into one — the seams between
    /// ARKit's chunks overlap, and a scan of a room carries every seam twice without this.
    public func welded(tolerance: Float = 0.001) -> ScanMesh {
        guard uvs == nil else { return self }   // a textured mesh's seams are its texture's seams
        var result = ScanMesh()
        var normalSums: [SIMD3<Float>] = []
        var seen: [SIMD3<Int32>: UInt32] = [:]
        var remap = [UInt32](repeating: 0, count: positions.count)

        for (index, p) in positions.enumerated() {
            let cell = SIMD3<Int32>(Int32((p.x / tolerance).rounded()),
                                    Int32((p.y / tolerance).rounded()),
                                    Int32((p.z / tolerance).rounded()))
            let n = index < normals.count ? normals[index] : .zero
            if let existing = seen[cell] {
                remap[index] = existing
                normalSums[Int(existing)] += n
            } else {
                let next = UInt32(result.positions.count)
                seen[cell] = next
                remap[index] = next
                result.positions.append(p)
                normalSums.append(n)
            }
        }
        result.normals = normalSums.map { simd_length($0) > 0 ? simd_normalize($0) : SIMD3(0, 1, 0) }

        // A triangle two of whose corners became one point has no area left; it is dropped.
        for t in stride(from: 0, to: indices.count - 2, by: 3) {
            let a = remap[Int(indices[t])], b = remap[Int(indices[t + 1])], c = remap[Int(indices[t + 2])]
            if a != b, b != c, a != c { result.indices.append(contentsOf: [a, b, c]) }
        }
        return result
    }
}

/// Writes a scan's mesh as glTF binary (`mesh.glb`), and reads it back (LiDAR plan, phase 1).
///
/// **Plain glTF 2.0, without Draco.** The plan named a vendored Draco encoder to make the mesh a
/// third of the size. It is left out for now: RealityKit reads neither glTF nor Draco, so the
/// phone reads its own file with the reader below, and a Draco mesh would need a C++ decoder on
/// the phone and in every viewer as well as the encoder. A room's mesh is a few megabytes beside
/// tens of megabytes of keyframes, so the saving is small; three.js reads a plain file with no
/// extra loader.
///
/// One mesh, one primitive: positions and normals as 32-bit floats, indices as 32-bit unsigned
/// integers, and in phase 2 texture coordinates and one embedded JPEG.
public enum MeshExporter {

    public enum GLBError: Error, Equatable, LocalizedError {
        case notGLB
        case unsupported(String)
        case truncated

        public var errorDescription: String? {
            switch self {
            case .notGLB: "That isn't a glTF model."
            case .unsupported(let what): "The model uses \(what), which this app doesn't read."
            case .truncated: "The model is incomplete."
            }
        }
    }

    // MARK: Writing

    /// The mesh as a `.glb` file's bytes, with `texture` (a JPEG) embedded when the mesh has texture coordinates.
    public static func glb(_ mesh: ScanMesh, texture: Data? = nil, generator: String = "IsHaunted LiDAR scan") -> Data {
        var binary = Data()
        var views: [GLTF.BufferView] = []
        var accessors: [GLTF.Accessor] = []

        func view(_ bytes: Data, target: Int?) -> Int {
            while binary.count % 4 != 0 { binary.append(0) }
            views.append(GLTF.BufferView(buffer: 0, byteOffset: binary.count, byteLength: bytes.count, target: target))
            binary.append(bytes)
            return views.count - 1
        }

        let bounds = mesh.bounds ?? (SIMD3<Float>(repeating: 0), SIMD3<Float>(repeating: 0))
        accessors.append(GLTF.Accessor(
            bufferView: view(floats(mesh.positions.flatMap { [$0.x, $0.y, $0.z] }), target: 34962),
            componentType: 5126, count: mesh.positions.count, type: "VEC3",
            min: [bounds.min.x, bounds.min.y, bounds.min.z], max: [bounds.max.x, bounds.max.y, bounds.max.z]))
        var attributes = ["POSITION": 0]

        if mesh.normals.count == mesh.positions.count, !mesh.normals.isEmpty {
            accessors.append(GLTF.Accessor(
                bufferView: view(floats(mesh.normals.flatMap { [$0.x, $0.y, $0.z] }), target: 34962),
                componentType: 5126, count: mesh.normals.count, type: "VEC3", min: nil, max: nil))
            attributes["NORMAL"] = accessors.count - 1
        }

        var material: Int?
        var images: [GLTF.Image]?
        var textures: [GLTF.Texture]?
        var materials: [GLTF.Material]?
        if let uvs = mesh.uvs, uvs.count == mesh.positions.count, let texture {
            accessors.append(GLTF.Accessor(
                bufferView: view(floats(uvs.flatMap { [$0.x, $0.y] }), target: 34962),
                componentType: 5126, count: uvs.count, type: "VEC2", min: nil, max: nil))
            attributes["TEXCOORD_0"] = accessors.count - 1
            images = [GLTF.Image(bufferView: view(texture, target: nil), mimeType: "image/jpeg")]
            textures = [GLTF.Texture(source: 0)]
            materials = [GLTF.Material(pbrMetallicRoughness: .init(baseColorTexture: .init(index: 0),
                                                                   metallicFactor: 0, roughnessFactor: 1))]
            material = 0
        }

        accessors.append(GLTF.Accessor(
            bufferView: view(uint32s(mesh.indices), target: 34963),
            componentType: 5125, count: mesh.indices.count, type: "SCALAR", min: nil, max: nil))

        while binary.count % 4 != 0 { binary.append(0) }
        let document = GLTF(
            asset: .init(version: "2.0", generator: generator),
            scene: 0, scenes: [.init(nodes: [0])], nodes: [.init(mesh: 0)],
            meshes: [.init(primitives: [.init(attributes: attributes, indices: accessors.count - 1,
                                              material: material, mode: 4)])],
            accessors: accessors, bufferViews: views, buffers: [.init(byteLength: binary.count)],
            materials: materials, textures: textures, images: images)

        var json = (try? JSONEncoder().encode(document)) ?? Data("{}".utf8)
        while json.count % 4 != 0 { json.append(0x20) }

        var file = Data()
        file.append(littleEndian: UInt32(0x4654_6C67))          // "glTF"
        file.append(littleEndian: UInt32(2))
        file.append(littleEndian: UInt32(12 + 8 + json.count + 8 + binary.count))
        file.append(littleEndian: UInt32(json.count))
        file.append(littleEndian: UInt32(0x4E4F_534A))          // "JSON"
        file.append(json)
        file.append(littleEndian: UInt32(binary.count))
        file.append(littleEndian: UInt32(0x004E_4942))          // "BIN\0"
        file.append(binary)
        return file
    }

    // MARK: Reading

    /// What a `.glb` holds: the mesh, and its texture when it has one.
    public struct Contents: Sendable {
        public var mesh: ScanMesh
        public var texture: Data?

        public init(mesh: ScanMesh, texture: Data?) {
            self.mesh = mesh
            self.texture = texture
        }
    }

    /// Reads a `.glb` this app wrote — one mesh, float positions, 16- or 32-bit indices.
    public static func read(_ data: Data) throws -> Contents {
        guard data.count >= 20, data.u32le(at: 0) == 0x4654_6C67 else { throw GLBError.notGLB }
        guard data.u32le(at: 4) == 2 else { throw GLBError.unsupported("a glTF version other than 2") }

        let jsonLength = Int(data.u32le(at: 12))
        guard data.u32le(at: 16) == 0x4E4F_534A, 20 + jsonLength <= data.count else { throw GLBError.truncated }
        let json = data.subdata(in: (data.startIndex + 20)..<(data.startIndex + 20 + jsonLength))
        let document = try JSONDecoder().decode(GLTF.self, from: json)

        var binary = Data()
        let binaryHeader = 20 + jsonLength
        if binaryHeader + 8 <= data.count {
            let length = Int(data.u32le(at: binaryHeader))
            guard binaryHeader + 8 + length <= data.count else { throw GLBError.truncated }
            binary = data.subdata(in: (data.startIndex + binaryHeader + 8)..<(data.startIndex + binaryHeader + 8 + length))
        }

        guard let primitive = document.meshes.first?.primitives.first else { throw GLBError.unsupported("no mesh") }
        guard primitive.mode == nil || primitive.mode == 4 else { throw GLBError.unsupported("primitives that are not triangles") }

        func bytes(of accessorIndex: Int) throws -> (accessor: GLTF.Accessor, bytes: Data) {
            guard accessorIndex < document.accessors.count else { throw GLBError.truncated }
            let accessor = document.accessors[accessorIndex]
            guard accessor.bufferView < document.bufferViews.count else { throw GLBError.truncated }
            let view = document.bufferViews[accessor.bufferView]
            guard view.byteOffset + view.byteLength <= binary.count else { throw GLBError.truncated }
            return (accessor, binary.subdata(in: (binary.startIndex + view.byteOffset)..<(binary.startIndex + view.byteOffset + view.byteLength)))
        }

        func vectors(_ accessorIndex: Int, width: Int) throws -> [[Float]] {
            let (accessor, raw) = try bytes(of: accessorIndex)
            guard accessor.componentType == 5126 else { throw GLBError.unsupported("compressed vertex data") }
            let values = raw.floatsLE()
            guard values.count >= accessor.count * width else { throw GLBError.truncated }
            return (0..<accessor.count).map { Array(values[($0 * width)..<($0 * width + width)]) }
        }

        var mesh = ScanMesh()
        guard let position = primitive.attributes["POSITION"] else { throw GLBError.unsupported("a mesh without positions") }
        mesh.positions = try vectors(position, width: 3).map { SIMD3($0[0], $0[1], $0[2]) }
        if let normal = primitive.attributes["NORMAL"] {
            mesh.normals = try vectors(normal, width: 3).map { SIMD3($0[0], $0[1], $0[2]) }
        }
        if let uv = primitive.attributes["TEXCOORD_0"] {
            mesh.uvs = try vectors(uv, width: 2).map { SIMD2($0[0], $0[1]) }
        }

        if let indices = primitive.indices {
            let (accessor, raw) = try bytes(of: indices)
            switch accessor.componentType {
            case 5125: mesh.indices = Array(raw.uint32sLE().prefix(accessor.count))
            case 5123: mesh.indices = raw.uint16sLE().prefix(accessor.count).map(UInt32.init)
            default: throw GLBError.unsupported("indices of that size")
            }
        } else {
            mesh.indices = (0..<UInt32(mesh.positions.count)).map { $0 }
        }
        guard mesh.indices.allSatisfy({ Int($0) < mesh.positions.count }) else { throw GLBError.truncated }

        var texture: Data?
        if let image = document.images?.first, let viewIndex = image.bufferView, viewIndex < document.bufferViews.count {
            let view = document.bufferViews[viewIndex]
            if view.byteOffset + view.byteLength <= binary.count {
                texture = binary.subdata(in: (binary.startIndex + view.byteOffset)..<(binary.startIndex + view.byteOffset + view.byteLength))
            }
        }
        return Contents(mesh: mesh, texture: texture)
    }

    // MARK: Bytes

    private static func floats(_ values: [Float]) -> Data {
        var data = Data(capacity: values.count * 4)
        for value in values { data.append(littleEndian: value.bitPattern) }
        return data
    }

    private static func uint32s(_ values: [UInt32]) -> Data {
        var data = Data(capacity: values.count * 4)
        for value in values { data.append(littleEndian: value) }
        return data
    }
}

/// The parts of a glTF document this app writes and reads.
struct GLTF: Codable {
    struct Asset: Codable { var version: String; var generator: String? }
    struct Scene: Codable { var nodes: [Int] }
    struct Node: Codable { var mesh: Int? }
    struct Mesh: Codable { var primitives: [Primitive] }
    struct Primitive: Codable {
        var attributes: [String: Int]
        var indices: Int?
        var material: Int?
        var mode: Int?
    }
    struct Accessor: Codable {
        var bufferView: Int
        var componentType: Int
        var count: Int
        var type: String
        var min: [Float]?
        var max: [Float]?
    }
    struct BufferView: Codable {
        var buffer: Int
        var byteOffset: Int
        var byteLength: Int
        var target: Int?
    }
    struct Buffer: Codable { var byteLength: Int }
    struct Image: Codable { var bufferView: Int?; var mimeType: String? }
    struct Texture: Codable { var source: Int }
    struct Material: Codable {
        struct PBR: Codable {
            struct TextureRef: Codable { var index: Int }
            var baseColorTexture: TextureRef?
            var metallicFactor: Float?
            var roughnessFactor: Float?
        }
        var pbrMetallicRoughness: PBR?
    }

    var asset: Asset
    var scene: Int?
    var scenes: [Scene]?
    var nodes: [Node]?
    var meshes: [Mesh]
    var accessors: [Accessor]
    var bufferViews: [BufferView]
    var buffers: [Buffer]
    var materials: [Material]?
    var textures: [Texture]?
    var images: [Image]?
}

extension Data {
    func u32le(at offset: Int) -> UInt32 {
        let start = startIndex + offset
        return UInt32(self[start]) | UInt32(self[start + 1]) << 8 | UInt32(self[start + 2]) << 16 | UInt32(self[start + 3]) << 24
    }

    func floatsLE() -> [Float] {
        stride(from: 0, to: count - 3, by: 4).map { Float(bitPattern: u32le(at: $0)) }
    }

    func uint32sLE() -> [UInt32] {
        stride(from: 0, to: count - 3, by: 4).map { u32le(at: $0) }
    }

    func uint16sLE() -> [UInt16] {
        stride(from: 0, to: count - 1, by: 2).map { UInt16(self[startIndex + $0]) | UInt16(self[startIndex + $0 + 1]) << 8 }
    }
}
