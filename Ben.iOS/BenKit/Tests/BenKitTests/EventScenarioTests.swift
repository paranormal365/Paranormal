import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Jim's overnight conference on the phone (event-scenario-gaps, 10/21/2026): the event's results, a tour
/// date's room, and the organizer's posts standing out in an event's room.
///
/// The fixtures are written from the API's records (`PublicHostedEventResultsRecord`,
/// `TourRoomRecord`) because the endpoints are new on this branch; capture them from a running API when
/// the branch is next run end to end.
@Suite("Events — results and the tour room")
@MainActor
struct EventScenarioTests {

    private func fixture(_ name: String) throws -> Data { try Fixtures.data(name, in: Bundle.module) }

    private static let eventId = UUID(uuidString: "40000002-0000-0000-0000-000000000002")!
    private static let dateId = UUID(uuidString: "5c4b3a29-1807-4f6e-9d5c-4b3a29180706")!

    private static func store(_ transport: MockTransport) -> HostedEventsStore {
        let tokens = TokenSession(
            storage: InMemoryTokenStorage(tokens: StoredTokens(
                accessToken: "AT", refreshToken: "RT", expiresAt: Date(timeIntervalSinceNow: 600))),
            transport: transport, environment: { .dev })
        return HostedEventsStore(
            api: APIClient(environment: { .dev }, transport: transport, tokens: tokens),
            cache: PassCache(directory: FileManager.default.temporaryDirectory
                .appendingPathComponent("scenario-\(UUID().uuidString)", isDirectory: true)))
    }

    // ── the results ──────────────────────────────────────────────────────────

    @Test func theResultsDecodeInTheOrderTheServerRankedThem() throws {
        let results = try BenJSON.decoder.decode(HostedEventResults.self, from: fixture("hosted-results"))
        #expect(results.hostedEventId == Self.eventId)
        #expect(results.asksForVotes)
        #expect(results.hasSomething)
        #expect(results.evidenceSentence?.contains("three days") == true)
        // Most positive votes first, even though the second was caught earlier.
        #expect(results.items.map(\.fileName) == ["Stairwell whisper.m4a", "Loading dock.jpg"])
        #expect(results.items[0].positiveVotes == 4)
        #expect(results.items[0].isAudio)
        #expect(results.items[1].isImage)
        #expect(results.items[1].note == nil)
    }

    @Test func votesAndKindsAreSaidPlainly() throws {
        let results = try BenJSON.decoder.decode(HostedEventResults.self, from: fixture("hosted-results"))
        #expect(EventResultsWording.votes(0) == nil)
        #expect(EventResultsWording.votes(1) == "1 confirms")
        #expect(EventResultsWording.votes(4) == "4 confirm")
        #expect(EventResultsWording.kind(results.items[0]) == "Recording")
        #expect(EventResultsWording.kind(results.items[1]) == "Photo")
    }

    @Test func resultsAskForNoSignInAndAMissingEventIsNothingNotAFailure() async throws {
        let ok = MockTransport(status: 200, body: try fixture("hosted-results"))
        guard case .ok(let results?) = await Self.store(ok).loadResults(Self.eventId) else {
            Issue.record("the results should load"); return
        }
        #expect(results.items.count == 2)
        let request = try #require(ok.requests.last)
        #expect(request.url?.path.hasSuffix("/hosted-events/\(Self.eventId.uuidString.lowercased())/results") == true)
        #expect(request.value(forHTTPHeaderField: "Authorization") == nil)

        guard case .ok(nil) = await Self.store(MockTransport(status: 404)).loadResults(Self.eventId) else {
            Issue.record("a 404 should read as no results"); return
        }
        guard case .failed = await Self.store(MockTransport { _ in throw URLError(.notConnectedToInternet) }).loadResults(Self.eventId) else {
            Issue.record("an unreachable server is a failure"); return
        }
    }

    // ── a tour date's room ───────────────────────────────────────────────────

    @Test func theTourRoomDecodesWithTheGuideStandingOut() throws {
        let room = try BenJSON.decoder.decode(TourRoom.self, from: fixture("tour-room"))
        #expect(room.dateId == Self.dateId)
        #expect(room.canPost)
        #expect(room.myRole == nil)
        #expect(room.messages.first?.authorRole == "Guide")
        #expect(room.messages.last?.authorRole == nil)
        #expect(room.messages.last?.isMine == true)
    }

    @Test func somebodyNotCheckedInHasNoRoomAndThatIsNotAFailure() async {
        // The server answers 204 to anybody who isn't in the room.
        guard case .ok(nil) = await Self.store(MockTransport(status: 204)).loadTourRoom(Self.dateId) else {
            Issue.record("a 204 should read as no room"); return
        }
        guard case .ok(nil) = await Self.store(MockTransport(status: 404)).loadTourRoom(Self.dateId) else {
            Issue.record("a 404 should read as no room"); return
        }
        guard case .failed = await Self.store(MockTransport { _ in throw URLError(.timedOut) }).loadTourRoom(Self.dateId) else {
            Issue.record("an unreachable server is a failure"); return
        }
    }

