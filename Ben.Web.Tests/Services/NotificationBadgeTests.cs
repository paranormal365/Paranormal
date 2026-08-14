using Ben.Service.Models.Entities;
using Ben.Web.Library.Services;
using Xunit;
using static Ben.Web.Library.Services.NotificationBadge;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Pins the badge thresholds. The bell and the drawer both read them, so a drift here would show up
/// as two badges disagreeing about the same messages.
/// </summary>
public class NotificationBadgeTests
{
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    private static NotificationBucket Aged(int count, TimeSpan age) =>
        new(count, Now - age);

    // ── Classification ───────────────────────────────────────────────────────

    [Fact]
    public void EmptyBucket_IsNone_EvenWithAnAncientTimestamp()
    {
        Assert.Equal(Urgency.None, Classify(new NotificationBucket(0, Now.AddYears(-1)), Now));
    }

    [Fact]
    public void BucketWithNoTimestamp_IsNone()
    {
        // Count without a timestamp shouldn't be coloured as though it just arrived.
        Assert.Equal(Urgency.None, Classify(new NotificationBucket(5, null), Now));
    }

    [Theory]
    [InlineData(0, Urgency.Fresh)]     // arrived this instant
    [InlineData(23, Urgency.Fresh)]
    [InlineData(24, Urgency.Aging)]    // exactly one day is already aging
    [InlineData(71, Urgency.Aging)]
    [InlineData(72, Urgency.Overdue)]  // exactly three days is already overdue
    [InlineData(240, Urgency.Overdue)]
    public void UrgencyFollowsTheAgeOfTheOldestItem(int ageHours, Urgency expected)
    {
        Assert.Equal(expected, Classify(Aged(1, TimeSpan.FromHours(ageHours)), Now));
    }

    [Fact]
    public void ASingleOldItemOutranksManyNewOnes()
    {
        // The whole reason colour tracks age rather than count.
        var many   = Aged(50, TimeSpan.FromMinutes(5));
        var oneOld = Aged(1,  TimeSpan.FromDays(5));

        Assert.Equal(Urgency.Fresh,   Classify(many,   Now));
        Assert.Equal(Urgency.Overdue, Classify(oneOld, Now));
    }

    // ── Roll-up across buckets ───────────────────────────────────────────────

    [Fact]
    public void SummaryTakesTheOldestItemAcrossEveryBucket()
    {
        var summary = new NotificationSummaryResponse(
            OrgMessages:               Aged(2, TimeSpan.FromMinutes(10)),
            CaseMessagesAsOrgMember:   NotificationBucket.Empty,
            CaseMessagesAsClient:      Aged(1, TimeSpan.FromDays(4)),
            SystemMessages:            NotificationBucket.Empty,
            PendingPermissionRequests: Aged(3, TimeSpan.FromHours(2)));

        Assert.Equal(6, summary.TotalCount);
        Assert.Equal(Urgency.Overdue, Classify(summary, Now));
    }

    [Fact]
    public void EmptySummaryIsNone()
    {
        Assert.Equal(Urgency.None, Classify(NotificationSummaryResponse.Empty, Now));
        Assert.Null(NotificationSummaryResponse.Empty.OldestUnreadUtc);
    }

    // ── Presentation ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1, "1")]
    [InlineData(99, "99")]
    [InlineData(100, "99+")]
    [InlineData(4000, "99+")]
    public void BadgeTextIsCappedSoThePillStaysSmall(int count, string expected)
    {
        Assert.Equal(expected, Text(count));
    }

    [Fact]
    public void EachUrgencyGetsADistinctBadgeClass()
    {
        var classes = new[] { Urgency.None, Urgency.Fresh, Urgency.Aging, Urgency.Overdue }
            .Select(CssClass).ToArray();

        Assert.Equal(classes.Length, classes.Distinct().Count());
        Assert.All(classes, c => Assert.StartsWith("badge rounded-pill ", c));
    }

    // "ago" belongs to the helper, not the call site — otherwise a caller appending it composes
    // "just now ago", which is exactly what shipped to the page the first time.
    [Theory]
    [InlineData(30, "just now")]                 // seconds
    [InlineData(60 * 5, "5 min ago")]
    [InlineData(60 * 60 * 3, "3 hours ago")]
    [InlineData(60 * 60 * 24, "1 day ago")]      // singular
    [InlineData(60 * 60 * 24 * 9, "9 days ago")]
    public void AgeIsDescribedInPlainLanguage(int ageSeconds, string expected)
    {
        Assert.Equal(expected, DescribeAge(Now.AddSeconds(-ageSeconds), Now));
    }

    [Fact]
    public void AgeOfNothingIsBlank()
    {
        Assert.Equal(string.Empty, DescribeAge(null, Now));
    }
}
