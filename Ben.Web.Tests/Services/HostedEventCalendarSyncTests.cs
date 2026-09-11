using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A hosted event's umbrella calendar row spans the whole event, in the event's own time.
/// </summary>
/// <remarks>
/// <para>The umbrella is what lets every existing part of the site — the public list, the
/// twenty-four-hour reminder, the calendar file, the share tags, and the app already in people's
/// pockets — treat a three-night weekend as an ordinary public event without knowing what it is. If
/// the span is wrong, all of them are wrong together and none of them says so.</para>
///
/// <para>The arithmetic is pure and tested without a database, because the thing worth pinning is
/// the timekeeping, not the saving.</para>
/// </remarks>
public sealed class HostedEventCalendarSyncTests
{
    private static readonly TimeZoneInfo Central =
        TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    private static HostedEvent Weekend(
        string zone = "America/Chicago", TimeSpan? start = null, TimeSpan? end = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = "Thomas House Weekend",
            TimeZoneId = zone,
            StartsOn = new DateTime(2026, 10, 30),
            EndsOn = new DateTime(2026, 11, 1),
            DefaultStartLocal = start,
            DefaultEndLocal = end,
        };

    private static HostedEventNight Night(DateTime date, TimeSpan? from = null, TimeSpan? to = null)
        => new() { Id = Guid.NewGuid(), Date = date, StartLocal = from, EndLocal = to };

    [Fact]
    public void The_span_runs_from_the_first_date_to_the_last()
    {
        var e = Weekend(start: new TimeSpan(19, 0, 0), end: new TimeSpan(23, 30, 0));
        var nights = new[]
        {
            Night(new DateTime(2026, 10, 30)),
            Night(new DateTime(2026, 10, 31)),
            Night(new DateTime(2026, 11, 1)),
        };

        var (startUtc, endUtc) = HostedEventCalendarSync.Window(e, nights);

        // 7:00 PM Central on the 30th, and 11:30 PM Central on the 1st — the whole weekend as one
        // thing, not three.
        Assert.Equal(new DateTime(2026, 10, 30, 19, 0, 0),
            TimeZoneInfo.ConvertTimeFromUtc(startUtc, Central));
        Assert.Equal(new DateTime(2026, 11, 1, 23, 30, 0),
            TimeZoneInfo.ConvertTimeFromUtc(endUtc, Central));
    }

    [Fact]
    public void A_night_that_states_its_own_times_beats_the_events_defaults()
    {
        // An opening night that starts early is the case this exists for: the span has to follow
        // what actually happens, not what the event usually does.
        var e = Weekend(start: new TimeSpan(19, 0, 0), end: new TimeSpan(23, 0, 0));
        var nights = new[]
        {
            Night(new DateTime(2026, 10, 30), from: new TimeSpan(16, 30, 0)),
            Night(new DateTime(2026, 11, 1), to: new TimeSpan(2, 0, 0)),
        };

        var (startUtc, endUtc) = HostedEventCalendarSync.Window(e, nights);

        Assert.Equal(new DateTime(2026, 10, 30, 16, 30, 0),
            TimeZoneInfo.ConvertTimeFromUtc(startUtc, Central));
        Assert.Equal(new DateTime(2026, 11, 1, 2, 0, 0),
            TimeZoneInfo.ConvertTimeFromUtc(endUtc, Central));
    }

    [Fact]
    public void The_clocks_going_back_mid_event_does_not_shorten_it()
    {
        // Central time falls back at 2am on 1 November 2026, inside this very weekend. The span is
        // computed in the event's zone and converted, so the extra hour is simply there — which is
        // the whole argument for storing a zone rather than an offset.
        var e = Weekend(start: new TimeSpan(19, 0, 0), end: new TimeSpan(23, 0, 0));
        var nights = new[] { Night(new DateTime(2026, 10, 30)), Night(new DateTime(2026, 11, 1)) };

        var (startUtc, endUtc) = HostedEventCalendarSync.Window(e, nights);

        // Read off a wall clock, 30 Oct 19:00 to 1 Nov 23:00 is 52 hours. Actually lived through,
        // it is 53: the hour between one and two in the morning on the Sunday happens twice. The
        // span carries the extra hour because it is computed in the event's zone and converted,
        // and an event that told its guests it was 52 hours long would be wrong by exactly the
        // hour they were all asleep for.
        var naive = (new DateTime(2026, 11, 1, 23, 0, 0) - new DateTime(2026, 10, 30, 19, 0, 0)).TotalHours;
        Assert.Equal(52, naive, 3);
        Assert.Equal(53, (endUtc - startUtc).TotalHours, 3);
    }

