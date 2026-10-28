import Foundation
import simd

/// A comparison scan: only what changed since the place's scan of the room, kept in the session it
/// was made in (LiDAR plan, phase 2, §3).
///
/// `scans/compare-NNN.scan` inside the session's folder, and so inside its `.ben`:
/// `manifest.json` (kind `comparison`, and which version of the place's scan it was made against),
/// `changes.json`, one picture per change (`changes/01.jpg`), and `patches.glb` — the changed
/// parts of the room's mesh only. A comparison is 0.5–3 MB where a room's scan is a hundred.
/// Publishing the session to the archive leaves the file out; its changes are readings and travel.
public enum ComparisonPackage {

    public static let changesEntry = "changes.json"
    public static let patchesEntry = "patches.glb"

    /// What a comparison found, as the session keeps it.
    public struct Result: Sendable, Equatable {
        /// The path inside the session, `scans/compare-001.scan`.
        public var relativePath: String
        public var changes: [ScanChange]
        public var bytes: Int64
    }

    /// The next free `scans/compare-NNN.scan` in a session's folder.
    public static func nextPath(in sessionFolder: URL) -> String {
        let scans = sessionFolder.appendingPathComponent("scans", isDirectory: true)
        let taken = Set((try? FileManager.default.contentsOfDirectory(atPath: scans.path)) ?? [])
        var number = 1
        while taken.contains(String(format: "compare-%03d.scan", number)) { number += 1 }
        return String(format: "scans/compare-%03d.scan", number)
    }

    /// Compares a fresh capture with the place's scan and writes the comparison into the session.
    /// Each change gets the picture of it the capture's keyframes saw best.
    public static func write(capture: ScanCapture, captureFolder: URL,
                             baseline: ScanPackage.Reader, baselineScanId: UUID, baselineVersion: Int,
                             sessionFolder: URL) throws -> Result {
        guard let baselineModel = try baseline.roomModel() else {
            throw ScanPackage.PackageError.notAScan("the room's scan has no room model to compare with.")
        }
        let baselineMesh = try baseline.mesh().mesh

        let model = try JSONDecoder().decode(ScanRoomModel.self, from: Data(contentsOf: captureFolder.appendingPathComponent(ScanPackage.roomModelEntry)))
        let mesh = try MeshExporter.read(Data(contentsOf: captureFolder.appendingPathComponent(ScanPackage.meshEntry))).mesh

        var changes = RoomComparer.compare(baseline: baselineModel, baselineMesh: baselineMesh,
                                           comparison: model, comparisonMesh: mesh)

        let relativePath = nextPath(in: sessionFolder)
        let destination = sessionFolder.appendingPathComponent(relativePath)
        try FileManager.default.createDirectory(at: destination.deletingLastPathComponent(), withIntermediateDirectories: true)

        // One picture per change, from the keyframe of this visit that saw it best.
        let keyframesFolder = captureFolder.appendingPathComponent("keyframes")
        let keyframes = (try? JSONDecoder().decode([ScanKeyframe].self,
                                                   from: Data(contentsOf: keyframesFolder.appendingPathComponent("index.json")))) ?? []
        let projections = keyframes.map(KeyframeProjection.init)
        var entries: [ZipWriter.Entry] = []
        for index in changes.indices {
            let w = changes[index].where_
            guard w.count == 3 else { continue }
            let point = SIMD3<Float>(Float(w[0]), Float(w[1]), Float(w[2]))
            guard let view = Texturer.bestView(of: point, among: projections),
                  let picture = Texturer.crop(view, around: point, folder: keyframesFolder) else { continue }
            let name = String(format: "changes/%02d.jpg", index + 1)
            entries.append(ZipWriter.Entry(path: name, data: picture))
            changes[index].picture = "\(relativePath)#\(name)"
        }

        // The changed parts of the mesh: every triangle of this visit within half a meter of a change.
        var patches = ScanMesh()
        let centers = changes.compactMap { c -> SIMD3<Float>? in
            c.where_.count == 3 ? SIMD3(Float(c.where_[0]), Float(c.where_[1]), Float(c.where_[2])) : nil
        }
        if !centers.isEmpty {
            for t in 0..<mesh.triangleCount {
                let corners = (0..<3).map { mesh.positions[Int(mesh.indices[t * 3 + $0])] }
                let center = (corners[0] + corners[1] + corners[2]) / 3
                guard centers.contains(where: { simd_distance($0, center) <= 0.5 }) else { continue }
                patches.append(positions: corners, normals: Array(repeating: simd_normalize(simd_cross(corners[1] - corners[0], corners[2] - corners[0])), count: 3),
                               indices: [0, 1, 2], transform: matrix_identity_float4x4)
            }
        }

        let manifest = ScanManifest(
            kind: .comparison, room: capture.room, placeId: capture.placeId, scannedAt: capture.scannedAt,
            device: capture.device, scannerVersion: capture.scannerVersion, retention: .modelOnly,
            stats: .init(floorAreaM2: capture.floorAreaM2, triangles: patches.triangleCount),
            entries: .init(mesh: patches.triangleCount > 0 ? patchesEntry : nil, keyframes: 0),
            baseline: .init(placeScanId: baselineScanId, version: baselineVersion))

        let encoder = JSONEncoder()
        encoder.outputFormatting = [.sortedKeys, .prettyPrinted]
        var all = [ZipWriter.Entry(path: ScanPackage.manifestEntry, data: try ScanManifest.encoder.encode(manifest)),
                   ZipWriter.Entry(path: changesEntry, data: try encoder.encode(changes))]
        all += entries
        if patches.triangleCount > 0 {
            all.append(ZipWriter.Entry(path: patchesEntry, data: MeshExporter.glb(patches)))
        }
        let bytes = try ZipWriter().write(all, to: destination)
        return Result(relativePath: relativePath, changes: changes, bytes: bytes)
    }

