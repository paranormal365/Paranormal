import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// A place's room scans, read from the server's own answers (LiDAR plan, phase 1).
///
/// Every fixture here was captured from the real controller by `PlaceScanFixtureTests` in
/// Ben.Web.Tests, which also fails when the server's answer changes shape — so these decode what
/// the server sends, not what somebody believed it sends.
@Suite("Place scan client")
struct PlaceScanClientTests {

    private static func fixture(_ name: String) throws -> Data {
        guard let url = Bundle.module.url(forResource: name, withExtension: "json",
                                          subdirectory: "Fixtures/place-scans")
        else { throw CocoaError(.fileNoSuchFile, userInfo: [NSFilePathErrorKey: "place-scans/\(name).json"]) }
        return try Data(contentsOf: url)
    }

    private func client(_ transport: MockTransport) -> PlaceScanClient {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return PlaceScanClient(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    // MARK: The captured answers decode

    @Test func theRoomPickerReadsTheServersList() throws {
        let list = try BenJSON.decoder.decode(PlaceScanRoomList.self, from: Self.fixture("place-scan-rooms"))

        #expect(list.placeName == "The Hermitage Hotel")
        #expect(list.mayScan)
        #expect(list.custodian?.kind == "venue")
        #expect(list.storageLeftBytes == nil)
        #expect(list.rooms.map(\.roomName) == ["Room 217", "The Ballroom"])

        let scanned = try #require(list.room(named: " room  217"))
        #expect(scanned.current?.state == .current)
        #expect(scanned.current?.scannedBy == "Sarah Whitfield")
        #expect(scanned.canCompare)
        #expect(!scanned.canScan)
        #expect(scanned.offersWaiting == 1)

        let held = try #require(list.room(named: "The Ballroom"))
        #expect(held.current == nil)
        #expect(held.holdBy?.heldBy == "Dana Reyes")
        #expect(held.holdBy?.isYou == false)
        #expect(held.holdBy?.holdId == nil)
    }

    @Test func everyOtherAnswerDecodes() throws {
        let hold = try BenJSON.decoder.decode(PlaceScanHold.self, from: Self.fixture("place-scan-hold"))
        #expect(hold.isYou && hold.holdId != nil)

        let upload = try BenJSON.decoder.decode(PlaceScanUploadResult.self, from: Self.fixture("place-scan-upload"))
        #expect(upload.outcome == "current")
        #expect(upload.scan.entries.contains { $0.path == "mesh.glb" && $0.contentType == "model/gltf-binary" })

        let offered = try BenJSON.decoder.decode(PlaceScanUploadResult.self, from: Self.fixture("place-scan-offered"))
        #expect(offered.outcome == "offered")
        #expect(offered.scan.state == .offered)

        let conflict = try BenJSON.decoder.decode(PlaceScanConflict.self, from: Self.fixture("place-scan-conflict"))
        #expect(conflict.choices == ["compare", "offer", "discard"])
        #expect(conflict.replaceReason?.contains("decides whether a new scan replaces it") == true)

        let held = try BenJSON.decoder.decode(PlaceScanHeld.self, from: Self.fixture("place-scan-held"))
        #expect(held.reason == "held")
        #expect(held.heldBy == "Dana Reyes")

        let record = try BenJSON.decoder.decode(PlaceScanRecord.self, from: Self.fixture("place-scan-record"))
        #expect(record.version == 1)
        #expect(record.sha256.count == 64)

        let versions = try BenJSON.decoder.decode([PlaceScanSummary].self, from: Self.fixture("place-scan-versions"))
        #expect(versions.map(\.state) == [.offered, .current])
    }

    // MARK: The requests and the answers with choices

    @Test func aHoldHeldBySomebodyElseSaysWho() async throws {
        let body = try Self.fixture("place-scan-held")
        let transport = MockTransport(status: 409, body: body)

        let outcome = await client(transport).takeHold(placeId: UUID(), room: "The Ballroom")

        guard case .heldByOther(let held) = outcome else { Issue.record("expected heldByOther, got \(outcome)"); return }
        #expect(held.says.hasPrefix("Dana Reyes is scanning The Ballroom right now."))
        let request = try #require(transport.requests.first)
        #expect(request.httpMethod == "POST")
        #expect(request.url?.path.hasSuffix("/scans/holds") == true)
        #expect(String(decoding: request.httpBody ?? Data(), as: UTF8.self).contains(#""room":"The Ballroom""#))
    }

    @Test func aHoldOnAScannedRoomComesBackAsAlreadyScanned() async throws {
        let transport = MockTransport(status: 409, body: try Self.fixture("place-scan-conflict"))
        guard case .alreadyScanned(let conflict) = await client(transport).takeHold(placeId: UUID(), room: "Room 217")
        else { Issue.record("expected alreadyScanned"); return }
        #expect(conflict.roomName == "Room 217")
    }

    @Test func anUploadThatMeetsAScanKeepsTheChoices() async throws {
        let transport = MockTransport(status: 409, body: try Self.fixture("place-scan-conflict"))
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString).scan")
        try Data("PK".utf8).write(to: file)
        let placeId = UUID()

        let outcome = await client(transport).upload(placeId: placeId, file: file, room: "Room 217",
                                                     retention: .modelOnly, holdId: nil, intent: .baseline)

        guard case .conflict(let conflict) = outcome else { Issue.record("expected conflict, got \(outcome)"); return }
        #expect(conflict.choices.contains("offer"))

        let request = try #require(transport.requests.first)
        #expect(request.url?.path == "/api/places/\(placeId.uuidString.lowercased())/scans")
        let body = String(decoding: request.httpBody ?? Data(), as: UTF8.self)
        #expect(body.contains(#"name="file"; filename="room.scan""#))
        #expect(body.contains("application/vnd.ishaunted.room-scan"))
        #expect(body.contains("modelOnly"))
        #expect(body.contains("baseline"))
        // No hold means the field is absent, not empty.
        #expect(!body.contains(#"name="holdId""#))
    }

    @Test func aRefusalWithASentenceKeepsTheSentence() async throws {
        let transport = MockTransport(status: 413, body: Data("That would take you past the 2 GB a free account can store.".utf8))
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString).scan")
        try Data("PK".utf8).write(to: file)

        let outcome = await client(transport).upload(placeId: UUID(), file: file, room: "Kitchen",
                                                     retention: .modelAndPictures, holdId: UUID(), intent: .offer)

        #expect(outcome == .failed(.failed(reason: "That would take you past the 2 GB a free account can store.")))
    }

