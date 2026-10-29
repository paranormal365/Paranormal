import Foundation

/// One keyframe as the scan's `keyframes/index.json` lists it.
public struct ScanKeyframe: Codable, Sendable, Equatable {
    /// `0001.jpg` — the picture, beside the index.
    public var file: String
    /// `0001.depth` — its depth map, when ARKit had one.
    public var depth: String?
    public var depthWidth: Int?
    public var depthHeight: Int?
    /// The camera's transform in the scan's frame, sixteen numbers, column-major.
    public var pose: [Float]
    /// The camera's intrinsics for the picture, nine numbers, column-major.
    public var intrinsics: [Float]
    public var imageWidth: Int
    public var imageHeight: Int
    /// Seconds since the scan began.
    public var at: Double

    public init(file: String, depth: String?, depthWidth: Int?, depthHeight: Int?, pose: [Float],
                intrinsics: [Float], imageWidth: Int, imageHeight: Int, at: Double) {
        self.file = file
        self.depth = depth
        self.depthWidth = depthWidth
        self.depthHeight = depthHeight
        self.pose = pose
        self.intrinsics = intrinsics
        self.imageWidth = imageWidth
        self.imageHeight = imageHeight
        self.at = at
    }
}

/// A finished scan on the phone, before it is packed — the folder the scanner wrote, and what it
/// knows about it (LiDAR plan, phase 1).
///
/// Kept apart from the `.scan` because the person chooses what to keep after they see the sizes:
/// "Model and pictures" packs everything, "Model only" leaves the keyframes out. The folder stays
/// until the server has the scan.
public struct ScanCapture: Codable, Sendable, Equatable, Identifiable {
    public var id: UUID
    public var room: String
    public var placeId: UUID?
    /// The field session it was made in, if any — for the `room_scan` reading.
    public var sessionId: UUID?
    public var startedAt: Date
    public var scannedAt: Date
    public var device: String?
    public var scannerVersion: Int
    public var floorAreaM2: Double?
    public var triangleCount: Int
    public var keyframeCount: Int

    public init(id: UUID = UUID(), room: String, placeId: UUID?, sessionId: UUID?, startedAt: Date,
                scannedAt: Date, device: String?, scannerVersion: Int, floorAreaM2: Double?,
                triangleCount: Int, keyframeCount: Int) {
        self.id = id
        self.room = room
        self.placeId = placeId
        self.sessionId = sessionId
        self.startedAt = startedAt
        self.scannedAt = scannedAt
        self.device = device
        self.scannerVersion = scannerVersion
        self.floorAreaM2 = floorAreaM2
        self.triangleCount = triangleCount
        self.keyframeCount = keyframeCount
    }

    /// How long the scanning took.
    public var durationSeconds: Double { max(0, scannedAt.timeIntervalSince(startedAt)) }
}

/// Writes and reads a room's `.scan` file (LiDAR plan, §3).
///
/// One stored ZIP, written by the same `ZipWriter` as a session's `.ben`, so the server reads it
/// in place and serves its members by range. The member names are fixed — the server refuses
/// any other — and the manifest goes first.
public enum ScanPackage {

    public static let fileExtension = "scan"
    public static let contentType = "application/vnd.ishaunted.room-scan"

    /// The pipeline's version, pinned like the EVP detector's: a scan says which code made it.
    public static let scannerVersion = 1

    /// The most keyframes a scan keeps — the server's limit.
    public static let mostKeyframes = 500

    /// A picture and a depth map per keyframe, the fixed members, and a little room.
    public static let mostEntries = mostKeyframes * 2 + 16

    public static let manifestEntry = "manifest.json"
    public static let meshEntry = "mesh.glb"
    public static let roomPlanEntry = "room.usdz"
    public static let roomModelEntry = "room.json"
    public static let worldMapEntry = "worldmap.arworldmap"
    public static let previewEntry = "preview.jpg"
    public static let keyframeIndexEntry = "keyframes/index.json"
    /// What the capture folder calls its record of itself; never packed.
    public static let captureRecord = "capture.json"

    public enum PackageError: Error, Equatable, LocalizedError {
        case missingMesh
        case notAScan(String)
        case tooLarge

