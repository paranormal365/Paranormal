import Foundation
import Testing
@testable import BenKit
import BenKitTestSupport

/// Notifications (iOS Slice 5): the urgency rule, the rows, and the states that are facts
/// rather than errors.
@Suite("Notification urgency — age, never count")
struct NotificationUrgencyTests {

    private let now = Date(timeIntervalSince1970: 1_800_000_000)

    private func bucket(_ count: Int, daysOld: Double?) -> NotificationBucket {
        NotificationBucket(count: count,
                           oldestUnreadUtc: daysOld.map { now.addingTimeInterval(-$0 * 86_400) })
    }

    @Test func fiftyFreshItemsAreCalmerThanOneOldOne() {
        // The whole point of the rule, and the thing a count-driven badge gets backwards.
        let busyMorning = bucket(50, daysOld: 0.2)
        let oneFromLastWeek = bucket(1, daysOld: 7)

        #expect(NotificationUrgency.classify(busyMorning, now: now) == .fresh)
        #expect(NotificationUrgency.classify(oneFromLastWeek, now: now) == .overdue)
    }

    @Test func thresholdsMatchTheWebsitesExactly() {
        // 1 day and 3 days, inclusive at the boundary — the same numbers as
        // Ben.Web.Services.NotificationBadge, so the two front ends cannot disagree.
        #expect(NotificationUrgency.classify(bucket(1, daysOld: 0.99), now: now) == .fresh)
        #expect(NotificationUrgency.classify(bucket(1, daysOld: 1), now: now) == .aging)
        #expect(NotificationUrgency.classify(bucket(1, daysOld: 2.99), now: now) == .aging)
        #expect(NotificationUrgency.classify(bucket(1, daysOld: 3), now: now) == .overdue)
    }

    @Test func anEmptyBucketIsNeverUrgentEvenWithATimestamp() {
        #expect(NotificationUrgency.classify(bucket(0, daysOld: 30), now: now) == .none)
        #expect(NotificationUrgency.classify(bucket(3, daysOld: nil), now: now) == .none)
    }

    @Test func ageReadsAsPlainLanguage() {
        #expect(NotificationText.describeAge(now.addingTimeInterval(-30), now: now) == "just now")
        #expect(NotificationText.describeAge(now.addingTimeInterval(-600), now: now) == "10 min ago")
        #expect(NotificationText.describeAge(now.addingTimeInterval(-3600), now: now) == "1 hour ago")
        #expect(NotificationText.describeAge(now.addingTimeInterval(-7200), now: now) == "2 hours ago")
        #expect(NotificationText.describeAge(now.addingTimeInterval(-86_400), now: now) == "1 day ago")
        #expect(NotificationText.describeAge(nil, now: now) == "")
    }

    @Test func badgeTextIsCappedSoThePillCannotStretch() {
        #expect(NotificationText.badge(0) == "0")
        #expect(NotificationText.badge(99) == "99")
        #expect(NotificationText.badge(100) == "99+")
    }
}

@Suite("NotificationsStore — the live shape, the rows, the honest states")
@MainActor
struct NotificationsStoreTests {

    private static func store(_ transport: MockTransport) -> NotificationsStore {
        let tokens = TokenSession(storage: InMemoryTokenStorage(), transport: transport, environment: { .dev })
        return NotificationsStore(api: APIClient(environment: { .dev }, transport: transport, tokens: tokens))
    }

    @Test func theLiveFixtureDecodesWithBothBreakdowns() throws {
        let data = try Fixtures.data("notification-summary", in: Bundle.module)
        let summary = try BenJSON.decoder.decode(NotificationSummary.self, from: data)

        #expect(summary.totalCount > 0)
        // Captured from a real account with both per-group and per-case slices — the item-173
        // shape, which is what makes a row able to open exactly what it counts.
        #expect(!(summary.orgMessagesByOrg ?? []).isEmpty)
        #expect(!(summary.caseMessagesAsOrgMemberByCase ?? []).isEmpty)
        // The roll-up equals what the rows can open, which is the invariant item 173 fixed.
        let byOrg = (summary.orgMessagesByOrg ?? []).reduce(0) { $0 + $1.count }
        #expect(byOrg == summary.orgMessages.count)
    }

