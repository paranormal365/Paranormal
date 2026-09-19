import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Decodes JSON captured VERBATIM from the running dev API (see Fixtures/).
/// These lock the two cross-cutting assumptions — camelCase keys, the naked
/// C# DateTime shape — and prove the Swift records match the C# ones in
/// `Ben.Service.Models`. When a fixture stops decoding, the server contract
/// moved and the model needs the same change the website client got.
@Suite("Live-API fixtures — Swift models match Ben.Service.Models")
struct FixtureDecodingTests {

    private func fixture(_ name: String) throws -> Data {
        try Fixtures.data(name, in: Bundle.module)
    }

    @Test func publicEventListDecodesFromLiveCapture() throws {
        let events = try BenJSON.decoder.decode(
            [PublicEventListItem].self, from: fixture("public-events"))
        #expect(!events.isEmpty)

        // Spot-check the first record against the capture. Re-captured 2026-09-11 (item 233):
        // the events it used to name had been removed from the testing database, and a fixture is
        // only worth anything while it is something the API really answered.
        let first = try #require(events.first)
        #expect(first.id == UUID(uuidString: "6c1b69fd-0957-4f2a-8330-f47beb746b4b"))
        #expect(first.organizationName == "Printers Alley Walks")
        #expect(first.organizationUrlName == "pw-tour-1789070429")
        #expect(first.city == "Nashville")
        #expect(first.state == "TN")
        // "2026-09-13T20:08:17" — the naked UTC DateTime shape.
        #expect(BenJSON.parseDate("2026-09-13T20:08:17") == first.startDateTime)

        // The tour a night belongs to, and the clock it runs on (item 233).
        #expect(first.tourName == "Printers Alley Ghost Walk")
        #expect(first.tourUrlName == "printers-alley-ghost-walk")
        #expect(first.timeZoneId == "America/Chicago")

        // An ordinary group night belongs to no tour, and its fields are absent rather than empty.
        let ordinary = try #require(events.first { $0.tourName == nil })
        #expect(ordinary.tourUrlName == nil)
    }

    @Test func publicEventDetailDecodesFromLiveCapture() throws {
        let event = try BenJSON.decoder.decode(
            PublicEventRecord.self, from: fixture("public-event-detail"))
        #expect(event.id == UUID(uuidString: "6c1b69fd-0957-4f2a-8330-f47beb746b4b"))
        // The nested location and flags records decode — structural absence of
        // the exact address for an unentitled reader is a nil, not a crash.
        #expect(event.location.city != nil || event.location.isExactAddressHidden || event.location.exactAddress == nil)
        _ = event.flags.canRsvp

        // The tour half (item 233): who is leading THIS date, and what guests made of the walk.
        #expect(event.tourName == "Printers Alley Ghost Walk")
        let guide = try #require(event.guides?.first)
        #expect(!guide.displayName.isEmpty)
        #expect(event.tourRatingCount >= 0)
        #expect(event.timeZoneId == "America/Chicago")
    }
}
