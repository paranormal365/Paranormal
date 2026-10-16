using Ben.Web.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The public event page's "How long", said the way a person would (10/16/2026: a fifteen-day
/// event read "352 hr").
/// </summary>
/// <remarks>
/// Every instant is written as the venue's wall clock and converted here, so each case reads as
/// the event somebody would actually advertise. America/Chicago throughout; the clocks there go
/// back an hour at 2:00 AM on 11/1/2026.
/// </remarks>
public sealed class EventLengthTests
{
    private const string Chicago = "America/Chicago";

    /// <summary>A wall-clock time in Chicago, as the UTC instant the database would hold.</summary>
    private static DateTime At(int month, int day, int hour, int minute = 0)
        => TimeZoneInfo.ConvertTimeToUtc(
            new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Unspecified),
            EventClock.ZoneOf(Chicago));

    private static string? Length(DateTime startUtc, DateTime endUtc) => EventLength.Between(startUtc, endUtc, Chicago);

    // ── short: hours ─────────────────────────────────────────────────────────

    [Fact]
    public void An_evening_is_hours() => Assert.Equal("4 hr", Length(At(10, 20, 19), At(10, 20, 23)));

    [Fact]
    public void Part_hours_say_the_minutes() => Assert.Equal("2 hr 30 min", Length(At(10, 20, 19), At(10, 20, 21, 30)));

    [Fact]
    public void Under_an_hour_is_minutes() => Assert.Equal("45 min", Length(At(10, 20, 19), At(10, 20, 19, 45)));

    [Fact]
    public void Overnight_is_still_hours() => Assert.Equal("6 hr", Length(At(10, 20, 21), At(10, 21, 3)));

    [Fact]
    public void Exactly_a_day_is_24_hours() => Assert.Equal("24 hr", Length(At(10, 20, 18), At(10, 21, 18)));

    // ── long: nights, days, weeks ────────────────────────────────────────────

    [Fact]
    public void A_weekend_that_ends_in_the_small_hours_is_nights()
        => Assert.Equal("2 nights", Length(At(10, 23, 19), At(10, 25, 2)));

    [Fact]
    public void A_hotel_weekend_from_check_in_to_check_out_is_nights()
        => Assert.Equal("2 nights", Length(At(10, 23, 15), At(10, 25, 11)));

    [Fact]
    public void A_run_of_daytimes_is_days()
        => Assert.Equal("5 days", Length(At(10, 19, 9), At(10, 23, 17)));

    [Fact]
    public void The_fifteen_day_event_that_read_352_hours_reads_15_nights()
    {
        // 10/17 6 PM to 11/1 10 AM: 353 real hours, because the clocks went back on the way.
        Assert.Equal(353.0, (At(11, 1, 10) - At(10, 17, 18)).TotalHours);
        Assert.Equal("15 nights", Length(At(10, 17, 18), At(11, 1, 10)));
    }

    [Fact]
    public void A_whole_number_of_weeks_is_weeks()
    {
        Assert.Equal("1 week", Length(At(10, 3, 18), At(10, 10, 10)));
        Assert.Equal("2 weeks", Length(At(10, 3, 18), At(10, 17, 10)));
    }

    [Fact]
    public void Past_three_weeks_it_is_roughly_weeks_then_months()
    {
        Assert.Equal("about 4 weeks", Length(At(9, 1, 9), At(9, 25, 17)));     // 25 days
        Assert.Equal("about 3 months", Length(At(8, 1, 9), At(10, 29, 17)));   // 90 days
    }

    // ── the clock change, 11/1/2026 ──────────────────────────────────────────

    [Fact]
    public void A_night_across_the_fall_back_counts_the_hour_that_really_passes()
        // 10 PM to 2 AM on the wall, but 1:00–2:00 AM happens twice: five hours there.
        => Assert.Equal("5 hr", Length(At(10, 31, 22), At(11, 1, 2)));

    [Fact]
    public void Six_to_six_across_the_fall_back_is_still_a_day_long_and_says_its_real_hours()
        => Assert.Equal("25 hr", Length(At(10, 31, 18), At(11, 1, 18)));

    [Fact]
    public void A_run_across_the_fall_back_counts_its_dates_not_its_hours()
        => Assert.Equal("3 nights", Length(At(10, 30, 19), At(11, 2, 1)));

    // ── nothing to say ───────────────────────────────────────────────────────

    [Fact]
    public void An_event_that_ends_when_it_starts_has_no_length()
    {
        Assert.Null(Length(At(10, 20, 19), At(10, 20, 19)));
        Assert.Null(Length(At(10, 20, 19), At(10, 20, 18)));
    }

    [Fact]
    public void An_event_with_no_zone_is_counted_on_the_house_clock()
        => Assert.Equal("2 nights", EventLength.Between(At(10, 23, 19), At(10, 25, 2), null));

    // ── a hosted event's run of dates ────────────────────────────────────────

    private static readonly TimeSpan SixPm = new(18, 0, 0), ElevenPm = new(23, 0, 0);

    [Fact]
    public void A_run_with_the_same_hours_every_night_says_them()
        => Assert.Equal("15 nights, 6:00–11:00 PM",
            EventLength.Run("night", [.. Enumerable.Repeat<(TimeSpan?, TimeSpan?)>((SixPm, ElevenPm), 15)]));

    [Fact]
    public void A_run_whose_hours_differ_says_only_how_many()
        => Assert.Equal("3 nights",
            EventLength.Run("night", [(SixPm, ElevenPm), (SixPm, ElevenPm), (new TimeSpan(20, 0, 0), ElevenPm)]));

    [Fact]
    public void A_run_with_no_hours_says_only_how_many()
        => Assert.Equal("6 dates", EventLength.Run("date", [.. Enumerable.Repeat<(TimeSpan?, TimeSpan?)>((null, null), 6)]));

    [Fact]
    public void Nights_past_midnight_say_both_halves_of_the_clock()
        => Assert.Equal("2 nights, 9:00 PM–1:00 AM",
            EventLength.Run("night", [(new TimeSpan(21, 0, 0), new TimeSpan(1, 0, 0)), (new TimeSpan(21, 0, 0), new TimeSpan(1, 0, 0))]));

    [Fact]
    public void A_start_with_no_end_is_just_the_start()
        => Assert.Equal("2 dates, 7:30 PM",
            EventLength.Run("date", [(new TimeSpan(19, 30, 0), null), (new TimeSpan(19, 30, 0), null)]));

    [Fact]
    public void A_run_is_never_rounded_to_weeks()
        // Fourteen Saturdays are not two weeks.
        => Assert.Equal("14 dates", EventLength.Run("date", [.. Enumerable.Repeat<(TimeSpan?, TimeSpan?)>((null, null), 14)]));
}
