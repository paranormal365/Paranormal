import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Every field the server can send as null, decoded as null.
///
/// **iOS-3 of the 2026-09-06 evaluation, and the class it belongs to.** `api/my-investigations`
/// sends `"didAttend": null` for an attendee nobody has marked either way. BenKit declared a
/// non-optional `Bool`, so one unmarked roster entry made the **entire roster** fail to decode on
/// iPhone and iPad — the screen read "Couldn't load your investigations", and iOS-7 followed from
/// it: the Send sheet could not offer the visit, because the visit had never arrived.
///
/// The fixture was captured from a database where every attendee happened to be marked. That is
/// how a field ends up non-optional: it is never null in the sample. Adding the null case to that
/// one fixture fixes that one field, and leaves every other field in every other fixture waiting
/// to do the same thing.
///
/// **So this asks the question of every key at once.** For each fixture, each key in turn is set
/// to null everywhere it appears and the document is decoded again. A key that breaks decoding is
/// one the app REQUIRES the server to send — which is a real claim, sometimes a correct one, and
/// always worth writing down. The list below is that claim, per fixture. A field that starts
/// arriving as null and is not on the list fails here rather than on somebody's phone.
///
/// **Adding a name to a list is a decision, not a fix.** It says "the server contract guarantees
/// this is never null". Before adding one, check the C# record in `Ben.Service.Models` — if the
/// property is nullable there, the Swift model is what needs to change.
@Suite("Fixtures decode when any nullable field arrives null")
struct FixtureNullDriftTests {

    // ── The claims ──────────────────────────────────────────────────────────

    /// Keys the app genuinely requires, per fixture. Everything else must survive being null.
    ///
    /// **Every name below was checked against its C# record in `Ben.Service.Models` when it was
    /// added.** Each is a non-nullable property there — a `Guid`, an `int`, a `bool`, a `string`,
    /// or a collection, which System.Text.Json writes as `[]` and never as null. So the app is
    /// right to require them, and this list is the record of having asked.
    ///
    /// The first run of this test reported exactly these and nothing else, which is the answer
    /// you want: the fields that had escaped were already found (iOS-3), and the models are in
    /// step with the contract today.
    private static let requiredKeys: [String: Set<String>] = [
        "my-cases": ["caseId", "caseReference", "title", "status", "dateCaseOpened"],

        // The collections and counts on a case detail. C# writes an empty collection as [],
        // and `int`/`bool` cannot be null.
        "my-case-detail": ["caseId", "caseReference", "title", "status", "dateCaseOpened",
                           "id", "entryType", "dateCreated",
                           "occurrences", "investigations", "contacts", "files",
                           "experienceTypeIds", "fromInvestigators",
                           "unreadMessageCount", "isPrimaryClient"],

        // didAttend is deliberately NOT here — it is the field iOS-3 was about, it is `bool?` on
        // the server, and the fixture carries a live-captured null for it.
        "my-investigations": ["attendeeId", "investigationId", "orgId", "orgName", "title",
                              "status", "rsvp"],

        // `bool WasLead` on AttendedInvestigationItem.
        "investigations-attended": ["investigationId", "title", "organizationId",
                                    "organizationName", "wasLead"],

        // PublicEventListItem: OrganizationId, OrganizationUrlName, EndDateTime, IsAllDay,
        // AttendingCount and IsOnline are all non-nullable there. TourName, TourUrlName and
        // TimeZoneId (item 233) are NOT in this list on purpose — they are null on every event
        // that belongs to no tour, which is most of them.
        "public-events": ["id", "organizationName", "title", "startDateTime",
                          "organizationId", "organizationUrlName", "endDateTime",
                          "isAllDay", "attendingCount", "isOnline"],

        // The eight buckets are non-nullable records, and the per-org and per-case slices carry
        // non-nullable ids and names. Only their OldestUnreadUtc is nullable, and it survives.
        "notification-summary": ["count",
                                 "orgMessages", "caseMessagesAsOrgMember", "caseMessagesAsClient",
                                 "systemMessages", "pendingPermissionRequests",
                                 "investigationInvites", "equipmentCheckouts", "feedMentions",
                                 "caseId", "caseTitle", "organizationId", "organizationName"],
    ]

