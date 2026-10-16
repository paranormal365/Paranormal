using Ben.Service.Models.Entities;
using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// What every countdown on the site says, and how full its bar is (10/16/2026).
/// </summary>
/// <remarks>
/// <para>"Tomorrow", "Tonight" and "Starts in 2 days" are calendar facts on the EVENT's clock, and
/// a calendar day is not always 24 hours: the clocks go back at 2 AM on Sunday 11/01/2026. These pin
/// the words, the moments they change, the bar, and which of somebody's events Home shows.</para>
///
/// <para>Each was run against code with the rule taken out before being relied on: the DST cases
/// against a version that counted days as 24-hour blocks back from the start, the other-zone case
/// against one that read "today" on the server's UTC clock.</para>
/// </remarks>
public sealed class CountdownMathTests
{
    private const string Chicago = "America/Chicago";
    private const string LosAngeles = "America/Los_Angeles";

    private static DateTime Utc(int y, int mo, int d, int h, int mi = 0, int s = 0)
        => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    private static CountdownStep Say(DateTime start, DateTime now, string? zone = Chicago, DateTime? end = null)
        => CountdownMath.Describe(start, end ?? start.AddHours(3), zone, now);

    // ── Before ──────────────────────────────────────────────────────────────

    [Fact]
    public void Two_days_out_it_says_how_many_days()
    {
        // Saturday 10/24/2026 7:00 PM CDT; now is Thursday noon CDT.
        var start = Utc(2026, 10, 25, 0);
        var step = Say(start, Utc(2026, 10, 22, 17));

        Assert.Equal(CountdownPhase.Later, step.Phase);
        Assert.Equal("Starts in 2 days", step.Words);
    }

    [Fact]
    public void Weeks_out_it_still_counts_calendar_days()
    {
        var start = Utc(2026, 11, 20, 1);          // Thursday 11/19 7:00 PM CST
        var step = Say(start, Utc(2026, 10, 30, 17)); // Friday 10/30 noon CDT

        Assert.Equal("Starts in 20 days", step.Words);
    }

    [Fact]
    public void The_day_before_an_evening_start_is_tomorrow_night()
    {
        var start = Utc(2026, 10, 25, 0);              // Sat 7:00 PM CDT
        var step = Say(start, Utc(2026, 10, 24, 3));   // Fri 10:00 PM CDT

        Assert.Equal(CountdownPhase.Tomorrow, step.Phase);
        Assert.Equal("Tomorrow night", step.Words);
    }

    // ── The day itself ──────────────────────────────────────────────────────

    [Fact]
    public void An_evening_start_today_is_tonight()
    {
        var start = Utc(2026, 10, 25, 0);              // Sat 7:00 PM CDT
        var step = Say(start, Utc(2026, 10, 24, 14));  // Sat 9:00 AM CDT

        Assert.Equal(CountdownPhase.Tonight, step.Phase);
        Assert.Equal("Tonight", step.Words);
    }

    [Fact]
    public void An_afternoon_start_today_is_today()
    {
        var start = Utc(2026, 10, 24, 19);             // Sat 2:00 PM CDT
        var step = Say(start, Utc(2026, 10, 24, 14));

        Assert.Equal(CountdownPhase.Today, step.Phase);
        Assert.Equal("Today", step.Words);
    }

    // ── Running, and after ──────────────────────────────────────────────────

    [Fact]
    public void Between_start_and_end_it_is_happening_now()
    {
        var start = Utc(2026, 10, 25, 0);
        var step = Say(start, start.AddMinutes(30), end: start.AddHours(3));

        Assert.Equal(CountdownPhase.Running, step.Phase);
        Assert.Equal("Happening now", step.Words);
    }

    [Fact]
    public void After_the_end_it_is_over()
    {
        var start = Utc(2026, 10, 25, 0);
        var step = Say(start, start.AddHours(3), end: start.AddHours(3));

        Assert.Equal(CountdownPhase.Over, step.Phase);
    }

    // ── Another zone ────────────────────────────────────────────────────────

    /// <summary>
    /// "Tonight" is tonight where it happens. At 1 AM in Chicago it is still 11 PM the night before
    /// in Los Angeles, so an 8 PM walk there is tomorrow night, whatever the server's clock says.
    /// </summary>
    [Fact]
    public void Days_are_counted_on_the_events_own_clock()
    {
        var start = Utc(2026, 10, 21, 3);              // Tue 10/20 8:00 PM PDT
        var now = Utc(2026, 10, 20, 6);                // Tue 1:00 AM CDT = Mon 11:00 PM PDT

        Assert.Equal("Tomorrow night", Say(start, now, LosAngeles).Words);
        Assert.Equal(1, CountdownMath.DaysAway(start, LosAngeles, now));
        // The same instant on Chicago's clock is already the day of it.
        Assert.Equal(0, CountdownMath.DaysAway(start, Chicago, now));
    }

