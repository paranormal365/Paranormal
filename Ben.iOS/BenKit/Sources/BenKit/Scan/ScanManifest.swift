import Foundation

/// What a `.scan` file says about itself — its `manifest.json` (LiDAR plan, §3).
///
/// The server checks this against the file's members before a scan is kept
/// (`PlaceScanManifest.ReadAsync`), so every name here is one it knows. Keys are camelCase, like
/// the rest of the API.
public struct ScanManifest: Codable, Sendable, Equatable {

    /// The one format a phone writes and the server reads. A new major format is a new string.
    public static let formatName = "ishaunted-scan/1"

    public enum Kind: String, Codable, Sendable {
        /// A room scanned for the place.
        case baseline
        /// Only what changed against a place's scan, kept in a session (phase 2).
        case comparison
    }

    public enum Retention: String, Codable, Sendable, CaseIterable {
        /// The model, the world map and the keyframe pictures. The default.
        case modelAndPictures
        /// The model and the world map only, for an account short of room.
        case modelOnly
    }

    /// The coordinate frame the model is in: ARKit's world origin, y up, meters.
    public struct Frame: Codable, Sendable, Equatable {
        public var origin: [Double]
        public var up: [Double]
        public static let arkit = Frame(origin: [0, 0, 0], up: [0, 1, 0])
    }

    /// What lists show without opening the model.
    public struct Stats: Codable, Sendable, Equatable {
        public var floorAreaM2: Double?
        public var triangles: Int?
        public init(floorAreaM2: Double?, triangles: Int?) {
            self.floorAreaM2 = floorAreaM2
            self.triangles = triangles
        }
    }

    /// What the file carries, by role. Names are fixed; the server refuses any other.
    public struct Entries: Codable, Sendable, Equatable {
        public var roomplan: String?
        public var roomModel: String?
        public var mesh: String?
        public var worldmap: String?
        public var preview: String?
        public var keyframes: Int
        public init(roomplan: String? = nil, roomModel: String? = nil, mesh: String? = nil,
                    worldmap: String? = nil, preview: String? = nil, keyframes: Int = 0) {
            self.roomplan = roomplan
            self.roomModel = roomModel
            self.mesh = mesh
            self.worldmap = worldmap
            self.preview = preview
            self.keyframes = keyframes
        }
    }

    /// The place's scan a comparison was made against (phase 2).
    public struct Baseline: Codable, Sendable, Equatable {
        public var placeScanId: UUID
        public var version: Int
        public init(placeScanId: UUID, version: Int) {
            self.placeScanId = placeScanId
            self.version = version
        }
    }

    public var format: String
    public var kind: Kind
    public var room: String
    public var placeId: UUID?
    public var scannedAt: Date
    public var device: String?
    public var scannerVersion: Int
    public var retention: Retention
    public var frame: Frame
    public var stats: Stats?
    public var entries: Entries
    public var baseline: Baseline?

    public init(kind: Kind = .baseline, room: String, placeId: UUID?, scannedAt: Date,
                device: String?, scannerVersion: Int, retention: Retention,
                frame: Frame = .arkit, stats: Stats?, entries: Entries, baseline: Baseline? = nil) {
        self.format = Self.formatName
        self.kind = kind
        self.room = room
        self.placeId = placeId
        self.scannedAt = scannedAt
        self.device = device
        self.scannerVersion = scannerVersion
        self.retention = retention
        self.frame = frame
        self.stats = stats
        self.entries = entries
        self.baseline = baseline
    }

    /// The coder pair a manifest is written and read with: ISO-8601 dates, keys in a stable order.
    public static let encoder: JSONEncoder = {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.sortedKeys, .prettyPrinted]
        return encoder
    }()

    public static let decoder: JSONDecoder = {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        return decoder
    }()
}
