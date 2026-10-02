using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The one list of what is waiting on somebody (W-CL3, 2026-09-06 evaluation).
/// </summary>
/// <remarks>
/// The complaint was three numbers in one glance: a sidebar badge of 6, a bell of 1, a page
/// listing 1. Every one of them was arithmetically correct about a different subset of the same
/// eight buckets. So what is asserted here is not any particular number — it is that the number
/// and the rows are the same list, and that no bucket can be counted without being shown.
/// </remarks>
public class NotificationRowsTests
{
    private static readonly DateTime Then = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static NotificationBucket Waiting(int count) => new(count, Then);

    /// <summary>
    /// The heart of it: a bucket that gets counted must also get a row.
    /// </summary>
    /// <remarks>
    /// <para>Written by filling every bucket at once rather than one at a time on purpose. A
    /// per-bucket test proves each bucket works and says nothing about the one somebody adds to
    /// the DTO next year — this fails the moment a ninth bucket is counted and not rendered.</para>
    ///
    /// <para>It is also the test that fails against the code as it stood: the page's own row
    /// builder had no case for InvestigationInvites or FeedMentions.</para>
    /// </remarks>
    [Fact]
    public void Every_bucket_that_is_counted_is_also_shown()
    {
        var summary = new NotificationSummaryResponse(
            OrgMessages:               Waiting(1),
            CaseMessagesAsOrgMember:   Waiting(2),
            CaseMessagesAsClient:      Waiting(3),
            SystemMessages:            Waiting(4),
            PendingPermissionRequests: Waiting(5),
            InvestigationInvites:      Waiting(6),
            EquipmentCheckouts:        Waiting(7),
            FeedMentions:              Waiting(8));

        Assert.Equal(summary.TotalCount, NotificationRows.TotalFor(summary));
        Assert.Equal(36, NotificationRows.TotalFor(summary));
    }

    [Fact]
    public void No_row_leads_to_the_bare_notifications_page_it_is_listed_on()
    {
        // "Unread messages" and "File permission requests" both went to plain /notifications —
        // from the bell they stopped at the top of the page; on the page they went nowhere and
        // the count stayed (Ben, 2026-10-02). Each now names what it opens.
        var summary = new NotificationSummaryResponse(
            OrgMessages:               Waiting(1),
            CaseMessagesAsOrgMember:   Waiting(2),
            CaseMessagesAsClient:      Waiting(3),
            SystemMessages:            Waiting(4),
            PendingPermissionRequests: Waiting(5),
            InvestigationInvites:      Waiting(6),
            EquipmentCheckouts:        Waiting(7),
            FeedMentions:              Waiting(8));

        var rows = NotificationRows.For(summary);
        Assert.DoesNotContain(rows, r => r.Destination == "/notifications");
        Assert.Equal("/notifications?open=unread", rows.Single(r => r.Title == "Unread messages").Destination);
        Assert.Equal("/feed?mode=mentions", rows.Single(r => r.Title == "Mentions on the feed").Destination);
    }

    [Fact]
    public void A_waiting_row_opens_the_page_its_bucket_names_and_the_groups_list_only_without_one()
    {
        // Tour sign-ups, venue requests, holds and bookings all opened /organizations — the list
        // of every group (walk, 2026-10-02). The server now names the page of the oldest item.
        var named = NotificationSummaryResponse.Empty with
        {
            TourSeatsToDecide = new(2, Then, "/organizations/o1/tours/t1/dates/d1"),
            EventBookingsToDecide = new(1, Then, "/organizations/o1/events/e1/bookings"),
            EventHoldsLapsing = new(1, Then, "/organizations/o1/events/e2/bookings"),
            VenueRequestsToDecide = new(1, Then, "/organizations/v1/venue-requests"),
        };
        var rows = NotificationRows.For(named);
        Assert.Equal("/organizations/o1/tours/t1/dates/d1", rows.Single(r => r.Title == "Sign-ups waiting on you").Destination);
        Assert.Equal("/organizations/o1/events/e1/bookings", rows.Single(r => r.Title == "Bookings waiting on you").Destination);
        Assert.Equal("/organizations/o1/events/e2/bookings", rows.Single(r => r.Title == "Holds running out").Destination);
        Assert.Equal("/organizations/v1/venue-requests", rows.Single(r => r.Title == "Groups asking to use your venue").Destination);

        // An older server that names nothing still lands somewhere real.
        var unnamed = NotificationSummaryResponse.Empty with { TourSeatsToDecide = Waiting(1) };
        Assert.Equal("/organizations", NotificationRows.For(unnamed).Single().Destination);
    }

