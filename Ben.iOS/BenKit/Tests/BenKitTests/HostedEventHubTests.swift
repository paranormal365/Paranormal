import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// What a guest reads and does during a hosted event: programme, menus, downloads, the room (item 235 phase 14b).
@Suite("Hosted events — programme, menus, downloads and the room")
@MainActor
struct HostedEventHubTests {

    private func fixture(_ name: String) throws -> Data { try Fixtures.data(name, in: Bundle.module) }

    private static let eventId = UUID(uuidString: "40000002-0000-0000-0000-000000000002")!
    private static let sessionId = UUID()
    private static let messageId = UUID()

    private static func store(_ transport: MockTransport) -> HostedEventsStore {
        let tokens = TokenSession(
            storage: InMemoryTokenStorage(tokens: StoredTokens(
                accessToken: "AT", refreshToken: "RT", expiresAt: Date(timeIntervalSinceNow: 600))),
            transport: transport, environment: { .dev })
        return HostedEventsStore(
            api: APIClient(environment: { .dev }, transport: transport, tokens: tokens),
            cache: PassCache(directory: FileManager.default.temporaryDirectory
                .appendingPathComponent("hub-\(UUID().uuidString)", isDirectory: true)))
    }

    private static func sentences(_ status: Int, _ sentence: String) -> MockTransport {
        MockTransport(status: status, body: Data(sentence.utf8), headers: ["Content-Type": "text/plain"])
    }

    // ── the contract, from what the API really answered ─────────────────────

    @Test func theProgrammeDecodesWithTheGuestsOwnPlace() throws {
        let programme = try BenJSON.decoder.decode(HostedEventProgramme.self, from: fixture("hosted-programme"))
        #expect(programme.hostedEventId == Self.eventId)
        #expect(programme.timeZone.identifier == "America/Chicago")
        #expect(programme.canSignUp)
        #expect(programme.maxPeople >= 1)

        let ovilus = try #require(programme.sessions.first { $0.title == "Operating the Ovilus" })
        #expect(ovilus.requiresSignUp)
        #expect(ovilus.capacity == 15)
        let mine = try #require(ovilus.mine)
        #expect(mine.people == 1)
        #expect(!mine.waiting)

        let dinner = try #require(programme.sessions.first { !$0.requiresSignUp })
        #expect(dinner.mine == nil)
        #expect(!dinner.isFull)
    }

    @Test func menusDecodeWithTheVenuesOwnTimeAndTags() throws {
        let menus = try BenJSON.decoder.decode(HostedEventMenus.self, from: fixture("hosted-menus"))
        let dinner = try #require(menus.menus.first { $0.title == "Dinner" })
        #expect(dinner.servedAtLocal == "19:00:00")
        #expect(dinner.servedAtText?.contains("7:00") == true)
        let soup = try #require(dinner.items.first { $0.name == "Tomato soup" })
        #expect(soup.tags == ["vegan"])
    }

    @Test func aFileForGuestsDecodes() throws {
        let files = try BenJSON.decoder.decode([HostedEventFile].self, from: fixture("hosted-files"))
        let pack = try #require(files.first)
        #expect(pack.fileName == "Guest pack.txt")
        #expect(pack.audience == .attendees)
        #expect(pack.folder == "Before you come")
    }

    @Test func theRoomDecodesWithAPhotoPost() throws {
        let room = try BenJSON.decoder.decode(EventRoom.self, from: fixture("hosted-room"))
        #expect(room.canPost)
        #expect(room.canAddPhotos)
        #expect(room.photoPosting == .teamAndGuests)
        #expect(room.hostNames == ["Paranormal365"])
        #expect(room.photoNotice?.contains("photo wall") == true)
        let post = try #require(room.messages.first)
        #expect(post.isMine)
        #expect(post.hasMedia)
        // The server re-encodes a photo; the app must not assume the type it sent.
        #expect(post.mediaContentType == "image/jpeg")
        #expect(!post.isVideo)
    }

    // ── absent is not failed ─────────────────────────────────────────────────

    @Test func noPublishedProgrammeIsNothingToShowNotAnError() async {
        guard case .ok(nil) = await Self.store(MockTransport(status: 404)).loadProgramme(Self.eventId) else {
            Issue.record("a 404 programme should read as none"); return
        }
    }

    @Test func menusNotYetPublishedToThisGuestAreNothingToShow() async {
        let store = Self.store(Self.sentences(403, "The venue publishes the menu once your place is agreed."))
        guard case .ok(nil) = await store.loadMenus(Self.eventId) else { Issue.record("a 403 menu should read as none"); return }
    }

    @Test func somebodyNotAtTheEventHasNoRoomAndNoFiles() async {
        let store = Self.store(MockTransport(status: 404))
        guard case .ok(nil) = await store.loadRoom(Self.eventId) else { Issue.record("room"); return }
        guard case .ok(let files) = await store.loadFiles(Self.eventId), files.isEmpty else { Issue.record("files"); return }
    }

    @Test func anUnreachableServerIsStillAFailure() async {
        let store = Self.store(MockTransport { _ in throw URLError(.notConnectedToInternet) })
        guard case .failed = await store.loadProgramme(Self.eventId) else { Issue.record("programme"); return }
        guard case .failed = await store.loadRoom(Self.eventId) else { Issue.record("room"); return }
    }

    // ── writes ───────────────────────────────────────────────────────────────

