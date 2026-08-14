using System.Text.Json.Serialization;

namespace Ben.Service.Models.Entities;

/// <summary>
/// One unread bucket: how many items are waiting, and when the oldest of them arrived.
/// The timestamp drives age-based badge colouring — a single unread item from last week
/// deserves more attention than five from this morning.
/// </summary>
/// <param name="Count">Number of unread/pending items.</param>
/// <param name="OldestUnreadUtc">UTC arrival time of the oldest item, or null when the bucket is empty.</param>
public sealed record NotificationBucket(int Count, DateTime? OldestUnreadUtc)
{
    public static readonly NotificationBucket Empty = new(0, null);
}

/// <summary>
/// Everything the badge system needs, in one round trip. Split by bucket rather than returned as a
/// single number so the bell popover can say *where* the unread items are and link straight to them.
/// </summary>
/// <param name="OrgMessages">Internal org messages addressed to the caller and not yet read.</param>
/// <param name="CaseMessagesAsOrgMember">Client-sent case messages awaiting an org reply, across every org the caller is an active member of.</param>
/// <param name="CaseMessagesAsClient">Org-sent case messages awaiting the caller on their own cases (including cases shared with them as a co-client).</param>
/// <param name="SystemMessages">Platform/system messages sent to the caller (e.g. an audit record forwarded by a SuperAdmin).</param>
/// <param name="PendingPermissionRequests">File-permission requests waiting on the caller as the file owner.</param>
public sealed record NotificationSummaryResponse(
    NotificationBucket OrgMessages,
    NotificationBucket CaseMessagesAsOrgMember,
    NotificationBucket CaseMessagesAsClient,
    NotificationBucket SystemMessages,
    NotificationBucket PendingPermissionRequests)
{
    public static readonly NotificationSummaryResponse Empty = new(
        NotificationBucket.Empty, NotificationBucket.Empty, NotificationBucket.Empty,
        NotificationBucket.Empty, NotificationBucket.Empty);

    /// <summary>All buckets, for callers that want to iterate rather than name each one.</summary>
    [JsonIgnore]
    public IReadOnlyList<NotificationBucket> AllBuckets =>
        [OrgMessages, CaseMessagesAsOrgMember, CaseMessagesAsClient, SystemMessages, PendingPermissionRequests];

    /// <summary>Total across every bucket — the number on the bell.</summary>
    [JsonIgnore]
    public int TotalCount => AllBuckets.Sum(b => b.Count);

    /// <summary>Arrival time of the oldest unread item anywhere, or null when nothing is waiting.</summary>
    [JsonIgnore]
    public DateTime? OldestUnreadUtc =>
        AllBuckets.Where(b => b.OldestUnreadUtc.HasValue)
                  .Select(b => b.OldestUnreadUtc!.Value)
                  .DefaultIfEmpty()
                  .Min() is var oldest && oldest == default ? null : oldest;
}