        public var errorDescription: String? {
            switch self {
            case .missingMesh: "The scan has no model of the room, so there is nothing to send."
            case .notAScan(let why): "That isn't a room scan this app can open: \(why)"
            case .tooLarge: "This scan is too big to send. Keep the model only, or scan a smaller part of the room."
            }
        }
    }

    /// What would go into the file for a retention choice: path inside the scan, file on disk.
    public static func members(of folder: URL, retention: ScanManifest.Retention) -> [(path: String, file: URL)] {
        let fm = FileManager.default
        var members: [(String, URL)] = []
        for name in [roomPlanEntry, roomModelEntry, meshEntry, worldMapEntry, previewEntry] {
            let url = folder.appendingPathComponent(name)
            if fm.fileExists(atPath: url.path) { members.append((name, url)) }
        }
        if retention == .modelAndPictures {
            let keyframes = folder.appendingPathComponent("keyframes")
            let index = keyframes.appendingPathComponent("index.json")
            if fm.fileExists(atPath: index.path) {
                members.append((keyframeIndexEntry, index))
                let names = ((try? fm.contentsOfDirectory(atPath: keyframes.path)) ?? [])
                    .filter { $0.hasSuffix(".jpg") || $0.hasSuffix(".depth") }
                    .sorted()
                members.append(contentsOf: names.map { ("keyframes/\($0)", keyframes.appendingPathComponent($0)) })
            }
        }
        return members
    }

    /// What the `.scan` will weigh for a retention choice, before it is written — the number the
    /// retention sheet shows beside each choice.
    public static func estimatedBytes(of folder: URL, retention: ScanManifest.Retention) -> Int64 {
        let fm = FileManager.default
        return members(of: folder, retention: retention).reduce(Int64(2_048)) { total, member in
            let size = (try? fm.attributesOfItem(atPath: member.file.path)[.size] as? NSNumber)?.int64Value ?? 0
            return total + size + Int64(76 + 2 * member.path.utf8.count)
        }
    }

    /// Packs a capture folder into a `.scan` at `destination`, keeping what `retention` says.
    @discardableResult
    public static func write(_ capture: ScanCapture, from folder: URL, retention: ScanManifest.Retention,
                             kind: ScanManifest.Kind = .baseline, baseline: ScanManifest.Baseline? = nil,
                             to destination: URL) throws -> Int64 {
        let members = members(of: folder, retention: retention)
        guard members.contains(where: { $0.path == meshEntry }) else { throw PackageError.missingMesh }

        let keyframes = members.filter { $0.path.hasPrefix("keyframes/") && $0.path.hasSuffix(".jpg") }.count
        let manifest = ScanManifest(
            kind: kind, room: capture.room, placeId: capture.placeId, scannedAt: capture.scannedAt,
            device: capture.device, scannerVersion: capture.scannerVersion, retention: retention,
            stats: .init(floorAreaM2: capture.floorAreaM2, triangles: capture.triangleCount),
            entries: .init(
                roomplan: members.contains { $0.path == roomPlanEntry } ? roomPlanEntry : nil,
                roomModel: members.contains { $0.path == roomModelEntry } ? roomModelEntry : nil,
                mesh: meshEntry,
                worldmap: members.contains { $0.path == worldMapEntry } ? worldMapEntry : nil,
                preview: members.contains { $0.path == previewEntry } ? previewEntry : nil,
                keyframes: keyframes),
            baseline: baseline)

        let entries = [ZipWriter.Entry(path: manifestEntry, data: try ScanManifest.encoder.encode(manifest))]
            + members.map { ZipWriter.Entry(path: $0.path, file: $0.file) }
        do {
            return try ZipWriter().write(entries, to: destination)
        } catch ZipWriterError.tooLarge {
            try? FileManager.default.removeItem(at: destination)
            throw PackageError.tooLarge
        }
    }

    /// A `.scan` opened for reading: its manifest and its members, read in place.
    public struct Reader: Sendable {
        public let zip: ZipReader
        public let manifest: ScanManifest

        public init(url: URL) throws {
            do {
                zip = try ZipReader(url: url, mostEntries: ScanPackage.mostEntries)
            } catch {
                throw PackageError.notAScan(error.localizedDescription)
            }
            guard let entry = zip.entry(manifestEntry) else { throw PackageError.notAScan("it has no manifest.") }
            do {
                manifest = try ScanManifest.decoder.decode(ScanManifest.self, from: try zip.data(of: entry))
            } catch {
                throw PackageError.notAScan("its manifest can't be read.")
            }
            guard manifest.format == ScanManifest.formatName else {
                throw PackageError.notAScan("it is in format \(manifest.format).")
            }
        }

