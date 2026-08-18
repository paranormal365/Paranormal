using Ben.Service.Models.Entities;

namespace Ben.Web.Services;

/// <summary>
/// The one place badge urgency is decided, so the bell, the drawer, and anything added later can't
/// drift into disagreeing about what counts as old.
/// </summary>
/// <remarks>
/// Colour tracks the <i>age of the oldest unread item</i>, not the count. Fifty messages from this
/// morning are a busy day; one unread message from last week is the thing worth escalating, and a
/// count-driven badge gets that exactly backwards.
/// </remarks>
public static class NotificationBadge
{
    /// <summary>Below this age a bucket is routine.</summary>
    public static readonly TimeSpan AgingAfter = TimeSpan.FromDays(1);

    /// <summary>At or above this age a bucket is overdue.</summary>
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromDays(3);

    /// <summary>How overdue a bucket is, from the age of its oldest unread item.</summary>
    public enum Urgency
    {
        /// <summary>Nothing waiting.</summary>
        None,
        /// <summary>Arrived within the last day.</summary>
        Fresh,
        /// <summary>Between one and three days old.</summary>
        Aging,
        /// <summary>Three days or older.</summary>
        Overdue,
    }

    /// <summary>Classifies a bucket. <paramref name="nowUtc"/> is injectable for tests.</summary>
    public static Urgency Classify(NotificationBucket bucket, DateTime? nowUtc = null)
    {
        if (bucket.Count <= 0 || bucket.OldestUnreadUtc is not { } oldest) return Urgency.None;

        var age = (nowUtc ?? DateTime.UtcNow) - oldest;
        if (age >= OverdueAfter) return Urgency.Overdue;
        if (age >= AgingAfter)   return Urgency.Aging;
        return Urgency.Fresh;
    }

    /// <summary>Classifies a whole summary by its oldest unread item across every bucket.</summary>
    public static Urgency Classify(NotificationSummaryResponse summary, DateTime? nowUtc = null) =>
        Classify(new NotificationBucket(summary.TotalCount, summary.OldestUnreadUtc), nowUtc);

    /// <summary>Bootstrap badge classes for an urgency, matching the pill badges already in use.</summary>
    public static string CssClass(Urgency urgency) => urgency switch
    {
        Urgency.Overdue => "badge rounded-pill bg-danger",
        Urgency.Aging   => "badge rounded-pill bg-warning text-dark",
        Urgency.Fresh   => "badge rounded-pill bg-primary",
        _               => "badge rounded-pill bg-secondary",
    };

    /// <summary>Convenience for markup: classes for a bucket in one call.</summary>
    public static string CssClass(NotificationBucket bucket, DateTime? nowUtc = null) =>
        CssClass(Classify(bucket, nowUtc));

    /// <summary>Convenience for markup: classes for the roll-up badge on the bell.</summary>
    public static string CssClass(NotificationSummaryResponse summary, DateTime? nowUtc = null) =>
        CssClass(Classify(summary, nowUtc));

    /// <summary>
    /// Badge text, capped so a large count can't stretch the pill out of the app bar.
    /// </summary>
    public static string Text(int count) => count > 99 ? "99+" : count.ToString();

    /// <summary>
    /// A plain-language age — "2 days ago" reads better than a timestamp the reader has to subtract
    /// from today. Includes the "ago" so callers can't compose "just now ago".
    /// </summary>
    public static string DescribeAge(DateTime? oldestUtc, DateTime? nowUtc = null)
    {
        if (oldestUtc is not { } oldest) return string.Empty;

        var age = (nowUtc ?? DateTime.UtcNow) - oldest;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1))   return $"{(int)age.TotalMinutes} min ago";
        if (age < TimeSpan.FromDays(1))    return Plural((int)age.TotalHours, "hour");
        return Plural((int)age.TotalDays, "day");

        static string Plural(int n, string unit) => $"{n} {unit}{(n == 1 ? "" : "s")} ago";
    }
}