    /// The changes a comparison in a session found, read back from its file.
    public static func changes(in file: URL) throws -> [ScanChange] {
        let reader = try ScanPackage.Reader(url: file)
        guard let data = try reader.data(changesEntry) else { return [] }
        return try JSONDecoder().decode([ScanChange].self, from: data)
    }
}

/// Watching a phone find its place in a scanned room (LiDAR plan, phase 2).
///
/// A comparison only means something in the room scan's own frame, so the phone loads that scan's
/// world map and looks around until ARKit says it recognizes the room. This decides, from the
/// tracking states it is fed, whether it has — and gives up with a sentence a person can act on
/// rather than comparing a room against a map of somewhere it is not.
public struct RelocalizationWatch: Sendable, Equatable {

    public enum Tracking: Sendable, Equatable {
        case notAvailable, relocalizing, limited, normal
    }

    public enum Outcome: Sendable, Equatable {
        case looking
        case recognized
        case failed(String)
    }

    /// Long enough to walk a room slowly with the phone up.
    public static let patience: TimeInterval = 60

    public static let failure = "The room wasn't recognized, so this scan can't be lined up with the room's scan. "
        + "Stand where the first scan began, with the lights as they were, and try again."

    private let startedAt: Date
    private var sawRelocalizing = false
    public private(set) var outcome: Outcome = .looking

    public init(startedAt: Date) {
        self.startedAt = startedAt
    }

    /// Takes one tracking state. Recognized once ARKit goes from relocalising to normal — normal
    /// before any relocalising means the map was not used at all, which is not recognition.
    public mutating func note(_ tracking: Tracking, at moment: Date) {
        guard outcome == .looking else { return }
        switch tracking {
        case .relocalizing:
            sawRelocalizing = true
        case .normal where sawRelocalizing:
            outcome = .recognized
            return
        default:
            break
        }
        if moment.timeIntervalSince(startedAt) >= Self.patience {
            outcome = .failed(Self.failure)
        }
    }
}
