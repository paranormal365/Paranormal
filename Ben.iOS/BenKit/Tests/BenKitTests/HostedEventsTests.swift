import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// A guest's hosted-event bookings and the pass that has to work in a basement (item 235 phase 14).
@Suite("Hosted events — bookings, passes, and the saved pass")
@MainActor
struct HostedEventsTests {

    private func fixture(_ name: String) throws -> Data { try Fixtures.data(name, in: Bundle.module) }

    private static let eventId = UUID(uuidString: "40000002-0000-0000-0000-000000000002")!

    private static func temporaryCache() -> PassCache {
        PassCache(directory: FileManager.default.temporaryDirectory
            .appendingPathComponent("pass-cache-\(UUID().uuidString)", isDirectory: true))
    }

    private static func store(_ transport: MockTransport, cache: PassCache) -> HostedEventsStore {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return HostedEventsStore(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens), cache: cache)
    }

    // ── the contract, from what the API really answered ─────────────────────

    @Test func myBookingsDecodeFromLiveCapture() throws {
        let mine = try BenJSON.decoder.decode([MyHostedEventBooking].self, from: fixture("hosted-mine"))
        let live = try #require(mine.first { $0.hostedEventId == Self.eventId })
        #expect(live.status == .confirmed)
        #expect(live.kind == .dayPass)
        #expect(live.eventUrlName == "thomas-house-seance-weekend")
        #expect(live.organizationUrlName == "paranormal365")
        #expect(live.sessions != nil)
        // A released booking at another event decodes too, with its nights and guests.
        #expect(mine.contains { $0.status == .cancelled })
    }

    @Test func passDecodesFromLiveCaptureWithoutAWorkingToken() throws {
        let pass = try BenJSON.decoder.decode(MyHostedEventPass.self, from: fixture("hosted-pass"))
        #expect(pass.pass.token == "fixture-pass-token")
        #expect(pass.pass.shortCode == "-TOKEN")
        #expect(!pass.pass.isRevoked)
        #expect(pass.eventName == "Thomas House Séance Weekend")
        #expect(pass.partySize == 2)
        #expect(pass.seating == [])
    }

    @Test func theUmbrellaDateSaysWhichHostedEventItStandsFor() throws {
        let event = try BenJSON.decoder.decode(PublicEventRecord.self, from: fixture("hosted-umbrella-event"))
        #expect(event.hostedEventId == Self.eventId)
        #expect(event.hostedEventName == "Thomas House Séance Weekend")
    }

    @Test func theHostedEventDecodesAndKnowsItsPage() throws {
        let hosted = try BenJSON.decoder.decode(PublicHostedEvent.self, from: fixture("hosted-event"))
        #expect(hosted.id == Self.eventId)
        #expect(hosted.bookingMode == .ask)
        #expect(hosted.isTakingBookings)
        #expect(hosted.pagePath == "o/paranormal365/events/thomas-house-seance-weekend")
        #expect(hosted.dayPassPrice == Decimal(45))
        #expect(hosted.accessNotes?.contains("no lift") == true)
    }

    @Test func anOrdinaryDateHasNoHostedEvent() throws {
        let event = try BenJSON.decoder.decode(PublicEventRecord.self, from: fixture("public-event-detail"))
        #expect(event.hostedEventId == nil)
    }

    // ── the pass, with and without a signal ──────────────────────────────────

    @Test func aLivePassIsSavedAndShownWhenTheServerCannotBeReached() async throws {
        let cache = Self.temporaryCache()
        let body = try fixture("hosted-pass")

        let online = Self.store(MockTransport(status: 200, body: body), cache: cache)
        guard case .live(let pass) = await online.loadPass(Self.eventId) else { Issue.record("expected live"); return }
        #expect(pass.pass.token == "fixture-pass-token")

        let offline = Self.store(MockTransport { _ in throw URLError(.notConnectedToInternet) }, cache: cache)
        guard case .saved(let saved, let savedAt) = await offline.loadPass(Self.eventId) else {
            Issue.record("expected the saved pass"); return
        }
        #expect(saved.pass.token == pass.pass.token)
        #expect(saved.pass.id == pass.pass.id)
        #expect(saved.leadName == pass.leadName && saved.partySize == pass.partySize)
        // Saved to the millisecond; the server writes microseconds, which nobody at a door needs.
        #expect(abs(saved.pass.issuedUtc.timeIntervalSince(pass.pass.issuedUtc)) < 0.001)
        #expect(savedAt <= Date())
    }

    @Test func aServerThatSaysThereIsNoPassClearsTheSavedOne() async throws {
        // Released since it was saved: an old code kept on the phone would be a lie at the door.
        let cache = Self.temporaryCache()
        let pass = try BenJSON.decoder.decode(MyHostedEventPass.self, from: fixture("hosted-pass"))
        cache.save(pass, for: Self.eventId)

        let refused = Self.store(MockTransport(status: 403, body: Data("This booking was released, so there is no pass.".utf8)), cache: cache)
        guard case .none(let reason) = await refused.loadPass(Self.eventId) else { Issue.record("expected none"); return }
        #expect(reason == "This booking was released, so there is no pass.")
        #expect(cache.load(Self.eventId) == nil)
    }

    @Test func aWithdrawnPassIsKeptWithdrawn() async throws {
        let cache = Self.temporaryCache()
        var pass = try BenJSON.decoder.decode(MyHostedEventPass.self, from: fixture("hosted-pass"))
        pass.pass.revokedUtc = Date()
        pass.pass.revokedReason = "Replaced at the desk."
        cache.save(pass, for: Self.eventId)

        let offline = Self.store(MockTransport { _ in throw URLError(.timedOut) }, cache: cache)
        guard case .saved(let saved, _) = await offline.loadPass(Self.eventId) else { Issue.record("expected saved"); return }
        #expect(saved.pass.isRevoked)
        #expect(saved.pass.revokedReason == "Replaced at the desk.")
    }

    @Test func noBookingIsAnAnswerNotAFailure() async {
        let store = Self.store(MockTransport(status: 404), cache: Self.temporaryCache())
        guard case .ok(let booking) = await store.loadMyBooking(Self.eventId) else { Issue.record("expected ok"); return }
        #expect(booking == nil)
    }

    @Test func forgettingTheSavedPassesLeavesNothingForTheNextPerson() throws {
        let cache = Self.temporaryCache()
        cache.save(try BenJSON.decoder.decode(MyHostedEventPass.self, from: fixture("hosted-pass")), for: Self.eventId)
        cache.removeAll()
        #expect(cache.load(Self.eventId) == nil)
    }

    // ── links, and the website the app sends people to ─────────────────────

    @Test func myEventsLinksLandOnTheirScreens() {
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/my-events")!) == .myEvents)
        #expect(DeepLinkParser.parse(URL(string: "ishaunted://my-events/\(Self.eventId.uuidString)/pass")!)
                == .eventPass(Self.eventId))
        // A shape with no screen falls back to the list rather than to nowhere.
        #expect(DeepLinkParser.parse(URL(string: "https://ishaunted.com/my-events/not-an-id/pass")!) == .myEvents)
    }

    @Test func theWebsiteIsTheAPIsOwnHostWithoutItsPath() {
        #expect(APIEnvironment.production.websiteURL.absoluteString == "https://ishaunted.com")
        #expect(APIEnvironment.dev.websiteURL.absoluteString == "http://localhost:5078")
        #expect(APIEnvironment.production.websiteURL(path: "o/paranormal365/events/seance").absoluteString
                == "https://ishaunted.com/o/paranormal365/events/seance")
    }
}