    [Fact]
    public void A_run_of_separate_dates_spans_the_whole_run()
    {
        // The resident play company: one production, a date a month. The umbrella says "on until
        // November", which is right for a listing — each date takes its own bookings.
        var production = new HostedEvent
        {
            Name = "Murder at the Manor",
            TimeZoneId = "America/Chicago",
            DatesAreSeparate = true,
            StartsOn = new DateTime(2026, 9, 12),
            EndsOn = new DateTime(2026, 11, 14),
            DefaultStartLocal = new TimeSpan(18, 30, 0),
            DefaultEndLocal = new TimeSpan(21, 30, 0),
        };
        var dates = new[]
        {
            Night(new DateTime(2026, 9, 12)),
            Night(new DateTime(2026, 10, 10)),
            Night(new DateTime(2026, 11, 14)),
        };

        var (startUtc, endUtc) = HostedEventCalendarSync.Window(production, dates);

        Assert.Equal(new DateTime(2026, 9, 12, 18, 30, 0),
            TimeZoneInfo.ConvertTimeFromUtc(startUtc, Central));
        Assert.Equal(new DateTime(2026, 11, 14, 21, 30, 0),
            TimeZoneInfo.ConvertTimeFromUtc(endUtc, Central));
    }

    [Fact]
    public void An_event_with_no_dates_yet_still_has_an_honest_span()
    {
        // A draft being built has no nights for a moment. It must still produce a row a reader
        // could look at, rather than a span of zero or one that ends before it starts.
        var e = Weekend();
        var (startUtc, endUtc) = HostedEventCalendarSync.Window(e, []);

        Assert.True(endUtc > startUtc);
        Assert.Equal(new DateTime(2026, 10, 30), TimeZoneInfo.ConvertTimeFromUtc(startUtc, Central).Date);
        Assert.Equal(new DateTime(2026, 11, 1), TimeZoneInfo.ConvertTimeFromUtc(endUtc, Central).Date);
    }

    [Fact]
    public void An_end_before_its_start_is_pushed_out_rather_than_shown()
    {
        // A one-day event whose end time was typed earlier than its start. Somebody will do this,
        // and a negative span renders as nonsense everywhere it is read.
        var e = new HostedEvent
        {
            Name = "One evening",
            TimeZoneId = "America/Chicago",
            StartsOn = new DateTime(2026, 10, 30),
            EndsOn = new DateTime(2026, 10, 30),
            DefaultStartLocal = new TimeSpan(20, 0, 0),
            DefaultEndLocal = new TimeSpan(9, 0, 0),
        };

        var (startUtc, endUtc) = HostedEventCalendarSync.Window(e, [Night(new DateTime(2026, 10, 30))]);

        Assert.True(endUtc > startUtc);
    }

    [Fact]
    public void A_zone_this_machine_has_never_heard_of_falls_back_rather_than_throwing()
    {
        // A zone database that disagrees with the one a row was written on must not take a page
        // down. The times are then out by an offset, which somebody can see and fix.
        var e = Weekend(zone: "Mars/Olympus_Mons");
        var (startUtc, endUtc) = HostedEventCalendarSync.Window(e, []);

        Assert.True(endUtc > startUtc);
        Assert.Equal(TimeZoneInfo.Utc, HostedEventCalendarSync.ZoneOf("Mars/Olympus_Mons"));
    }

    [Fact]
    public void A_stay_gets_one_date_per_day_and_a_run_gets_none()
    {
        var stay = HostedEventCalendarSync
            .DatesOfAStay(new DateTime(2026, 10, 30), new DateTime(2026, 11, 1)).ToList();

        Assert.Equal(3, stay.Count);
        Assert.Equal(new DateTime(2026, 10, 31), stay[1]);

        // A run's dates are chosen one at a time. Generating every day between September and
        // November for a monthly show would be a wrong answer delivered quickly.
        var single = HostedEventCalendarSync
            .DatesOfAStay(new DateTime(2026, 10, 30), new DateTime(2026, 10, 30)).ToList();
        Assert.Single(single);
    }
}