    @Test func aTourRoomPostSendsTheWordsAndKeepsTheServersRefusal() async throws {
        let ok = MockTransport(status: 200, body: try fixture("tour-room"))
        guard case .ok(let room) = await Self.store(ok).postTourRoom(Self.dateId, body: "Thanks, everyone") else {
            Issue.record("the post should land"); return
        }
        #expect(room.messages.count == 2)
        let request = try #require(ok.requests.last)
        #expect(request.httpMethod == "POST")
        #expect(request.url?.path.hasSuffix("/tour-dates/\(Self.dateId.uuidString.lowercased())/room") == true)
        #expect(String(decoding: request.httpBody ?? Data(), as: UTF8.self).contains(#""body":"Thanks, everyone""#))

        let closed = "The room closed a week after the walk. You can still read it."
        let refused = MockTransport(status: 409, body: Data(closed.utf8), headers: ["Content-Type": "text/plain"])
        guard case .failed(let reason, 409) = await Self.store(refused).postTourRoom(Self.dateId, body: "Late") else {
            Issue.record("expected the refusal"); return
        }
        #expect(reason == closed)
    }

    // ── who came to the classes ──────────────────────────────────────────────

    private static let orgId = UUID(uuidString: "30000000-0000-0000-0000-000000000003")!

    @Test func theClassesDecodeWithWhoIsHereAndWhoIsWaiting() throws {
        let classes = try BenJSON.decoder.decode(HostedEventClasses.self, from: fixture("hosted-classes"))
        #expect(classes.timeZone.identifier == "America/Chicago")
        let ovilus = try #require(classes.classes.first)
        #expect(ovilus.where == "The loading dock")
        #expect(ovilus.roster.in.map(\.isHere) == [true, false])
        #expect(ovilus.roster.peopleIn == 3)
        #expect(ovilus.roster.peopleThere == 2)
        #expect(ovilus.roster.waiting.count == 1)
    }

    @Test func markingSomebodyHereSendsTheSwitchAndKeepsTheServersRefusal() async throws {
        let classes = try BenJSON.decoder.decode(HostedEventClasses.self, from: fixture("hosted-classes"))
        let roster = try BenJSON.encoder.encode(classes.classes[0].roster)
        let ok = MockTransport(status: 200, body: roster)
        let session = classes.classes[0].sessionId, signUp = classes.classes[0].roster.in[1].signUpId
        guard case .ok = await Self.store(ok).setAttended(organizationId: Self.orgId, hostedEventId: Self.eventId,
                                                          sessionId: session, signUpId: signUp, here: true) else {
            Issue.record("the mark should land"); return
        }
        let request = try #require(ok.requests.last)
        #expect(request.httpMethod == "PUT")
        #expect(request.url?.path.hasSuffix("/sessions/\(session.uuidString.lowercased())/sign-ups/\(signUp.uuidString.lowercased())/attended") == true)
        #expect(String(decoding: request.httpBody ?? Data(), as: UTF8.self).contains(#""attended":true"#))

        let waiting = "They're still waiting for a place, so they weren't let in. Give them a place first."
        let refused = MockTransport(status: 400, body: Data(waiting.utf8), headers: ["Content-Type": "text/plain"])
        guard case .failed(let reason, 400) = await Self.store(refused).setAttended(
            organizationId: Self.orgId, hostedEventId: Self.eventId, sessionId: session, signUpId: signUp, here: true) else {
            Issue.record("expected the refusal"); return
        }
        #expect(reason == waiting)
    }

    @Test func theMenuCreditAndTheAlcoholMarkReadAsBenAsked() {
        #expect(EventMenuWording.credit(year: 2026) == "IsHaunted.com © 2026 ISHAUNTED LLC")
        #expect(MenuDietary.alcohol.rawValue == 256)
    }

    // ── the event's room ─────────────────────────────────────────────────────

    @Test func anOlderRoomWithNoRolesStillDecodesAndANewOneCarriesThem() throws {
        // The captured room from before roles: every post reads as a guest's.
        let old = try BenJSON.decoder.decode(EventRoom.self, from: fixture("hosted-room"))
        #expect(old.messages.allSatisfy { $0.authorRole == nil })

        var json = try #require(String(data: try fixture("hosted-room"), encoding: .utf8))
        json = json.replacingOccurrences(of: #""reports": 0"#, with: #""reports": 0, "authorRole": "Organizer""#)
        let new = try BenJSON.decoder.decode(EventRoom.self, from: Data(json.utf8))
        #expect(new.messages.first?.authorRole == "Organizer")
    }
}
