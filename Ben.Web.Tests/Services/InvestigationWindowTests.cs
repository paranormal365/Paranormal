using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Ben's rule, 2026-09-09: crossing midnight is still one day. Three screens depend on this
/// agreeing with itself, so it is tested here rather than in any of them.
/// </summary>
public class InvestigationWindowTests
{
    [Fact]
    public void SingleDayEnd_LaterThanTheStart_StaysOnTheSameDay()
    {
        var start = new DateTime(2026, 9, 14, 19, 0, 0);

        var end = InvestigationWindow.SingleDayEnd(start, new TimeSpan(23, 30, 0));

        Assert.Equal(new DateTime(2026, 9, 14, 23, 30, 0), end);
    }

    [Fact]
    public void SingleDayEnd_EarlierThanTheStart_IsTheNextMorning()
    {
        // Arrive 3pm, leave 8am. One investigation.
        var start = new DateTime(2026, 9, 14, 15, 0, 0);

        var end = InvestigationWindow.SingleDayEnd(start, new TimeSpan(8, 0, 0));

        Assert.Equal(new DateTime(2026, 9, 15, 8, 0, 0), end);
    }

    [Fact]
    public void SingleDayEnd_TheSameTimeAsTheStart_IsAFullDayLater()
    {
        // Not zero length: a window that ends the instant it opens is nobody's intention.
        var start = new DateTime(2026, 9, 14, 15, 0, 0);

        var end = InvestigationWindow.SingleDayEnd(start, new TimeSpan(15, 0, 0));

        Assert.Equal(new DateTime(2026, 9, 15, 15, 0, 0), end);
    }

    [Fact]
    public void NeedsMultiDay_NoEnd_IsFalse()
        => Assert.False(InvestigationWindow.NeedsMultiDay(new DateTime(2026, 9, 14, 15, 0, 0), null));

    [Fact]
    public void NeedsMultiDay_Overnight_IsFalse()
    {
        var start = new DateTime(2026, 9, 14, 15, 0, 0);
        var end   = new DateTime(2026, 9, 15, 8, 0, 0);

        Assert.False(InvestigationWindow.NeedsMultiDay(start, end));
    }

    [Fact]
    public void NeedsMultiDay_TheFollowingNight_IsTrue()
    {
        // One calendar day apart, like the overnight above, and yet the single-day rule cannot
        // produce it: 11pm is later than 3pm, so it would land on the first evening.
        var start = new DateTime(2026, 9, 14, 15, 0, 0);
        var end   = new DateTime(2026, 9, 15, 23, 0, 0);

        Assert.True(InvestigationWindow.NeedsMultiDay(start, end));
    }

    [Fact]
    public void NeedsMultiDay_AWeek_IsTrue()
    {
        var start = new DateTime(2026, 9, 14, 15, 0, 0);

        Assert.True(InvestigationWindow.NeedsMultiDay(start, start.AddDays(7)));
    }
}