    // ── Across the 11/01/2026 clock change ──────────────────────────────────

    /// <summary>
    /// Sunday 11/01/2026 7:00 PM CST is 25 hours after Saturday 7:00 PM CDT, because the clocks go
    /// back an hour overnight. It is still one calendar day away: tomorrow night, not "in 1 day" or
    /// "in 2 days".
    /// </summary>
    [Fact]
    public void A_25_hour_day_is_still_one_day()
    {
        var start = Utc(2026, 11, 2, 1);               // Sun 11/01 7:00 PM CST (UTC-6)
        var now = Utc(2026, 11, 1, 0);                 // Sat 10/31 7:00 PM CDT (UTC-5)

        Assert.Equal(TimeSpan.FromHours(25), start - now);
        Assert.Equal("Tomorrow night", Say(start, now).Words);
        Assert.Equal("1d 01h 00m", CountdownMath.Remaining(start - now));
    }

    /// <summary>
    /// The word changes at Chicago's midnight, which that night is 05:00 UTC (still daylight time);
    /// a day counted as 24 hours back from a CST start would put it at 06:00 and say "Tomorrow" for
    /// the first hour of the day itself.
    /// </summary>
    [Fact]
    public void The_day_starts_at_the_events_midnight_across_the_change()
    {
        var start = Utc(2026, 11, 2, 1);

        Assert.Equal("Tomorrow night", Say(start, Utc(2026, 11, 1, 4, 59)).Words);
        Assert.Equal("Tonight", Say(start, Utc(2026, 11, 1, 5, 0)).Words);

        var steps = CountdownMath.Steps(start, start.AddHours(3), Chicago, Utc(2026, 10, 30, 12));
        Assert.Contains(steps, s => s.FromUtc == Utc(2026, 11, 1, 5) && s.Phase == CountdownPhase.Tonight);
    }

    [Fact]
    public void Steps_are_in_order_and_end_with_the_start_and_the_end()
    {
        var start = Utc(2026, 11, 2, 1);
        var steps = CountdownMath.Steps(start, start.AddHours(3), Chicago, Utc(2026, 10, 1, 12));

        Assert.Equal(steps.OrderBy(s => s.FromUtc), steps);
        Assert.Equal((start, CountdownPhase.Running), (steps[^2].FromUtc, steps[^2].Phase));
        Assert.Equal((start.AddHours(3), CountdownPhase.Over), (steps[^1].FromUtc, steps[^1].Phase));
    }

    // ── The digits ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(2 * 86400 + 4 * 3600 + 12 * 60 + 7, true, "2d 04h 12m")]
    [InlineData(3 * 3600 + 5 * 60 + 59, true, "3h 05m")]
    [InlineData(9 * 60 + 5, true, "9m 05s")]       // seconds only in the last hour
    [InlineData(9 * 60 + 5, false, "10 min")]      // less motion: minutes, rounded up
    [InlineData(20, false, "1 min")]
    [InlineData(-5, true, "0m 00s")]
    public void The_time_left_reads_in_the_largest_units_that_matter(int seconds, bool withSeconds, string expected)
        => Assert.Equal(expected, CountdownMath.Remaining(TimeSpan.FromSeconds(seconds), withSeconds));

    [Fact]
    public void The_spoken_start_names_the_day_date_time_and_clock()
        => Assert.Equal("Starts Saturday, 10/24/2026 at 7:00 PM CDT",
            CountdownMath.Spoken(Utc(2026, 10, 25, 0), Utc(2026, 10, 25, 3), Chicago, Utc(2026, 10, 20, 0)));

    // ── The bar ─────────────────────────────────────────────────────────────

    [Fact]
    public void Before_the_baseline_the_bar_is_empty()
    {
        var start = Utc(2026, 10, 25, 0);
        var listed = Utc(2026, 10, 15, 0);
        Assert.Equal(0, CountdownMath.Progress(CountdownMath.Baseline(listed, start), start, Utc(2026, 10, 14, 0)));
    }

    [Fact]
    public void Halfway_through_the_wait_the_bar_is_half_full()
    {
        var start = Utc(2026, 10, 25, 0);
        var listed = Utc(2026, 10, 15, 0);
        Assert.Equal(.5, CountdownMath.Progress(CountdownMath.Baseline(listed, start), start, Utc(2026, 10, 20, 0)), 6);
    }