        /// A member's bytes — the mesh, the room model, the keyframe index.
        public func data(_ path: String) throws -> Data? {
            guard let entry = zip.entry(path) else { return nil }
            return try zip.data(of: entry)
        }

        /// The mesh, read from the scan.
        public func mesh() throws -> MeshExporter.Contents {
            guard let data = try data(meshEntry) else { throw PackageError.missingMesh }
            return try MeshExporter.read(data)
        }

        public func roomModel() throws -> ScanRoomModel? {
            guard let data = try data(roomModelEntry) else { return nil }
            return try JSONDecoder().decode(ScanRoomModel.self, from: data)
        }

        public func keyframes() throws -> [ScanKeyframe] {
            guard let data = try data(keyframeIndexEntry) else { return [] }
            return try JSONDecoder().decode([ScanKeyframe].self, from: data)
        }
    }
}

/// Where scans live on the phone until the server has them, and the place scans fetched to view
/// (LiDAR plan, phase 1).
///
/// Outside the sessions' own folders on purpose: a session's `.ben` carries what is in its folder,
/// and a room's scan belongs to the place, not to the session that happened to be running.
public struct ScanStore: Sendable {
    public let root: URL

    public init(root: URL) {
        self.root = root
    }

    /// The app's own: Application Support/Scans.
    public static func applicationSupport() -> ScanStore {
        let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
        return ScanStore(root: base.appendingPathComponent("Scans", isDirectory: true))
    }

    /// A new capture's folder, made empty.
    public func newCaptureFolder(_ id: UUID) throws -> URL {
        let folder = captureFolder(id)
        try FileManager.default.createDirectory(at: folder.appendingPathComponent("keyframes"),
                                                withIntermediateDirectories: true)
        return folder
    }

    public func captureFolder(_ id: UUID) -> URL {
        root.appendingPathComponent("captures", isDirectory: true)
            .appendingPathComponent(id.uuidString.lowercased(), isDirectory: true)
    }

    public func save(_ capture: ScanCapture) throws {
        let data = try ScanManifest.encoder.encode(capture)
        try data.write(to: captureFolder(capture.id).appendingPathComponent(ScanPackage.captureRecord), options: .atomic)
    }

    public func capture(_ id: UUID) -> ScanCapture? {
        let url = captureFolder(id).appendingPathComponent(ScanPackage.captureRecord)
        guard let data = try? Data(contentsOf: url) else { return nil }
        return try? ScanManifest.decoder.decode(ScanCapture.self, from: data)
    }

    /// Scans made on this phone that the server does not have yet, newest first.
    public func pending() -> [ScanCapture] {
        let folder = root.appendingPathComponent("captures", isDirectory: true)
        let names = (try? FileManager.default.contentsOfDirectory(atPath: folder.path)) ?? []
        return names.compactMap { UUID(uuidString: $0) }.compactMap(capture).sorted { $0.scannedAt > $1.scannedAt }
    }

    /// Where a capture is packed for sending.
    public func packageURL(for id: UUID) -> URL {
        captureFolder(id).appendingPathComponent("room.\(ScanPackage.fileExtension)")
    }

    /// Forgets a capture once the server has it, or the person discarded it.
    public func remove(_ id: UUID) {
        try? FileManager.default.removeItem(at: captureFolder(id))
    }

    /// Where a place's scan is kept once fetched, to view and to compare against.
    public func placeScanURL(_ scanId: UUID) -> URL {
        root.appendingPathComponent("place-scans", isDirectory: true)
            .appendingPathComponent("\(scanId.uuidString.lowercased()).\(ScanPackage.fileExtension)")
    }

    /// Keeps a just-uploaded scan as the place's copy, so it opens without downloading it again.
    public func keepAsPlaceScan(_ file: URL, scanId: UUID) throws {
        let destination = placeScanURL(scanId)
        try FileManager.default.createDirectory(at: destination.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? FileManager.default.removeItem(at: destination)
        try FileManager.default.copyItem(at: file, to: destination)
    }
}