    @Test func rowsFromTheLiveFixtureOpenWhatTheyCount() async throws {
        let data = try Fixtures.data("notification-summary", in: Bundle.module)
        let summary = try BenJSON.decoder.decode(NotificationSummary.self, from: data)

        // Through the real load path with the captured payload — no test-only back door
        // into the store, so this exercises decode and rows together.
        let store = Self.store(MockTransport(status: 200, body: data))
        await store.load()
        let rows = store.rows

        #expect(!rows.isEmpty)
        // Every per-case row points at that case on its group's side.
        for slice in summary.caseMessagesAsOrgMemberByCase ?? [] {
            let row = try #require(rows.first { $0.id == "case-\(slice.caseId)" })
            #expect(row.destination == .orgCase(organizationId: slice.organizationId,
                                                caseId: slice.caseId))
            #expect(row.bucket.count == slice.count)
        }
        // Row counts never exceed the total they roll up into.
        #expect(rows.reduce(0) { $0 + $1.bucket.count } <= summary.totalCount)
    }

    @Test func a401IsSignedOutNotAnError() async {
        let store = Self.store(MockTransport(status: 401))
        await store.load()
        #expect(store.state == .signedOut)
        #expect(store.badgeCount == 0)
    }

    @Test func aServerFailureKeepsItsSentence() async {
        let store = Self.store(MockTransport(status: 500, body: Data("The server fell over.".utf8)))
        await store.load()
        #expect(store.state == .failed(reason: "The server fell over."))
    }

    @Test func clearingRemovesTheBadgeSoItCannotOutliveTheSession() async {
        let body = Data("""
        {"orgMessages":{"count":4,"oldestUnreadUtc":"2026-08-20T10:00:00Z"},
         "caseMessagesAsOrgMember":{"count":0,"oldestUnreadUtc":null},
         "caseMessagesAsClient":{"count":0,"oldestUnreadUtc":null},
         "systemMessages":{"count":0,"oldestUnreadUtc":null},
         "pendingPermissionRequests":{"count":0,"oldestUnreadUtc":null},
         "investigationInvites":{"count":0,"oldestUnreadUtc":null},
         "equipmentCheckouts":{"count":0,"oldestUnreadUtc":null},
         "feedMentions":{"count":0,"oldestUnreadUtc":null},
         "orgMessagesByOrg":null,"caseMessagesAsOrgMemberByCase":null}
        """.utf8)
        let store = Self.store(MockTransport(status: 200, body: body))
        await store.load()
        #expect(store.badgeCount == 4)

        store.clear()
        #expect(store.badgeCount == 0)
        #expect(store.state == .idle)
    }

    @Test func aRowWithNoAppScreenSaysSoRatherThanFakingALink() async {
        // Group messaging has no app screen until a later slice. The count is still news, so
        // the row shows — but it must not claim to open something.
        let body = Data("""
        {"orgMessages":{"count":2,"oldestUnreadUtc":"2026-08-20T10:00:00Z"},
         "caseMessagesAsOrgMember":{"count":0,"oldestUnreadUtc":null},
         "caseMessagesAsClient":{"count":0,"oldestUnreadUtc":null},
         "systemMessages":{"count":0,"oldestUnreadUtc":null},
         "pendingPermissionRequests":{"count":0,"oldestUnreadUtc":null},
         "investigationInvites":{"count":0,"oldestUnreadUtc":null},
         "equipmentCheckouts":{"count":0,"oldestUnreadUtc":null},
         "feedMentions":{"count":0,"oldestUnreadUtc":null},
         "orgMessagesByOrg":[{"organizationId":"11111111-1111-1111-1111-111111111111",
                              "organizationName":"BenCo","count":2,
                              "oldestUnreadUtc":"2026-08-20T10:00:00Z"}],
         "caseMessagesAsOrgMemberByCase":null}
        """.utf8)
        let store = Self.store(MockTransport(status: 200, body: body))
        await store.load()

        let row = store.rows.first { $0.id.hasPrefix("org-") }
        #expect(row?.destination == nil)
        #expect(row?.title.contains("BenCo") == true)
    }
}
