import Foundation

// Ports of Ben.Service.Models/Entities/NotificationSummaryResponse.cs — everything waiting on
// the signed-in person, in one round trip.

/// One unread bucket: how many are waiting, and when the oldest arrived. The timestamp is the
/// load-bearing half — see `NotificationUrgency`.
public struct NotificationBucket: Sendable, Codable, Equatable {
    public var count: Int
    public var oldestUnreadUtc: Date?

    public static let empty = NotificationBucket(count: 0, oldestUnreadUtc: nil)

    public init(count: Int, oldestUnreadUtc: Date?) {
        self.count = count
        self.oldestUnreadUtc = oldestUnreadUtc
    }
}

/// One group's slice of a cross-org bucket — enough to open exactly what it counts.
public struct OrgScopedBucket: Sendable, Codable, Equatable, Identifiable {
    public var organizationId: UUID
    public var organizationName: String
    public var count: Int
    public var oldestUnreadUtc: Date?

    public var id: UUID { organizationId }
}

/// One case's slice — the case's own thread is the only surface that answers these.
public struct CaseScopedBucket: Sendable, Codable, Equatable, Identifiable {
    public var caseId: UUID
    public var organizationId: UUID
    public var caseTitle: String
    public var organizationName: String
    public var count: Int
    public var oldestUnreadUtc: Date?

    public var id: UUID { caseId }
}

/// Everything the badge system needs. Split by bucket rather than one number so the app can say
/// *where* the unread items are and open them.
public struct NotificationSummary: Sendable, Codable, Equatable {
    public var orgMessages: NotificationBucket
    public var caseMessagesAsOrgMember: NotificationBucket
    public var caseMessagesAsClient: NotificationBucket
    public var systemMessages: NotificationBucket
    public var pendingPermissionRequests: NotificationBucket
    public var investigationInvites: NotificationBucket
    public var equipmentCheckouts: NotificationBucket
    public var feedMentions: NotificationBucket

    /// Per-group and per-case breakdowns (item 173). Optional because the server defaults them
    /// for older callers; absent is not the same as empty, but both render as no rows.
    public var orgMessagesByOrg: [OrgScopedBucket]?
    public var caseMessagesAsOrgMemberByCase: [CaseScopedBucket]?

    public static let empty = NotificationSummary(
        orgMessages: .empty, caseMessagesAsOrgMember: .empty, caseMessagesAsClient: .empty,
        systemMessages: .empty, pendingPermissionRequests: .empty, investigationInvites: .empty,
        equipmentCheckouts: .empty, feedMentions: .empty,
        orgMessagesByOrg: nil, caseMessagesAsOrgMemberByCase: nil)

    public var allBuckets: [NotificationBucket] {
        [orgMessages, caseMessagesAsOrgMember, caseMessagesAsClient, systemMessages,
         pendingPermissionRequests, investigationInvites, equipmentCheckouts, feedMentions]
    }

    /// The number on the tab badge.
    public var totalCount: Int { allBuckets.reduce(0) { $0 + $1.count } }

    /// Arrival time of the oldest unread item anywhere — what decides the badge's urgency.
    public var oldestUnreadUtc: Date? {
        allBuckets.compactMap(\.oldestUnreadUtc).min()
    }
}
