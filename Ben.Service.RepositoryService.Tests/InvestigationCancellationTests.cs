using Ben.Data.Common.Helpers;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// Unit tests for InvestigationCancellationHelper — pure business logic,
/// no DB required. Tests the 24hr/72hr distance-based deadline rules.
/// </summary>
public class InvestigationCancellationTests
{
    // ── RequiredLeadHours ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0,   24.0)]    // same city
    [InlineData(74.9,  24.0)]    // just under 75 mi
    [InlineData(75.0,  24.0)]    // exactly 75 mi — boundary is >75
    [InlineData(75.01, 72.0)]    // just over 75 mi
    [InlineData(200.0, 72.0)]    // far away
    public void RequiredLeadHours_BasedOnDistance(double distMiles, double expected)
        => Assert.Equal(expected, InvestigationCancellationHelper.RequiredLeadHours(distMiles));

    // ── CancellationDeadlineUtc ───────────────────────────────────────────────

    [Fact]
    public void Deadline_Within75Miles_Is24HoursBefore()
    {
        var scheduled = new DateTime(2026, 9, 15, 19, 0, 0, DateTimeKind.Utc);
        var deadline  = InvestigationCancellationHelper.CancellationDeadlineUtc(scheduled, 50.0);
        Assert.Equal(scheduled.AddHours(-24), deadline);
    }

    [Fact]
    public void Deadline_Beyond75Miles_Is72HoursBefore()
    {
        var scheduled = new DateTime(2026, 9, 15, 19, 0, 0, DateTimeKind.Utc);
        var deadline  = InvestigationCancellationHelper.CancellationDeadlineUtc(scheduled, 100.0);
        Assert.Equal(scheduled.AddHours(-72), deadline);
    }

    // ── IsCancellationAllowed ─────────────────────────────────────────────────

    [Fact]
    public void Cancellation_Allowed_When25HoursBeforeCloseOrg()
    {
        var scheduled = DateTime.UtcNow.AddHours(25);
        Assert.True(InvestigationCancellationHelper.IsCancellationAllowed(scheduled, 50.0));
    }

    [Fact]
    public void Cancellation_Blocked_When23HoursBeforeCloseOrg()
    {
        var scheduled = DateTime.UtcNow.AddHours(23);
        Assert.False(InvestigationCancellationHelper.IsCancellationAllowed(scheduled, 50.0));
    }

    [Fact]
    public void Cancellation_Allowed_When73HoursBeforeFarOrg()
    {
        var scheduled = DateTime.UtcNow.AddHours(73);
        Assert.True(InvestigationCancellationHelper.IsCancellationAllowed(scheduled, 100.0));
    }

    [Fact]
    public void Cancellation_Blocked_When71HoursBeforeFarOrg()
    {
        var scheduled = DateTime.UtcNow.AddHours(71);
        Assert.False(InvestigationCancellationHelper.IsCancellationAllowed(scheduled, 100.0));
    }

    [Fact]
    public void Cancellation_Blocked_ForPastInvestigation()
    {
        var scheduled = DateTime.UtcNow.AddDays(-1);
        Assert.False(InvestigationCancellationHelper.IsCancellationAllowed(scheduled, 0.0));
    }

    [Fact]
    public void Cancellation_AllowsCustomNow_ForDeterministicTesting()
    {
        var scheduled = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
        var nowJustBefore = scheduled.AddHours(-25); // 25 hrs before → inside 24hr window → allowed
        var nowJustAfter  = scheduled.AddHours(-23); // 23 hrs before → deadline passed → blocked

        Assert.True (InvestigationCancellationHelper.IsCancellationAllowed(scheduled, 0.0, nowJustBefore));
        Assert.False(InvestigationCancellationHelper.IsCancellationAllowed(scheduled, 0.0, nowJustAfter));
    }

    // ── Constants ─────────────────────────────────────────────────────────────

    [Fact]
    public void Constants_AreCorrectValues()
    {
        Assert.Equal(75.0, InvestigationCancellationHelper.DistanceThresholdMiles);
        Assert.Equal(24.0, InvestigationCancellationHelper.ShortLeadHours);
        Assert.Equal(72.0, InvestigationCancellationHelper.LongLeadHours);
    }
}