    @Test func anUploadThatLandsSaysWhatItBecame() async throws {
        let transport = MockTransport(status: 200, body: try Self.fixture("place-scan-upload"))
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString).scan")
        try Data("PK".utf8).write(to: file)

        guard case .uploaded(let result) = await client(transport).upload(
            placeId: UUID(), file: file, room: "Room 217", retention: .modelAndPictures, holdId: nil, intent: .baseline)
        else { Issue.record("expected uploaded"); return }
        #expect(result.says.contains("is scanned"))
    }

    @Test func aRoomsVersionsAreAskedForByItsKey() async throws {
        let transport = MockTransport(status: 200, body: try Self.fixture("place-scan-versions"))
        let placeId = UUID()
        _ = await client(transport).versions(placeId: placeId, roomKey: "room 217")
        #expect(transport.requests.first?.url?.absoluteString.hasSuffix("/scans/rooms/room%20217/versions") == true)
    }
}

/// The room's key on the phone is the room's key on the server.
@Suite("Room key parity")
struct RoomKeyParityTests {

    private struct Vectors: Decodable {
        struct Vector: Decodable { let input: String; let name: String?; let key: String }
        let vectors: [Vector]
    }

    @Test func everyVectorTheServerIsHeldToHoldsHereToo() throws {
        let url = try #require(Bundle.module.url(forResource: "room-key-vectors", withExtension: "json",
                                                 subdirectory: "Fixtures/place-scans"))
        let vectors = try JSONDecoder().decode(Vectors.self, from: Data(contentsOf: url)).vectors
        #expect(vectors.count >= 10)
        for vector in vectors {
            #expect(RoomKey.cleanName(vector.input) == vector.name, "name of \(vector.input.debugDescription)")
            #expect(RoomKey.key(vector.input) == vector.key, "key of \(vector.input.debugDescription)")
        }
    }

    @Test func twoSpellingsOfOneRoomAreTheSameRoom() {
        #expect(RoomKey.same("Bedroom 1", "  bedroom\t1 "))
        #expect(!RoomKey.same("Bedroom 1", "Bedroom 2"))
        #expect(!RoomKey.same("", ""))
    }
}
