import Foundation

// ── The server's records for a place's room scans (LiDAR plan, phase 1) ──────────────────────
// Ben.Service.Models/Entities/PlaceScanRecords.cs. Held to Fixtures/place-scans/*.json, which
// PlaceScanFixtureTests captures from the real controller — never written by hand.

/// Where one version of a room's scan stands. The server sends the number.
public enum PlaceScanState: Int, Codable, Sendable {
    case current = 1, previous = 2, offered = 3, declined = 4, removed = 5
}

/// The rooms at a place and what the caller may do in each — the room picker's source.
public struct PlaceScanRoomList: Codable, Sendable, Equatable {
    public var placeId: UUID
    public var placeName: String?
    /// 1 a private residence, 2 a public location.
    public var kind: Int
    public var mayScan: Bool
    public var whyCannotScan: String?
    public var custodian: PlaceScanCustodian?
    /// What the person's own account can still store; nil when nothing caps it.
    public var storageLeftBytes: Int64?
    public var rooms: [PlaceScanRoom]

    /// The room with this name, however it was typed.
    public func room(named name: String?) -> PlaceScanRoom? {
        let key = RoomKey.key(name)
        return key.isEmpty ? nil : rooms.first { $0.roomKey == key }
    }
}

/// One room at a place.
public struct PlaceScanRoom: Codable, Sendable, Equatable, Identifiable {
    public var roomKey: String
    public var roomName: String
    public var current: PlaceScanSummary?
    public var canScan: Bool
    public var canCompare: Bool
    public var canReplace: Bool
    public var replaceReason: String?
    public var holdBy: PlaceScanHold?
    public var offersWaiting: Int
    public var placeRoomId: UUID?

    public var id: String { roomKey }
}

/// One version of a room's scan, as a list shows it.
public struct PlaceScanSummary: Codable, Sendable, Equatable, Identifiable {
    public var id: UUID
    public var version: Int
    public var state: PlaceScanState
    public var retention: Int
    public var scannedUtc: Date
    public var scannedBy: String
    public var scannedByYou: Bool
    public var fileSize: Int64
    public var keyframeCount: Int
    public var floorAreaM2: Double?
    public var triangleCount: Int
}

/// Somebody scanning a room right now.
public struct PlaceScanHold: Codable, Sendable, Equatable {
    /// Only on the person's own hold.
    public var holdId: UUID?
    public var roomKey: String
    public var roomName: String
    public var heldBy: String
    public var isYou: Bool
    public var until: Date
}

/// Who looks after a place's scans.
public struct PlaceScanCustodian: Codable, Sendable, Equatable {
    /// `venue`, `grant`, `scanner` or `group`.
    public var kind: String
    public var name: String
    public var isYou: Bool
}

/// One version of a room's scan in full.
public struct PlaceScanRecord: Codable, Sendable, Equatable, Identifiable {
    public var id: UUID
    public var placeId: UUID
    public var roomKey: String
    public var roomName: String
    public var version: Int
    public var state: PlaceScanState
    public var retention: Int
    public var scannedUtc: Date
    public var scannedBy: String
    public var scannedByYou: Bool
    public var fileSize: Int64
    public var sha256: String
    public var deviceModel: String?
    public var scannerVersion: Int
    public var keyframeCount: Int
    public var floorAreaM2: Double?
    public var triangleCount: Int
    public var shownOnPlacePage: Bool
    public var decidedUtc: Date?
    public var decisionNote: String?
    public var entries: [PlaceScanEntry]
}

/// One member of a `.scan`, servable by its path.
public struct PlaceScanEntry: Codable, Sendable, Equatable {
    public var path: String
    public var length: Int64
    public var contentType: String
}

/// What an upload became.
public struct PlaceScanUploadResult: Codable, Sendable, Equatable {
    /// `current`, `replaced` or `offered`.
    public var outcome: String
    public var says: String
    public var scan: PlaceScanRecord
}

/// The answer when a room already has a scan the person may not replace. The phone still has
/// its file; `choices` are what it may do with it.
public struct PlaceScanConflict: Codable, Sendable, Equatable {
    public var reason: String
    public var says: String
    public var roomName: String
    public var current: PlaceScanSummary
    public var mayReplace: Bool
    public var replaceReason: String?
    /// `compare`, `offer`, `discard`, and `replace` when allowed.
    public var choices: [String]
}

/// The answer when somebody else is scanning the room.
public struct PlaceScanHeld: Codable, Sendable, Equatable {
    public var reason: String
    public var says: String
    public var heldBy: String
    public var until: Date
}

