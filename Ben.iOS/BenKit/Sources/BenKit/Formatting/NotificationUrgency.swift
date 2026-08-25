import Foundation

/// How overdue a bucket is — the Swift twin of `Ben.Web.Services.NotificationBadge`, with the
/// same thresholds so the app and the website can never disagree about what counts as old.
///
/// **Urgency tracks the AGE of the oldest unread item, not the count.** Fifty messages from this
/// morning are a busy day; one unread message from last week is the thing worth escalating, and
/// a count-driven badge gets that exactly backwards.
public enum NotificationUrgency: Sendable, Equatable {
    case none
    /// Arrived within the last day.
    case fresh
    /// Between one and three days old.
    case aging
    /// Three days or older.
    case overdue

    /// Below this age a bucket is routine.
    public static let agingAfter: TimeInterval = 60 * 60 * 24
    /// At or above this age a bucket is overdue.
    public static let overdueAfter: TimeInterval = 60 * 60 * 24 * 3

    public static func classify(_ bucket: NotificationBucket, now: Date = Date()) -> NotificationUrgency {
        guard bucket.count > 0, let oldest = bucket.oldestUnreadUtc else { return .none }
        let age = now.timeIntervalSince(oldest)
        if age >= overdueAfter { return .overdue }
        if age >= agingAfter { return .aging }
        return .fresh
    }

    /// The whole summary, classified by its oldest unread item anywhere.
    public static func classify(_ summary: NotificationSummary, now: Date = Date()) -> NotificationUrgency {
        classify(NotificationBucket(count: summary.totalCount,
                                    oldestUnreadUtc: summary.oldestUnreadUtc), now: now)
    }
}

public enum NotificationText {
    /// Badge text, capped so a large count can't stretch the pill.
    public static func badge(_ count: Int) -> String { count > 99 ? "99+" : "\(count)" }

    /// A plain-language age — "2 days ago" reads better than a timestamp the reader has to
    /// subtract from today. Includes the "ago" so callers can't compose "just now ago".
    public static func describeAge(_ oldest: Date?, now: Date = Date()) -> String {
        guard let oldest else { return "" }
        let age = now.timeIntervalSince(oldest)
        if age < 60 { return "just now" }
        if age < 3600 { return "\(Int(age / 60)) min ago" }
        if age < 86_400 { return plural(Int(age / 3600), "hour") }
        return plural(Int(age / 86_400), "day")
    }

    private static func plural(_ n: Int, _ unit: String) -> String {
        "\(n) \(unit)\(n == 1 ? "" : "s") ago"
    }
}
