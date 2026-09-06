import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Investigations and events (iOS Slice 7): the live shapes, the roster split, what may be
/// drawn on a map, and RSVP refusals that must reach the person.
@Suite("Investigations — roster, history, map")
@MainActor
struct InvestigationsTests {

    private static func store(_ transport: MockTransport) -> InvestigationsStore {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return InvestigationsStore(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    @Test func theLiveRosterFixtureDecodes() throws {
        let data = try Fixtures.data("my-investigations", in: Bundle.module)
        let items = try BenJSON.decoder.decode([MyInvestigation].self, from: data)
        let first = try #require(items.first)

        #expect(first.title == "Initial Night Investigation")
        #expect(first.orgName == "Tennessee Ghost Hunters")
        #expect(first.assignedRole == "EMF Specialist")
        #expect(first.didAttend == true)
        // No answer recorded — the roster's job is to make that visible, not to guess.
        #expect(first.rsvp == .noAnswer)
    }

    /// Captured live on 2026-09-06: a roster entry the organiser has not marked either way comes
    /// back with `didAttend: null`, and a non-optional Bool made the WHOLE roster fail to decode —
    /// "Couldn't load your investigations" on both devices, and the Send screen could not offer
    /// the visit. The fixture carries that record now, so this cannot drift back.
    @Test func aRosterEntryNobodyHasMarkedYetStillDecodes() throws {
        let data = try Fixtures.data("my-investigations", in: Bundle.module)
        let items = try BenJSON.decoder.decode([MyInvestigation].self, from: data)
        let unmarked = try #require(items.first { $0.didAttend == nil })
        #expect(unmarked.title == "Initial walkthrough and baseline readings")
        #expect(unmarked.scheduledDateTime != nil)   // 7-digit fraction, no offset — the C# shape
    }

    @Test func theAttendedFixtureDecodesIncludingItsMissingCoordinates() throws {
        let data = try Fixtures.data("investigations-attended", in: Bundle.module)
        let visits = try BenJSON.decoder.decode([AttendedInvestigation].self, from: data)
        let first = try #require(visits.first)

        // This seed visit genuinely has no coordinates. It is still a real visit — it just
        // cannot be drawn, which is exactly the distinction the map has to respect.
        #expect(!first.hasCoordinates)
        #expect(first.placeLabel == "Springfield, TN")
    }

    @Test func aVisitWithNoCoordinatesIsListedButNotMapped() async {
        let withPin = """
        {"investigationId":"\(UUID().uuidString.lowercased())","title":"Pinned",
         "scheduledDateTime":"2026-03-22T20:00:00","organizationId":"\(UUID().uuidString.lowercased())",
         "organizationName":"Org","caseId":null,"caseReference":null,"placeId":null,
         "placeName":"The Mill","placeCity":"Adams","placeState":"TN",
         "latitude":36.55,"longitude":-87.07,"geocodeNote":null,"wasLead":false}
        """
        let withoutPin = """
        {"investigationId":"\(UUID().uuidString.lowercased())","title":"Unpinned",
         "scheduledDateTime":"2026-03-22T20:00:00","organizationId":"\(UUID().uuidString.lowercased())",
         "organizationName":"Org","caseId":null,"caseReference":null,"placeId":null,
         "placeName":"Somewhere","placeCity":"Nashville","placeState":"TN",
         "latitude":null,"longitude":null,"geocodeNote":null,"wasLead":false}
        """
        let transport = MockTransport { request in
            let path = request.url?.path ?? ""
            let body = path.hasSuffix("/attended")
                ? Data("[\(withPin),\(withoutPin)]".utf8)
                : Data("[]".utf8)
            return (body, MockTransport.response(for: request, status: 200))
        }
        let store = Self.store(transport)
        await store.load()

        #expect(store.attended.count == 2)      // both are real visits
        #expect(store.mappable.count == 1)      // only one can be drawn
        #expect(store.mappable.first?.title == "Pinned")
    }

    @Test func theRosterSplitsByWhetherItHasHappened() {
        let now = Date(timeIntervalSince1970: 1_800_000_000)
        func inv(_ title: String, start: Date, end: Date?) -> MyInvestigation {
            MyInvestigation(
                attendeeId: UUID(), investigationId: UUID(), caseId: nil, caseReference: nil,
                caseTitle: nil, orgId: UUID(), orgName: "Org", orgUrlName: nil, title: title,
                scheduledDateTime: start, endDateTime: end, location: nil, status: 1,
                assignedRole: nil, rsvp: .going, didAttend: false, evidenceDueDate: nil)
        }
        let tonight = inv("Tonight", start: now.addingTimeInterval(3600), end: nil)
        // Started an hour ago and running for six more: still UPCOMING, because a roster that
        // buries the investigation you are currently at is useless.
        let running = inv("Running", start: now.addingTimeInterval(-3600),
                          end: now.addingTimeInterval(6 * 3600))
        let done = inv("Done", start: now.addingTimeInterval(-86_400 * 7),
                       end: now.addingTimeInterval(-86_400 * 7 + 3600))

        #expect(tonight.isUpcoming(now: now))
        #expect(running.isUpcoming(now: now))
        #expect(!done.isUpcoming(now: now))
    }

    @Test func aFailedMapFetchDoesNotBreakAWorkingRoster() async {
        // The roster is the point of the screen; the map is extra. One failing must not
        // turn the other into an error page.
        let roster = """
        [{"attendeeId":"\(UUID().uuidString.lowercased())","investigationId":"\(UUID().uuidString.lowercased())",
          "caseId":null,"caseReference":null,"caseTitle":null,"orgId":"\(UUID().uuidString.lowercased())",
          "orgName":"Org","orgUrlName":null,"title":"Night one","scheduledDateTime":"2026-09-01T20:00:00",
          "endDateTime":null,"location":null,"status":1,"assignedRole":null,"rsvp":1,
          "didAttend":false,"evidenceDueDate":null}]
        """
        let transport = MockTransport { request in
            let path = request.url?.path ?? ""
            return path.hasSuffix("/attended")
                ? (Data(), MockTransport.response(for: request, status: 500))
                : (Data(roster.utf8), MockTransport.response(for: request, status: 200))
        }
        let store = Self.store(transport)
        await store.load()

        #expect(store.state == .loaded)
        #expect(store.investigations.count == 1)
        #expect(store.attended.isEmpty)
    }

    @Test func signedOutIsAFactNotAnError() async {
        let store = Self.store(MockTransport(status: 401))
        await store.load()
        #expect(store.state == .signedOut)
    }
}

@Suite("Public events — the open door, and RSVP")
@MainActor
struct PublicEventsTests {

    private static func store(_ transport: MockTransport) -> EventsStore {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return EventsStore(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    @Test func theLiveEventsFixtureDecodes() throws {
        let data = try Fixtures.data("public-events-full", in: Bundle.module)
        let events = try BenJSON.decoder.decode([PublicEventListItem].self, from: data)
        let first = try #require(events.first)

        #expect(first.organizationName == "Tennessee Ghost Hunters")
        #expect(first.hasCoordinates)          // approximate, by design
        #expect(first.attendeeCapacity == nil) // no cap …
        #expect(!first.isFull)                 // … so never full
        #expect(first.spacesLeft == nil)
    }

    @Test func aNullCapacityIsUnlimitedNotZero() {
        // The trap this guards: treating null as 0 would show every uncapped event as full.
        func event(capacity: Int?, attending: Int) -> PublicEventListItem {
            PublicEventListItem(
                id: UUID(), urlName: nil, organizationId: UUID(), organizationName: "Org",
                organizationUrlName: "org", title: "T", startDateTime: Date(), endDateTime: Date(), isAllDay: false, city: nil, state: nil, approximateLatitude: nil, approximateLongitude: nil, attendingCount: attending,
                attendeeCapacity: capacity, isOnline: false)
        }
        #expect(!event(capacity: nil, attending: 500).isFull)
        #expect(event(capacity: 10, attending: 10).isFull)
        #expect(!event(capacity: 10, attending: 9).isFull)
        #expect(event(capacity: 10, attending: 9).spacesLeft == 1)
        // Overbooked (a cap lowered after sign-ups) reports zero left, never a negative.
        #expect(event(capacity: 5, attending: 8).spacesLeft == 0)
    }

    @Test func rsvpRefusalsReachThePersonInTheServersWords() async {
        for sentence in ["Sign-ups for this event have closed.",
                         "That event has already started.",
                         "This event is full."] {
            let store = Self.store(MockTransport(status: 409, body: Data(sentence.utf8)))
            let result = await store.rsvp(UUID())
            guard case .failure(let error) = result else {
                Issue.record("expected a refusal for: \(sentence)"); return
            }
            // Verbatim: "This event is full" tells somebody to look for another date.
            // "Couldn't RSVP" tells them to try the same button again.
            #expect(error.message == sentence)
        }
    }

    @Test func eventsReadAnonymouslyAndSkipTheMineCall() async {
        let data = try! Fixtures.data("public-events-full", in: Bundle.module)
        let transport = MockTransport(status: 200, body: data)
        let store = Self.store(transport)

        await store.load(signedIn: false)
        #expect(store.state == .loaded)
        #expect(store.attending.isEmpty)
        // One request only: a visitor has no "mine" to ask about.
        #expect(transport.requests.count == 1)
        #expect(transport.requests.first?.value(forHTTPHeaderField: "Authorization") == nil)
    }
}
