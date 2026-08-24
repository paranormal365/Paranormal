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

        // Spot-check the first record against known seed data.
        let first = try #require(events.first)
        #expect(first.id == UUID(uuidString: "5c9e27dc-d801-4e6b-934d-6839fd9bc6aa"))
        #expect(first.organizationName == "Tennessee Ghost Hunters")
        #expect(first.organizationUrlName == "tgh")
        #expect(first.city == "Adams")
        #expect(first.state == "TN")
        // "2026-07-23T20:00:00" — the naked UTC DateTime shape.
        #expect(BenJSON.parseDate("2026-07-23T20:00:00") == first.startDateTime)
    }

    @Test func publicEventDetailDecodesFromLiveCapture() throws {
        let event = try BenJSON.decoder.decode(
            PublicEventRecord.self, from: fixture("public-event-detail"))
        #expect(event.id == UUID(uuidString: "5c9e27dc-d801-4e6b-934d-6839fd9bc6aa"))
        // The nested location and flags records decode — structural absence of
        // the exact address for an unentitled reader is a nil, not a crash.
        #expect(event.location.city != nil || event.location.isExactAddressHidden || event.location.exactAddress == nil)
        _ = event.flags.canRsvp
    }
}