    /// <summary>
    /// The badge is the sum of the rows, item by item.
    /// </summary>
    [Fact]
    public void The_total_is_the_sum_of_the_rows()
    {
        var summary = NotificationSummaryResponse.Empty with
        {
            InvestigationInvites = Waiting(2),
            FeedMentions         = Waiting(1),
        };

        var rows = NotificationRows.For(summary);
        Assert.Equal(rows.Sum(r => r.Bucket.Count), NotificationRows.TotalFor(summary));
        Assert.Equal(3, NotificationRows.TotalFor(summary));
    }

    /// <summary>
    /// Nothing waiting means no rows and no badge — not an empty list under a number.
    /// </summary>
    [Fact]
    public void Nothing_waiting_produces_no_rows_and_no_number()
    {
        Assert.Empty(NotificationRows.For(NotificationSummaryResponse.Empty));
        Assert.Equal(0, NotificationRows.TotalFor(NotificationSummaryResponse.Empty));
        Assert.Null(NotificationRows.OldestFor(NotificationSummaryResponse.Empty));
    }

    /// <summary>
    /// A per-case breakdown replaces its aggregate rather than joining it.
    /// </summary>
    /// <remarks>
    /// Item 173 added the slices beside the aggregate that already summed them. Rendering both
    /// would double every client message in the badge — the opposite of this class's job.
    /// </remarks>
    [Fact]
    public void Per_case_slices_replace_their_aggregate_instead_of_adding_to_it()
    {
        var orgId  = Guid.NewGuid();
        var caseId = Guid.NewGuid();

        var summary = NotificationSummaryResponse.Empty with
        {
            CaseMessagesAsOrgMember = Waiting(5),
            CaseMessagesAsOrgMemberByCase =
            [
                new CaseScopedBucket(caseId, orgId, "Bell Witch Cave", "Paranormal365", 3, Then),
                new CaseScopedBucket(Guid.NewGuid(), orgId, "Old Mill", "Paranormal365", 2, Then),
            ],
        };

        var rows = NotificationRows.For(summary);
        Assert.Equal(5, NotificationRows.TotalFor(summary));
        Assert.Equal(2, rows.Count(r => r.Title.StartsWith("Client messages awaiting a reply")));
        Assert.Contains(rows, r => r.Destination == $"/organizations/{orgId}/cases/{caseId}");
    }

    /// <summary>
    /// And with no slices, the aggregate still gets a row.
    /// </summary>
    /// <remarks>
    /// A payload from before item 173 carries the aggregate alone. Without this fallback the rows
    /// sum to less than the DTO's own total and the page reads as empty while the bell insists
    /// something is waiting — which is exactly the bug being fixed, reintroduced by the fix.
    /// </remarks>
    [Fact]
    public void An_aggregate_with_no_slices_still_gets_a_row()
    {
        var summary = NotificationSummaryResponse.Empty with
        {
            CaseMessagesAsOrgMember = Waiting(4),
            OrgMessages             = Waiting(2),
        };

        Assert.Equal(6, NotificationRows.TotalFor(summary));
        Assert.Equal(summary.TotalCount, NotificationRows.TotalFor(summary));
    }

    /// <summary>
    /// An unanswered RSVP reads first; a mention on a public post reads last.
    /// </summary>
    [Fact]
    public void Rows_are_ordered_by_what_waits_on_you()
    {
        var summary = NotificationSummaryResponse.Empty with
        {
            FeedMentions         = Waiting(9),
            InvestigationInvites = Waiting(1),
        };

        var rows = NotificationRows.For(summary);
        Assert.Equal("Investigation invitations", rows[0].Title);
        Assert.Equal("Mentions on the feed", rows[^1].Title);
    }

    /// <summary>Every row opens something, and says how many are behind it.</summary>
    [Fact]
    public void Every_row_carries_a_destination_and_a_count()
    {
        var summary = NotificationSummaryResponse.Empty with
        {
            EquipmentCheckouts        = Waiting(1),
            PendingPermissionRequests = Waiting(1),
            SystemMessages            = Waiting(1),
            CaseMessagesAsClient      = Waiting(1),
        };

        foreach (var row in NotificationRows.For(summary))
        {
            Assert.False(string.IsNullOrWhiteSpace(row.Destination));
            Assert.False(string.IsNullOrWhiteSpace(row.Title));
            Assert.False(string.IsNullOrWhiteSpace(row.Icon));
            Assert.True(row.Bucket.Count > 0);
        }
    }
}
