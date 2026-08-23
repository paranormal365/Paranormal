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

/// <summary>One group's slice of a cross-org bucket — enough to render a row that links to
/// the group surface holding exactly these items (item 173).</summary>
public sealed record OrgScopedBucket(
    Guid OrganizationId, string OrganizationName, int Count, DateTime? OldestUnreadUtc);

/// <summary>
/// Everything the badge system needs, in one round trip. Split by bucket rather than returned as a
/// single number so the bell popover can say *where* the unread items are and link straight to them.
/// </summary>
/// <param name="OrgMessages">Internal org messages addressed to the caller and not yet read.</param>
/// <param name="CaseMessagesAsOrgMember">Client-sent case messages awaiting an org reply, across every org the caller is an active member of.</param>
/// <param name="CaseMessagesAsClient">Org-sent case messages awaiting the caller on their own cases (including cases shared with them as a co-client).</param>
/// <param name="SystemMessages">Platform/system messages sent to the caller (e.g. an audit record forwarded by a SuperAdmin).</param>
/// <param name="PendingPermissionRequests">File-permission requests waiting on the caller as the file owner.</param>
/// <param name="InvestigationInvites">
/// Investigations the caller has been invited to but not yet answered, limited to ones still
/// ahead of them. The bucket timestamp is when the <i>invite</i> was sent, not when the
/// investigation is scheduled — every other bucket means "waiting since", and a future date fed
/// to the shared age classifier would read as negative age and colour Fresh forever. A soon-but-
/// recently-sent invite is therefore not escalated by colour; the row text carries the date.
/// </param>
/// <param name="FeedMentions">
/// Public-feed posts that named the caller with an <c>@name</c> and that they have not opened.
/// <para>"Not opened" is an <c>OrgMessageView</c> row, the same marker the rest of the messaging
/// system uses, rather than a read flag on the mention itself: a mention is read exactly when the
/// post carrying it has been read, and two markers for one fact would drift apart.</para>
/// <para>Empty whenever the feed is switched off, so a site that has never turned it on shows no
/// trace of it on the bell.</para>
/// </param>
public sealed record NotificationSummaryResponse(
    NotificationBucket OrgMessages,
    NotificationBucket CaseMessagesAsOrgMember,
    NotificationBucket CaseMessagesAsClient,
    NotificationBucket SystemMessages,
    NotificationBucket PendingPermissionRequests,
    NotificationBucket InvestigationInvites,
    NotificationBucket EquipmentCheckouts,
    NotificationBucket FeedMentions,
    // Item 173 (Ben's report): the two cross-org buckets rendered as single rows whose click
    // could not open what they counted — 54 unread across every group, a destination showing
    // one group's 18. Per-group breakdowns let each row open exactly what it counts; the
    // aggregates above stay for the bell's total. Defaulted so pre-173 payloads deserialize.
    IReadOnlyList<OrgScopedBucket>? OrgMessagesByOrg = null,
    IReadOnlyList<OrgScopedBucket>? CaseMessagesAsOrgMemberByOrg = null)
{
    public static readonly NotificationSummaryResponse Empty = new(
        NotificationBucket.Empty, NotificationBucket.Empty, NotificationBucket.Empty,
        NotificationBucket.Empty, NotificationBucket.Empty, NotificationBucket.Empty,
        NotificationBucket.Empty, NotificationBucket.Empty);

    /// <summary>All buckets, for callers that want to iterate rather than name each one.</summary>
    [JsonIgnore]
    public IReadOnlyList<NotificationBucket> AllBuckets =>
        [OrgMessages, CaseMessagesAsOrgMember, CaseMessagesAsClient, SystemMessages,
         PendingPermissionRequests, InvestigationInvites, EquipmentCheckouts, FeedMentions];

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