    [Fact]
    public void At_the_start_and_while_it_runs_the_bar_is_full()
    {
        var start = Utc(2026, 10, 25, 0);
        var baseline = CountdownMath.Baseline(Utc(2026, 10, 15, 0), start);
        Assert.Equal(1, CountdownMath.Progress(baseline, start, start));
        Assert.Equal(1, CountdownMath.Progress(baseline, start, start.AddHours(1)));
    }

    [Fact]
    public void After_it_ends_the_bar_stays_full()
    {
        var start = Utc(2026, 10, 25, 0);
        Assert.Equal(1, CountdownMath.Progress(CountdownMath.Baseline(null, start), start, start.AddDays(2)));
    }

    [Fact]
    public void With_no_listing_date_the_bar_covers_the_last_thirty_days()
    {
        var start = Utc(2026, 10, 25, 0);
        Assert.Equal(start - TimeSpan.FromDays(30), CountdownMath.Baseline(null, start));
    }

    [Fact]
    public void An_event_listed_minutes_before_it_starts_gets_an_hour_of_bar()
    {
        var start = Utc(2026, 10, 25, 0);
        Assert.Equal(start.AddHours(-1), CountdownMath.Baseline(start.AddMinutes(-10), start));
    }

    /// <summary>The bar measures real time, so the extra hour of 11/01/2026 is in it.</summary>
    [Fact]
    public void Across_the_clock_change_the_bar_measures_real_hours()
    {
        var listed = Utc(2026, 11, 1, 0);              // Sat 10/31 7:00 PM CDT
        var start = Utc(2026, 11, 2, 1);               // Sun 11/01 7:00 PM CST: 25 hours later
        var now = Utc(2026, 11, 1, 7, 30);             // 7.5 hours in

        Assert.Equal(7.5 / 25, CountdownMath.Progress(CountdownMath.Baseline(listed, start), start, now), 6);
    }

    // ── Which of somebody's events Home shows ───────────────────────────────

    private static MyNextEventItem Item(string title, DateTime start, DateTime? end = null) => new(
        MyNextEventKind.Event, Guid.NewGuid(), title, "Ghost Squad", start, end ?? start.AddHours(3),
        Chicago, "/o/ghost-squad/events/x", MyNextEventStanding.Going, null);

    [Fact]
    public void With_nothing_coming_up_there_is_no_card()
    {
        Assert.Null(CountdownMath.PickNext([], 0, Utc(2026, 10, 20, 0)));
        Assert.Null(CountdownMath.PickNext([Item("Last week", Utc(2026, 10, 10, 0))], 1, Utc(2026, 10, 20, 0)));
    }

    [Fact]
    public void With_several_it_shows_the_soonest_and_counts_the_rest()
    {
        var now = Utc(2026, 10, 20, 0);
        var pick = CountdownMath.PickNext(
            [Item("Later", Utc(2026, 11, 5, 0)), Item("Soonest", Utc(2026, 10, 22, 0)), Item("Middle", Utc(2026, 10, 30, 0))],
            3, now);

        Assert.Equal("Soonest", pick!.Value.Next.Title);
        Assert.Equal(2, pick.Value.More);
    }

    [Fact]
    public void The_total_counts_ones_beyond_the_handful_sent()
        => Assert.Equal(11, CountdownMath.PickNext([Item("A", Utc(2026, 10, 22, 0))], 12, Utc(2026, 10, 20, 0))!.Value.More);

    [Fact]
    public void One_running_now_is_shown_before_one_still_to_come()
    {
        var now = Utc(2026, 10, 24, 1);
        var pick = CountdownMath.PickNext(
            [Item("Tomorrow", Utc(2026, 10, 25, 0)), Item("Under way", Utc(2026, 10, 24, 0), Utc(2026, 10, 24, 4))],
            2, now);

        Assert.Equal("Under way", pick!.Value.Next.Title);
    }

    [Fact]
    public void When_the_shown_one_ends_the_card_moves_on()
    {
        var items = new[] { Item("First", Utc(2026, 10, 24, 0), Utc(2026, 10, 24, 3)), Item("Second", Utc(2026, 10, 30, 0)) };

        var during = CountdownMath.PickNext(items, 2, Utc(2026, 10, 24, 2))!.Value;
        var after = CountdownMath.PickNext(items, 2, Utc(2026, 10, 24, 3))!.Value;

        Assert.Equal(("First", 1), (during.Next.Title, during.More));
        Assert.Equal(("Second", 0), (after.Next.Title, after.More));
    }
}
