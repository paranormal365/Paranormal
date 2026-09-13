import Foundation
import Testing
import ImageIO
import UniformTypeIdentifiers
@testable import BenKit
import BenKitTestSupport

/// Room posts kept for a signal, shared with the Share Extension (item 235 phase 14d).
@Suite("The room outbox — kept posts, sending them, and what the Share Extension offers")
@MainActor
struct RoomOutboxTests {

    private func fixture(_ name: String) throws -> Data { try Fixtures.data(name, in: Bundle.module) }

    nonisolated private static let eventId = UUID(uuidString: "40000002-0000-0000-0000-000000000002")!
    nonisolated private static let otherEventId = UUID()

    private static func folder() -> URL {
        FileManager.default.temporaryDirectory.appendingPathComponent("outbox-\(UUID().uuidString)", isDirectory: true)
    }

    private final class Line: @unchecked Sendable {
        var up = true
        var answer: @Sendable (URLRequest) throws -> (Data, Int) = { _ in (Data(), 200) }
    }

    private static func sender(_ line: Line, outbox: RoomOutbox) -> (RoomOutboxSender, MockTransport) {
        let transport = MockTransport { request in
            guard line.up else { throw URLError(.notConnectedToInternet) }
            let (body, status) = try line.answer(request)
            return (body, MockTransport.response(for: request, status: status))
        }
        let tokens = TokenSession(
            storage: InMemoryTokenStorage(tokens: StoredTokens(accessToken: "AT", refreshToken: "RT", expiresAt: Date(timeIntervalSinceNow: 600))),
            transport: transport, environment: { .dev })
        let store = HostedEventsStore(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens),
                                      cache: PassCache(directory: folder()))
        return (RoomOutboxSender(store: store, outbox: outbox), transport)
    }

    /// A real PNG on disk — a format the server should not be sent as it is.
    private static func png(_ name: String = "stairs.png") throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString)-\(name)")
        let data = Data(base64Encoded: "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGP8z8DAwMDAxMDAwMDAAAANHQEDasKb6QAAAABJRU5ErkJggg==")!
        try data.write(to: url)
        return url
    }

    private static func video(bytes: Int) throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("\(UUID().uuidString).mov")
        FileManager.default.createFile(atPath: url.path, contents: nil)
        let handle = try FileHandle(forWritingTo: url)
        try handle.truncate(atOffset: UInt64(bytes))
        try handle.close()
        return url
    }

    // ── keeping ──────────────────────────────────────────────────────────────

    @Test func aKeptPhotoIsConvertedToJPEGAndTheScratchCopyIsMovedIn() throws {
        let outbox = RoomOutbox(directory: Self.folder())
        let source = try Self.png()

        let post = try outbox.add(hostedEventId: Self.eventId, eventName: "Séance Weekend", body: "The stairs", file: source,
                                  contentType: "image/png", originalName: "IMG_0042.PNG", sendToHosts: true, agreeToShow: true, moveFile: true)

        #expect(post.contentType == "image/jpeg")
        #expect(!FileManager.default.fileExists(atPath: source.path))
        let media = try #require(outbox.media(for: post))
        #expect(media.filename == "IMG_0042.jpg")
        let kept = try #require(CGImageSourceCreateWithURL(media.fileURL as CFURL, nil))
        #expect(CGImageSourceGetType(kept) as String? == UTType.jpeg.identifier)
        #expect(outbox.waiting(for: Self.eventId).map(\.id) == [post.id])
    }

    @Test func aFileSharedFromAnotherAppIsCopiedAndLeftWhereItWas() throws {
        let outbox = RoomOutbox(directory: Self.folder())
        let clip = try Self.video(bytes: 2_000)
        _ = try outbox.add(hostedEventId: Self.eventId, eventName: "E", body: "", file: clip, contentType: "video/quicktime",
                           originalName: "clip.mov", sendToHosts: false, agreeToShow: false, moveFile: false)
        #expect(FileManager.default.fileExists(atPath: clip.path))
        #expect(outbox.waiting().first?.byteCount == 2_000)
    }

    @Test func aVideoTooLargeForOnePostIsRefusedInWordsAndNotKept() throws {
        let outbox = RoomOutbox(directory: Self.folder())
        let clip = try Self.video(bytes: Int(RoomOutbox.largestFile) + 1)
        #expect(throws: RoomOutbox.AddError.self) {
            try outbox.add(hostedEventId: Self.eventId, eventName: "E", body: "", file: clip, contentType: "video/quicktime",
                           originalName: nil, sendToHosts: false, agreeToShow: false, moveFile: false)
        }
        #expect(outbox.all().isEmpty)
    }

    @Test func removingAPostTakesItsPhotoWithIt() throws {
        let outbox = RoomOutbox(directory: Self.folder())
        let post = try outbox.add(hostedEventId: Self.eventId, eventName: "E", body: "", file: try Self.png(), contentType: "image/png",
                                  originalName: nil, sendToHosts: false, agreeToShow: true, moveFile: true)
        let file = try #require(outbox.media(for: post)).fileURL
        outbox.remove(post.id)
        #expect(outbox.all().isEmpty)
        #expect(!FileManager.default.fileExists(atPath: file.path))
    }

    @Test func twoWritersAtOnceLoseNothing() async throws {
        // The app and the Share Extension add at the same moment; the coordinator keeps both.
        let folder = Self.folder()
        await withTaskGroup(of: Void.self) { group in
            for n in 0..<20 {
                group.addTask {
                    _ = try? RoomOutbox(directory: folder).add(hostedEventId: Self.eventId, eventName: "E", body: "post \(n)", file: nil,
                                                               contentType: nil, originalName: nil, sendToHosts: false,
                                                               agreeToShow: false, moveFile: false)
                }
            }
        }
        #expect(RoomOutbox(directory: folder).all().count == 20)
    }

    // ── sending ──────────────────────────────────────────────────────────────

    @Test func keptPostsAreSentOldestFirstWithTheirChoicesAndRemovedOnceTheyLand() async throws {
        let outbox = RoomOutbox(directory: Self.folder())
        let first = try outbox.add(hostedEventId: Self.eventId, eventName: "E", body: "first", file: try Self.png(), contentType: "image/png",
                                   originalName: nil, sendToHosts: true, agreeToShow: true, moveFile: true, at: Date(timeIntervalSince1970: 1))
        _ = try outbox.add(hostedEventId: Self.eventId, eventName: "E", body: "second", file: nil, contentType: nil,
                           originalName: nil, sendToHosts: false, agreeToShow: false, moveFile: false, at: Date(timeIntervalSince1970: 2))
        let photo = try #require(outbox.media(for: first)).fileURL

        let line = Line()
        let room = try fixture("hosted-room")
        line.answer = { _ in (room, 200) }
        let (sender, transport) = Self.sender(line, outbox: outbox)

        let report = await sender.sendWaiting()
        #expect(report.sent == 2)
        #expect(report.stillWaiting == 0)
        #expect(report.rooms[Self.eventId] != nil)
        #expect(outbox.all().isEmpty)
        #expect(!FileManager.default.fileExists(atPath: photo.path))

        let bodies = transport.requests.map { String(decoding: $0.httpBody ?? Data(), as: UTF8.self) }
        #expect(bodies.first?.contains("first") == true)
        #expect(bodies.first?.contains(#"name="media"; filename="#) == true)
        #expect(bodies.first?.contains("image/jpeg") == true)
        #expect(bodies.last?.contains("second") == true)
    }

    @Test func noSignalSendsNothingAndKeepsEverything() async throws {
        let outbox = RoomOutbox(directory: Self.folder())
        _ = try outbox.add(hostedEventId: Self.eventId, eventName: "E", body: "one", file: nil, contentType: nil, originalName: nil,
                           sendToHosts: false, agreeToShow: false, moveFile: false)
        _ = try outbox.add(hostedEventId: Self.eventId, eventName: "E", body: "two", file: nil, contentType: nil, originalName: nil,
                           sendToHosts: false, agreeToShow: false, moveFile: false)
        let line = Line()
        line.up = false
        let (sender, transport) = Self.sender(line, outbox: outbox)

        let report = await sender.sendWaiting()
        #expect(report.sent == 0)
        #expect(report.stillWaiting == 2)
        #expect(transport.requests.count == 1)
    }

    @Test func aPostTheRoomRefusesIsKeptWithItsSentenceAndTheNextIsStillSent() async throws {
        let outbox = RoomOutbox(directory: Self.folder())
        let refusedOne = try outbox.add(hostedEventId: Self.otherEventId, eventName: "Closed", body: "late", file: nil, contentType: nil,
                                        originalName: nil, sendToHosts: false, agreeToShow: false, moveFile: false, at: Date(timeIntervalSince1970: 1))
        _ = try outbox.add(hostedEventId: Self.eventId, eventName: "Open", body: "fine", file: nil, contentType: nil,
                           originalName: nil, sendToHosts: false, agreeToShow: false, moveFile: false, at: Date(timeIntervalSince1970: 2))

        let line = Line()
        let room = try fixture("hosted-room")
        let closedSentence = "The room closed a week after the last night. You can still look back through it."
        line.answer = { request in
            request.url!.path.contains(Self.otherEventId.uuidString.lowercased()) ? (Data(closedSentence.utf8), 409) : (room, 200)
        }
        let (sender, _) = Self.sender(line, outbox: outbox)

        let report = await sender.sendWaiting()
        #expect(report.sent == 1)
        #expect(report.refused == 1)
        #expect(outbox.waiting().isEmpty)
        #expect(outbox.refused().map(\.id) == [refusedOne.id])
        #expect(outbox.refused().first?.refusal == closedSentence)
    }

    // ── what the Share Extension offers ──────────────────────────────────────

    @Test func theRoomsRulesLearnedInTheAppSurviveTheNextListAndShapeWhatIsOffered() throws {
        let events = ShareableEvents(file: Self.folder().appendingPathComponent("events.json"))
        let now = Date()
        let open = ShareableEvents.Event(hostedEventId: Self.eventId, eventName: "Open", organizationName: nil,
                                         startsOn: now, endsOn: now, canAddPhotos: nil, needsPhotoConsent: nil, photoNotice: nil, hostNames: [])
        let teamOnly = ShareableEvents.Event(hostedEventId: Self.otherEventId, eventName: "Team only", organizationName: nil,
                                             startsOn: now, endsOn: now, canAddPhotos: nil, needsPhotoConsent: nil, photoNotice: nil, hostNames: [])
        let longOver = ShareableEvents.Event(hostedEventId: UUID(), eventName: "Last month", organizationName: nil,
                                             startsOn: now.addingTimeInterval(-40 * 86_400), endsOn: now.addingTimeInterval(-39 * 86_400),
                                             canAddPhotos: nil, needsPhotoConsent: nil, photoNotice: nil, hostNames: [])
        events.save([open, teamOnly, longOver])

        var room = try BenJSON.decoder.decode(EventRoom.self, from: fixture("hosted-room"))
        room.needsPhotoConsent = true
        events.learn(from: room, for: Self.eventId)
        room.canAddPhotos = false
        events.learn(from: room, for: Self.otherEventId)

        // The app writes the list again from bookings, knowing nothing of the rooms: what was learned stays.
        events.save([open, teamOnly, longOver])

        let offered = events.offerable(now: now)
        #expect(offered.map(\.hostedEventId) == [Self.eventId])
        #expect(offered.first?.needsPhotoConsent == true)
        #expect(offered.first?.hostNames == ["Paranormal365"])
        #expect(offered.first?.photoNotice?.contains("photo wall") == true)
    }

    @Test func openingARoomBeforeTheListIsWrittenStillRecordsItsRules() throws {
        let events = ShareableEvents(file: Self.folder().appendingPathComponent("events.json"))
        let room = try BenJSON.decoder.decode(EventRoom.self, from: fixture("hosted-room"))
        let now = Date()
        events.learn(from: room, for: Self.eventId,
                     event: ShareableEvents.Event(hostedEventId: Self.eventId, eventName: "Séance", organizationName: nil, startsOn: now, endsOn: now))
        #expect(events.offerable(now: now).first?.canAddPhotos == true)
    }
}
