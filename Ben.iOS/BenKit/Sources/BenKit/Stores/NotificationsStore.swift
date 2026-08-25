import Foundation
import Observation

/// One line on the notifications screen: what is waiting, how old, and where it opens.
public struct NotificationRow: Sendable, Identifiable, Equatable {
    public var id: String
    public var title: String
    public var detail: String
    public var systemImage: String
    public var bucket: NotificationBucket
    /// Where tapping goes. Nil means nothing in the app opens this yet — the row still shows,
    /// because a count with no destination is still news, but it must not pretend to be a link.
    public var destination: DeepLink?

    public var urgency: NotificationUrgency { NotificationUrgency.classify(bucket) }
}

/// Everything waiting on the signed-in person, and the rows that open it.
///
/// The row set mirrors the website's notifications page deliberately: the same buckets, the same
/// wording, the same order. Somebody who uses both should not have to learn the site twice.
@Observable
@MainActor
public final class NotificationsStore {
    public enum State: Equatable {
        case idle
        case loading
        case loaded
        /// Reading notifications requires an account — this is not an error, it's a fact
        /// about who is asking.
        case signedOut
        case failed(reason: String?)
    }

    public private(set) var state: State = .idle
    public private(set) var summary: NotificationSummary = .empty

    private let api: APIClient

    public init(api: APIClient) {
        self.api = api
    }

    /// The number on the tab badge; 0 hides it.
    public var badgeCount: Int { summary.totalCount }
    public var urgency: NotificationUrgency { NotificationUrgency.classify(summary) }

    public func load() async {
        if case .loaded = state {} else { state = .loading }

        switch await api.load(Endpoint(.get, "api/me/notification-summary"), as: NotificationSummary.self) {
        case .ok(let summary):
            self.summary = summary
            state = .loaded
        case .sessionEnded:
            // Signed out mid-session, or never signed in. Not an error to apologize for.
            summary = .empty
            state = .signedOut
        case .failed(_, let statusCode) where statusCode == 401:
            summary = .empty
            state = .signedOut
        case .failed(let reason, _):
            state = .failed(reason: reason)
        case .rateLimited:
            state = .failed(reason: "Too many requests — try again shortly.")
        }
    }

    /// Clears everything on sign-out, so the badge cannot outlive the session that earned it.
    public func clear() {
        summary = .empty
        state = .idle
    }

    /// The rows, in the website's order. Per-group and per-case slices come first because they
    /// open exactly what they count; the aggregates they roll up are deliberately not repeated.
    public var rows: [NotificationRow] {
        var rows: [NotificationRow] = []

        if summary.caseMessagesAsClient.count > 0 {
            rows.append(NotificationRow(
                id: "case-client",
                title: "Replies on your cases",
                detail: "From the group handling your case · oldest "
                      + NotificationText.describeAge(summary.caseMessagesAsClient.oldestUnreadUtc),
                systemImage: "folder",
                bucket: summary.caseMessagesAsClient,
                destination: .myCases))
        }

        for slice in summary.caseMessagesAsOrgMemberByCase ?? [] {
            rows.append(NotificationRow(
                id: "case-\(slice.caseId)",
                title: "Client messages awaiting a reply · \(slice.caseTitle)",
                detail: "\(slice.organizationName) · oldest "
                      + NotificationText.describeAge(slice.oldestUnreadUtc),
                systemImage: "bubble.left.and.bubble.right",
                bucket: NotificationBucket(count: slice.count, oldestUnreadUtc: slice.oldestUnreadUtc),
                destination: .orgCase(organizationId: slice.organizationId, caseId: slice.caseId)))
        }

        for slice in summary.orgMessagesByOrg ?? [] {
            rows.append(NotificationRow(
                id: "org-\(slice.organizationId)",
                title: "Unread group messages · \(slice.organizationName)",
                detail: "Internal messages addressed to you · oldest "
                      + NotificationText.describeAge(slice.oldestUnreadUtc),
                systemImage: "envelope",
                bucket: NotificationBucket(count: slice.count, oldestUnreadUtc: slice.oldestUnreadUtc),
                // Group messaging has no app screen yet (Slice 7). The row is honest about
                // what is waiting and simply doesn't claim to open it.
                destination: nil))
        }

        if summary.feedMentions.count > 0 {
            rows.append(NotificationRow(
                id: "feed-mentions",
                title: "You were mentioned",
                detail: "In posts on the feed · oldest "
                      + NotificationText.describeAge(summary.feedMentions.oldestUnreadUtc),
                systemImage: "at",
                bucket: summary.feedMentions,
                destination: .feed))
        }

        if summary.investigationInvites.count > 0 {
            rows.append(NotificationRow(
                id: "investigation-invites",
                title: "Investigation invitations",
                detail: "Waiting on your answer",
                systemImage: "binoculars",
                bucket: summary.investigationInvites,
                destination: .myInvestigations))
        }

        if summary.equipmentCheckouts.count > 0 {
            rows.append(NotificationRow(
                id: "equipment",
                title: "Equipment requests & overdue gear",
                detail: "Waiting on your decision, or late back · oldest "
                      + NotificationText.describeAge(summary.equipmentCheckouts.oldestUnreadUtc),
                systemImage: "wrench.and.screwdriver",
                bucket: summary.equipmentCheckouts,
                destination: nil))
        }

        if summary.pendingPermissionRequests.count > 0 {
            rows.append(NotificationRow(
                id: "permissions",
                title: "File permission requests",
                detail: "Waiting on your decision · oldest "
                      + NotificationText.describeAge(summary.pendingPermissionRequests.oldestUnreadUtc),
                systemImage: "lock",
                bucket: summary.pendingPermissionRequests,
                destination: nil))
        }

        if summary.systemMessages.count > 0 {
            rows.append(NotificationRow(
                id: "system",
                title: "Unread messages",
                detail: "Sent to you through the platform · oldest "
                      + NotificationText.describeAge(summary.systemMessages.oldestUnreadUtc),
                systemImage: "bell",
                bucket: summary.systemMessages,
                destination: nil))
        }

        return rows
    }
}