    @Test func signingUpSendsHowManyAndKeepsTheServersRefusal() async throws {
        let ok = MockTransport(status: 200, body: try fixture("hosted-programme"))
        _ = await Self.store(ok).signUp(Self.eventId, session: Self.sessionId, people: 2)
        let request = try #require(ok.requests.last)
        #expect(request.httpMethod == "POST")
        #expect(request.url?.path.hasSuffix("/sessions/\(Self.sessionId.uuidString.lowercased())/sign-up") == true)
        let sent = String(decoding: request.httpBody ?? Data(), as: UTF8.self)
        #expect(sent.contains(#""people":2"#))

        let refused = Self.store(Self.sentences(409, "Your place isn't confirmed yet, so there's nothing to sign up with."))
        guard case .failed(let reason, 409) = await refused.signUp(Self.eventId, session: Self.sessionId, people: 1) else {
            Issue.record("expected the refusal"); return
        }
        #expect(reason == "Your place isn't confirmed yet, so there's nothing to sign up with.")
    }

    @Test func aRoomPostCarriesTheWordsThePhotoTheAgreementAndTheShare() async throws {
        let transport = MockTransport(status: 200, body: try fixture("hosted-room"))
        let photo = FileManager.default.temporaryDirectory.appendingPathComponent("room-\(UUID().uuidString).jpg")
        try Data([0xFF, 0xD8, 0xFF]).write(to: photo)
        defer { try? FileManager.default.removeItem(at: photo) }

        let result = await Self.store(transport).post(
            Self.eventId, body: "The stairs", media: MediaUpload(fileURL: photo, filename: "stairs.jpg", contentType: "image/jpeg", byteCount: 3),
            sendToHosts: true, agreeToShow: true)
        #expect(result.isOk)

        let request = try #require(transport.requests.last)
        #expect(request.url?.path.hasSuffix("/hosted-events/\(Self.eventId.uuidString.lowercased())/room") == true)
        let body = String(decoding: request.httpBody ?? Data(), as: UTF8.self)
        #expect(body.contains(#"name="body""#) && body.contains("The stairs"))
        #expect(body.contains(#"name="sendToHosts""#) && body.contains("true"))
        #expect(body.contains(#"name="agreeToShow""#))
        #expect(body.contains(#"name="media"; filename="stairs.jpg""#))
    }

    @Test func aPostWithoutTheAgreementIsRefusedInTheServersWords() async {
        let notice = "Photos you add here are shown to the people at this event. Tick that you agree to add your photo."
        let result = await Self.store(Self.sentences(409, notice)).post(
            Self.eventId, body: "", media: nil, sendToHosts: false, agreeToShow: false)
        guard case .failed(let reason, _) = result else { Issue.record("expected the refusal"); return }
        #expect(reason == notice)
    }

    @Test func olderPostsAreAskedForBeforeTheOldestShown() async throws {
        let transport = MockTransport(status: 200, body: try fixture("hosted-room"))
        let oldest = Date(timeIntervalSince1970: 1_789_331_775.5)
        _ = await Self.store(transport).loadRoom(Self.eventId, before: oldest)
        let query = try #require(transport.requests.last?.url?.query)
        #expect(query.contains("before=2026-09-13T20:36:15.500Z"))
    }

    @Test func aDownloadIsSavedUnderItsOwnNameAndAHostileNameIsMadeSafe() async throws {
        let transport = MockTransport(status: 200, body: Data("Doors open at seven.".utf8))
        let files = try BenJSON.decoder.decode([HostedEventFile].self, from: fixture("hosted-files"))
        guard case .ok(let url) = await Self.store(transport).download(Self.eventId, file: try #require(files.first)) else {
            Issue.record("download"); return
        }
        #expect(url.lastPathComponent == "Guest pack.txt")
        #expect(try String(contentsOf: url, encoding: .utf8) == "Doors open at seven.")

        #expect(HostedEventsStore.safeFileName("../../etc/passwd") == "download..-..-etc-passwd")
        #expect(HostedEventsStore.safeFileName("a:b/c.pdf") == "a-b-c.pdf")
        #expect(HostedEventsStore.safeFileName("   ") == "download")
    }

    @Test func aTimeOnTheVenuesClockIsNeverShifted() {
        var menu = HostedEventMenu(id: UUID(), hostedEventNightId: UUID(), nightDate: Date(), nightTitle: nil,
                                   title: "Breakfast", servedAtLocal: "08:30:00", notes: nil, sortOrder: 0, items: [])
        #expect(menu.servedAtText?.contains("8:30") == true)
        menu.servedAtLocal = "nonsense"
        #expect(menu.servedAtText == nil)
    }

    // ── links ────────────────────────────────────────────────────────────────

    @Test func linksLandOnTheEventTheRoomAndTheAddPhotosComposer() {
        let id = Self.eventId
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/my-events/\(id.uuidString)")!) == .eventHub(id))
        #expect(DeepLinkParser.parse(URL(string: "ishaunted://my-events/\(id.uuidString)/review")!) == .eventHub(id))
        #expect(DeepLinkParser.parse(URL(string: "ishaunted://my-events/\(id.uuidString)/pass")!) == .eventPass(id))
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/events/\(id.uuidString)/room")!) == .eventRoom(id, addPhotos: false))
        // The photo wall's code.
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/events/\(id.uuidString)/photos")!) == .eventRoom(id, addPhotos: true))
        // A calendar date is still a calendar date, and the wall itself is not a phone screen.
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/events/\(id.uuidString)")!) == .eventDetail(id))
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/events/\(id.uuidString)/wall")!) == .eventDetail(id))
    }
}
