using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The demo events are moved on by whole weeks, and their program keeps its clock time
/// (10/22/2026: a month-old test database had both events in the past).
/// </summary>
public class DemoEventsStayAheadTests
{
    [Fact]
    public void A_passed_weekend_moves_on_by_whole_weeks_to_at_least_its_lead()
    {
        var friday = new DateTime(2026, 10, 16);
        var today = new DateTime(2026, 10, 22);

        var shift = HostedEventDemoSeeder.WeeksToMove(friday, today, leadDays: 30);
        var moved = friday + shift;

        Assert.Equal(0, shift.Days % 7);
        Assert.Equal(DayOfWeek.Friday, moved.DayOfWeek);
        Assert.True(moved >= today.AddDays(30), $"moved to {moved:d}, under 30 days ahead");
        Assert.True(moved < today.AddDays(37), $"moved to {moved:d}, more than a week past the lead");
    }

    [Fact]
    public void An_event_already_far_enough_ahead_still_moves_a_week_when_asked()
    {
        var shift = HostedEventDemoSeeder.WeeksToMove(new DateTime(2027, 1, 1), new DateTime(2026, 10, 22), 30);
        Assert.Equal(7, shift.Days);
    }

    [Fact]
    public void A_program_session_keeps_its_local_time_across_the_clock_change()
    {
        var chicago = HostedEventCalendarSync.ZoneOf("America/Chicago");
        // 9 PM on Friday 10/16/2026 in Chicago is 02:00 UTC on Saturday (daylight time).
        var nine = new DateTime(2026, 10, 17, 2, 0, 0, DateTimeKind.Utc);

        var moved = HostedEventDemoSeeder.MoveInZone(nine, TimeSpan.FromDays(35), chicago);

        // Five weeks on is 11/20, after the clock goes back: still 9 PM there, so 03:00 UTC.
        var local = TimeZoneInfo.ConvertTimeFromUtc(moved, chicago);
        Assert.Equal(new DateTime(2026, 11, 20, 21, 0, 0), local);
        Assert.Equal(new DateTime(2026, 11, 21, 3, 0, 0), moved);
    }
}