    /// The fixtures this covers, in the order they read.
    private static let fixtureNames = [
        "investigations-attended", "my-case-detail", "my-cases", "my-investigations",
        "notification-summary", "public-events",
    ]

    /// Decodes one fixture as the type the app really uses for it.
    ///
    /// A switch rather than a dictionary of closures: a stored table of functions is not Sendable,
    /// and making it so would mean an actor around something that is pure.
    private func decode(_ fixtureName: String, _ data: Data) throws {
        switch fixtureName {
        case "my-cases":                _ = try BenJSON.decoder.decode([MyCaseSummary].self, from: data)
        case "my-case-detail":          _ = try BenJSON.decoder.decode(MyCaseDetail.self, from: data)
        case "my-investigations":       _ = try BenJSON.decoder.decode([MyInvestigation].self, from: data)
        case "investigations-attended": _ = try BenJSON.decoder.decode([AttendedInvestigation].self, from: data)
        case "public-events":           _ = try BenJSON.decoder.decode([PublicEventListItem].self, from: data)
        case "notification-summary":    _ = try BenJSON.decoder.decode(NotificationSummary.self, from: data)
        default:
            Issue.record("No decoder for fixture '\(fixtureName)'.")
        }
    }

    // ── The walk ────────────────────────────────────────────────────────────

    /// Every key name anywhere in the document.
    private func keys(in json: Any) -> Set<String> {
        switch json {
        case let object as [String: Any]:
            return object.reduce(into: Set(object.keys)) { $0.formUnion(keys(in: $1.value)) }
        case let array as [Any]:
            return array.reduce(into: Set<String>()) { $0.formUnion(keys(in: $1)) }
        default:
            return []
        }
    }

    /// The document with every occurrence of `key` set to null.
    private func nulling(_ key: String, in json: Any) -> Any {
        switch json {
        case let object as [String: Any]:
            var out: [String: Any] = [:]
            for (name, value) in object {
                out[name] = (name == key) ? NSNull() : nulling(key, in: value)
            }
            return out
        case let array as [Any]:
            return array.map { nulling(key, in: $0) }
        default:
            return json
        }
    }

    @Test("Any nullable field can arrive null without losing the whole payload",
          arguments: FixtureNullDriftTests.fixtureNames)
    func nullingAnyFieldStillDecodes(fixtureName: String) throws {
        let required = Self.requiredKeys[fixtureName] ?? []

        let original = try Fixtures.data(fixtureName, in: Bundle.module)
        let json = try JSONSerialization.jsonObject(with: original)

        // The fixture itself must decode, or everything below is measuring the wrong thing.
        try decode(fixtureName, original)

        var brokeButIsNotClaimedRequired: [String] = []

        for key in keys(in: json).sorted() {
            let mutated = try JSONSerialization.data(withJSONObject: nulling(key, in: json))
            do {
                try decode(fixtureName, mutated)
            } catch {
                if !required.contains(key) { brokeButIsNotClaimedRequired.append(key) }
            }
        }

        #expect(brokeButIsNotClaimedRequired.isEmpty, """
            \(fixtureName): these fields make the WHOLE payload fail to decode when the server \
            sends them as null — \(brokeButIsNotClaimedRequired.joined(separator: ", ")).

            One unmarked roster entry did exactly this to the investigations list (iOS-3). Make \
            the Swift property optional, or — if the server truly never sends null — add the name \
            to requiredKeys in this file and say why.
            """)
    }

    /// The claims are claims about fields that exist.
    ///
    /// Without this, a renamed field would leave its name sitting in `requiredKeys` forever,
    /// quietly excusing whatever took its place.
    @Test("Every field claimed as required is actually in its fixture",
          arguments: FixtureNullDriftTests.fixtureNames)
    func requiredKeysStillExist(fixtureName: String) throws {
        let json = try JSONSerialization.jsonObject(
            with: try Fixtures.data(fixtureName, in: Bundle.module))
        let present = keys(in: json)

        for claimed in Self.requiredKeys[fixtureName] ?? [] {
            #expect(present.contains(claimed),
                    "\(fixtureName) has no field called '\(claimed)', but it is listed as required.")
        }
    }
}