/// A place's room scans, on the server (LiDAR plan, phase 1, §4).
///
/// Every refusal the server writes is a sentence meant for the person holding the phone, and it
/// reaches them as written — including the two that come with choices.
public struct PlaceScanClient: Sendable {

    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    /// What an upload is meant to be.
    public enum Intent: String, Sendable {
        /// The room's first scan — or, for whoever may replace it, its new one.
        case baseline
        /// A replacement the person believes they may make.
        case replace
        /// A replacement for the custodian to accept or decline.
        case offer
    }

    public enum HoldOutcome: Sendable, Equatable {
        case held(PlaceScanHold)
        case heldByOther(PlaceScanHeld)
        case alreadyScanned(PlaceScanConflict)
        case failed(FeedActionError)
    }

    public enum UploadOutcome: Sendable, Equatable {
        case uploaded(PlaceScanUploadResult)
        case conflict(PlaceScanConflict)
        case failed(FeedActionError)
    }

    private static func path(_ placeId: UUID, _ rest: String = "") -> String {
        "api/places/\(placeId.uuidString.lowercased())/scans\(rest)"
    }

    /// What naming a public location answered: the place, and whether it was already there.
    public struct NamedPlace: Codable, Sendable, Equatable {
        public var id: UUID
        public var alreadyExisted: Bool
        public var says: String
    }

    /// Names a public location to scan at, when the picker had nothing near — `api/places/public-location`,
    /// the same door the website uses. An address that already exists comes back as that place
    /// rather than a second one; somebody's home at that address is refused in words.
    public func namePlace(name: String, city: String, state: String,
                          latitude: Double?, longitude: Double?) async -> Result<NamedPlace, FeedActionError> {
        struct Body: Encodable {
            let name: String, city: String, state: String
            let latitude: Double?, longitude: Double?
        }
        guard let endpoint = try? Endpoint.json(.post, "api/places/public-location",
                                                payload: Body(name: name, city: city, state: state,
                                                              latitude: latitude, longitude: longitude))
        else { return .failure(.failed(reason: nil)) }
        switch await api.load(endpoint, as: NamedPlace.self) {
        case .ok(let place): return .success(place)
        case .failed(let reason, _): return .failure(.failed(reason: reason))
        case .sessionEnded: return .failure(.sessionEnded)
        case .rateLimited(let after): return .failure(.rateLimited(retryAfter: after))
        }
    }

    /// The rooms at a place, their scans, and who is scanning which.
    public func rooms(placeId: UUID) async -> LoadResult<PlaceScanRoomList> {
        await api.load(Endpoint(.get, Self.path(placeId)), as: PlaceScanRoomList.self)
    }

    /// Takes the room for twenty minutes, so the next person is told somebody is scanning it.
    public func takeHold(placeId: UUID, room: String) async -> HoldOutcome {
        struct Body: Encodable { let room: String }
        guard let endpoint = try? Endpoint.json(.post, Self.path(placeId, "/holds"), payload: Body(room: room)),
              let (data, status) = await api.loadRaw(endpoint)
        else { return .failed(.failed(reason: nil)) }

        switch status {
        case 200..<300:
            guard let hold = try? BenJSON.decoder.decode(PlaceScanHold.self, from: data)
            else { return .failed(.failed(reason: "The server's answer couldn't be read.")) }
            return .held(hold)
        case 409:
            if let held = try? BenJSON.decoder.decode(PlaceScanHeld.self, from: data), held.reason == "held" {
                return .heldByOther(held)
            }
            if let conflict = try? BenJSON.decoder.decode(PlaceScanConflict.self, from: data) {
                return .alreadyScanned(conflict)
            }
            return .failed(Self.failure(status, data))
        default:
            return .failed(Self.failure(status, data))
        }
    }

    /// Lets the room go early — the scan was canceled.
    public func releaseHold(placeId: UUID, holdId: UUID) async {
        _ = await api.send(Endpoint(.delete, Self.path(placeId, "/holds/\(holdId.uuidString.lowercased())")))
    }

