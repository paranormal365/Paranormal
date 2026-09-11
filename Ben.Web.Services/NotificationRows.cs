using Ben.Service.Models.Entities;

namespace Ben.Web.Services;

/// <summary>
/// One row of the notification list: what is waiting, where it is, and how many.
/// </summary>
/// <param name="Title">The line somebody reads.</param>
/// <param name="Detail">A second line of context, or null where the title says everything.</param>
/// <param name="Icon">The BenIcon name for the row.</param>
/// <param name="Destination">The URL that opens exactly what this row counts.</param>
/// <param name="Bucket">The count and the age, for the badge.</param>
public sealed record NotificationRow(
    string Title,
    string? Detail,
    string Icon,
    string Destination,
    NotificationBucket Bucket);

/// <summary>
/// The one list of what is waiting on somebody, in one order, for every surface that shows it.
/// </summary>
/// <remarks>
/// <para><b>W-CL3 of the 2026-09-06 evaluation.</b> A client saw a sidebar badge of 6, a bell of 1
/// and a page listing 1, in one glance, all three claiming to be their notifications. Nothing was
/// miscounted. Three surfaces had each been written to enumerate the buckets, each picked a
/// different subset, and each was then given a number computed a different way:</para>
///
/// <list type="bullet">
/// <item>the sidebar badged two of the eight buckets and called the entry "Notifications";</item>
/// <item>the bell badged the sum of all eight and listed seven of them;</item>
/// <item>the page tested the sum of all eight to decide whether to say "you're all caught up",
/// then listed six — it had no row for an investigation invitation or a feed mention, so a person
/// whose only waiting item was one of those was told something was waiting and shown nothing.</item>
/// </list>
///
/// <para>So the rule is not "make the three agree", which is what the last two fixes here did and
/// is why there were three. It is that the number is the sum of the rows, because the number and
/// the rows come from the same list. Add a bucket to the DTO and it must be given a row here, or
/// no surface will show it and the total will exceed what any of them lists.</para>
///
/// <para><b>Ordering is by what waits on you, not by size.</b> An unanswered RSVP is first because
/// the visit happens whether or not you replied; a mention on a public post is last because
/// nothing at all depends on your answer.</para>
/// </remarks>
public static class NotificationRows
{
    /// <summary>Every waiting thing, in the order it should be read.</summary>
    /// <param name="s">The summary as fetched. Empty buckets contribute no row.</param>
    public static IReadOnlyList<NotificationRow> For(NotificationSummaryResponse s)
    {
        var rows = new List<NotificationRow>();

        if (s.InvestigationInvites.Count > 0)
            rows.Add(new("Investigation invitations",
                $"Waiting on your answer · oldest {NotificationBadge.DescribeAge(s.InvestigationInvites.OldestUnreadUtc)}",
                "calendar", "/my-investigations", s.InvestigationInvites));

        if (s.EquipmentCheckouts.Count > 0)
            rows.Add(new("Equipment requests & overdue gear",
                $"Waiting on your decision, or late back · oldest {NotificationBadge.DescribeAge(s.EquipmentCheckouts.OldestUnreadUtc)}",
                "tool", "/my-checkouts", s.EquipmentCheckouts));

        if (s.CaseMessagesAsClient.Count > 0)
            rows.Add(new("Replies on your cases",
                $"From the group handling your case · oldest {NotificationBadge.DescribeAge(s.CaseMessagesAsClient.OldestUnreadUtc)}",
                "folder", "/my-cases", s.CaseMessagesAsClient));

        // Item 173: one row per case, each opening the thread that holds exactly these messages.
        // The per-case list and the aggregate are the same messages counted twice, so the
        // aggregate is used only as the fallback below.
        var caseSlices = s.CaseMessagesAsOrgMemberByCase ?? [];
        foreach (var slice in caseSlices)
            rows.Add(new($"Client messages awaiting a reply · {slice.CaseTitle}",
                $"{slice.OrganizationName} · oldest {NotificationBadge.DescribeAge(slice.OldestUnreadUtc)}",
                "message-square",
                $"/organizations/{slice.OrganizationId}/cases/{slice.CaseId}",
                new NotificationBucket(slice.Count, slice.OldestUnreadUtc)));

        // A payload from before item 173 carries the aggregate and no slices. Without this the
        // rows would sum to less than the badge and the page would look empty while the bell
        // insisted otherwise — the exact failure this class exists to remove.
        if (caseSlices.Count == 0 && s.CaseMessagesAsOrgMember.Count > 0)
            rows.Add(new("Client messages awaiting a reply",
                $"Across your groups · oldest {NotificationBadge.DescribeAge(s.CaseMessagesAsOrgMember.OldestUnreadUtc)}",
                "message-square", "/organizations", s.CaseMessagesAsOrgMember));

        var orgSlices = s.OrgMessagesByOrg ?? [];
        foreach (var slice in orgSlices)
            rows.Add(new($"Unread group messages · {slice.OrganizationName}",
                $"Internal messages addressed to you · oldest {NotificationBadge.DescribeAge(slice.OldestUnreadUtc)}",
                "mail",
                $"/organizations/{slice.OrganizationId}?tab=messages",
                new NotificationBucket(slice.Count, slice.OldestUnreadUtc)));

        if (orgSlices.Count == 0 && s.OrgMessages.Count > 0)
            rows.Add(new("Unread group messages",
                $"Internal messages addressed to you · oldest {NotificationBadge.DescribeAge(s.OrgMessages.OldestUnreadUtc)}",
                "mail", "/organizations", s.OrgMessages));

        if (s.PendingPermissionRequests.Count > 0)
            rows.Add(new("File permission requests",
                $"Waiting on your decision · oldest {NotificationBadge.DescribeAge(s.PendingPermissionRequests.OldestUnreadUtc)}",
                "lock", "/notifications", s.PendingPermissionRequests));

        if (s.SystemMessages.Count > 0)
            rows.Add(new("Unread messages",
                $"Sent to you through the platform · oldest {NotificationBadge.DescribeAge(s.SystemMessages.OldestUnreadUtc)}",
                "bell", "/notifications", s.SystemMessages));

        // ── Tour seats (item 234) ────────────────────────────────────────────
        // The business's queue first: somebody is standing at the other end of it waiting to be
        // told whether they have a place.
        if (s.TourSeatsToDecide is { Count: > 0 } toDecide)
            rows.Add(new("Sign-ups waiting on you",
                $"People asking for places on your tours · oldest {NotificationBadge.DescribeAge(toDecide.OldestUnreadUtc)}",
                "user-check", "/organizations", toDecide));

        if (s.MyTourSeats is { Count: > 0 } mine)
            rows.Add(new("A tour answered you",
                $"Your seat has been decided · {NotificationBadge.DescribeAge(mine.OldestUnreadUtc)}",
                "calendar", "/events", mine));

        // Last: being named on a public post waits on nothing. It still gets a row, because the
        // total counts it and a number that explains everything except one item reads as wrong.
        if (s.FeedMentions.Count > 0)
            rows.Add(new("Mentions on the feed",
                $"Somebody named you in a post · oldest {NotificationBadge.DescribeAge(s.FeedMentions.OldestUnreadUtc)}",
                "at-sign", "/feed", s.FeedMentions));

        return rows;
    }

    /// <summary>
    /// The number on every badge: the sum of the rows above, not of the DTO's buckets.
    /// </summary>
    /// <remarks>
    /// Computed from the rows on purpose. <see cref="NotificationSummaryResponse.TotalCount"/>
    /// sums the eight named buckets, and a bucket with no row here would inflate it past anything
    /// a person can see — which is the shape of the original complaint. Sum what you show.
    /// </remarks>
    public static int TotalFor(NotificationSummaryResponse s) => For(s).Sum(r => r.Bucket.Count);

    /// <summary>The oldest waiting item across the rows, for badge colouring.</summary>
    public static DateTime? OldestFor(NotificationSummaryResponse s)
    {
        var stamps = For(s).Where(r => r.Bucket.OldestUnreadUtc.HasValue)
                           .Select(r => r.Bucket.OldestUnreadUtc!.Value)
                           .ToList();
        return stamps.Count == 0 ? null : stamps.Min();
    }
}