    /// Sends a `.scan` for a room. Streamed from disk; the phone keeps the file until this says
    /// the server has it.
    public func upload(placeId: UUID, file: URL, room: String, retention: ScanManifest.Retention,
                       holdId: UUID?, intent: Intent) async -> UploadOutcome {
        var parts: [MultipartBody.Part] = [
            .file("file", filename: "room.\(ScanPackage.fileExtension)", contentType: ScanPackage.contentType, url: file),
            .field("room", room),
            .field("retention", retention.rawValue),
            .field("intent", intent.rawValue),
        ]
        if let holdId { parts.append(.field("holdId", holdId.uuidString)) }

        let endpoint = Endpoint(.post, Self.path(placeId), body: .multipart(MultipartBody(parts: parts)))
        guard let (data, status) = await api.uploadRaw(endpoint) else { return .failed(.failed(reason: nil)) }

        switch status {
        case 200..<300:
            guard let result = try? BenJSON.decoder.decode(PlaceScanUploadResult.self, from: data)
            else { return .failed(.failed(reason: "The scan was sent, but the server's answer couldn't be read.")) }
            return .uploaded(result)
        case 409:
            if let conflict = try? BenJSON.decoder.decode(PlaceScanConflict.self, from: data) {
                return .conflict(conflict)
            }
            return .failed(Self.failure(status, data))
        default:
            return .failed(Self.failure(status, data))
        }
    }

    /// One version of a room's scan, with its members.
    public func scan(placeId: UUID, scanId: UUID) async -> LoadResult<PlaceScanRecord> {
        await api.load(Endpoint(.get, Self.path(placeId, "/\(scanId.uuidString.lowercased())")), as: PlaceScanRecord.self)
    }

    /// The whole `.scan`, to view the room and compare against it.
    public func download(placeId: UUID, scanId: UUID, to destination: URL) async -> LoadResult<URL> {
        try? FileManager.default.createDirectory(at: destination.deletingLastPathComponent(), withIntermediateDirectories: true)
        return await api.download(Endpoint(.get, Self.path(placeId, "/\(scanId.uuidString.lowercased())/file")), to: destination)
    }

    /// Every version of a room's scan the person may see, newest first.
    ///
    /// The key goes into the path as it is: the request's URL escapes the spaces in it, and
    /// escaping it here as well would send "%2520".
    public func versions(placeId: UUID, roomKey: String) async -> LoadResult<[PlaceScanSummary]> {
        await api.load(Endpoint(.get, Self.path(placeId, "/rooms/\(roomKey)/versions")), as: [PlaceScanSummary].self)
    }

    /// The custodian takes an offered scan.
    public func accept(placeId: UUID, scanId: UUID, note: String?) async -> Result<PlaceScanRecord, FeedActionError> {
        await decide("accept", placeId: placeId, scanId: scanId, note: note)
    }

    /// The custodian turns an offered scan down; the note reaches its scanner.
    public func decline(placeId: UUID, scanId: UUID, note: String?) async -> Result<PlaceScanRecord, FeedActionError> {
        await decide("decline", placeId: placeId, scanId: scanId, note: note)
    }

    /// Takes a version down, or withdraws the person's own offer.
    public func remove(placeId: UUID, scanId: UUID) async -> Result<Void, FeedActionError> {
        switch await api.send(Endpoint(.delete, Self.path(placeId, "/\(scanId.uuidString.lowercased())"))) {
        case .ok: .success(())
        case .failed(let reason, _): .failure(.failed(reason: reason))
        case .sessionEnded: .failure(.sessionEnded)
        case .rateLimited(let after): .failure(.rateLimited(retryAfter: after))
        }
    }

    private func decide(_ verb: String, placeId: UUID, scanId: UUID, note: String?) async -> Result<PlaceScanRecord, FeedActionError> {
        struct Body: Encodable { let note: String? }
        guard let endpoint = try? Endpoint.json(.post, Self.path(placeId, "/\(scanId.uuidString.lowercased())/\(verb)"),
                                                payload: Body(note: note))
        else { return .failure(.failed(reason: nil)) }
        switch await api.load(endpoint, as: PlaceScanRecord.self) {
        case .ok(let record): return .success(record)
        case .failed(let reason, _): return .failure(.failed(reason: reason))
        case .sessionEnded: return .failure(.sessionEnded)
        case .rateLimited(let after): return .failure(.rateLimited(retryAfter: after))
        }
    }

    /// A refusal from a raw answer, mapped exactly as every other answer is.
    private static func failure(_ status: Int, _ data: Data) -> FeedActionError {
        switch ResponseMapping.failure(statusCode: status, data: data, headers: [:]) as LoadResult<EmptyBody> {
        case .sessionEnded: .sessionEnded
        case .rateLimited(let after): .rateLimited(retryAfter: after)
        case .failed(let reason, _): .failed(reason: reason)
        case .ok: .failed(reason: nil)
        }
    }
}
