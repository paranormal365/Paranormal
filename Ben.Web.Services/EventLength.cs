namespace Ben.Web.Services;

/// <summary>
/// How long an event runs, said the way a person would say it: "4 hr", "2 hr 30 min",
/// "15 nights", "2 weeks", "15 nights, 6:00–11:00 PM".
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> The public event page's "How long" tile counted every event in
/// hours, so a fifteen-day event read "352 hr" and a hosted run "341 hr over 15 nights"
/// (production, 10/16/2026). Nobody plans a fortnight in hours. Up to a day the hours are the
/// useful figure; past a day it is how many nights or days, and past a few weeks, roughly how many
/// weeks.</para>
///
/// <para><b>Nights or days.</b> Counted on the event's own clock, never the reader's or the
/// server's, because a night is a date on a calendar at the venue. An event that ends earlier in
/// the day than it started (Friday 7 PM to Sunday 2 AM, a hotel weekend from 3 PM Friday to 11 AM
/// Sunday) runs over nights, and the count is the dates it crosses. One that ends later in the day
/// than it started (Monday 9 AM to Friday 5 PM) runs over days, and the count is the dates it
/// touches.</para>
///
/// <para><b>Hours are real hours.</b> The short form is the time that actually passes, so a night
/// from 10 PM to 2 AM over the November clock change is "5 hr", which is how long somebody is
/// there. Whether something is short enough to count in hours is judged on the clock on the wall:
/// 6 PM to 6 PM the next day is a day long however the clocks moved.</para>
/// </remarks>
public static class EventLength
{
    /// <summary>A length in minutes as a figure for a fact tile: "45 min", "4 hr", "2 hr 30 min".</summary>
    public static string Hours(int minutes)
    {
        var hours = minutes / 60;
        var rest = minutes % 60;
        return hours switch
        {
            0 => $"{rest} min",
            _ when rest == 0 => $"{hours} hr",
            _ => $"{hours} hr {rest} min",
        };
    }

    /// <summary>
    /// How long an event runs from its start to its end, on the clock of the place it happens.
    /// Null when it has no length to state (the end is not after the start).
    /// </summary>
    /// <param name="startUtc">The start, in UTC as stored.</param>
    /// <param name="endUtc">The end, in UTC as stored.</param>
    /// <param name="ianaZoneId">The event's zone; the house zone when it has none (see <see cref="EventClock"/>).</param>
    public static string? Between(DateTime startUtc, DateTime endUtc, string? ianaZoneId)
    {
        var elapsed = endUtc - startUtc;
        if (elapsed <= TimeSpan.Zero) return null;

        var start = EventClock.At(startUtc, ianaZoneId);
        var end = EventClock.At(endUtc, ianaZoneId);

        if (end - start <= TimeSpan.FromHours(24))
            return Hours((int)Math.Round(elapsed.TotalMinutes));

        var datesApart = (end.Date - start.Date).Days;
        return end.TimeOfDay <= start.TimeOfDay
            ? Count(datesApart, "night")
            : Count(datesApart + 1, "day");
    }

    /// <summary>
    /// A hosted event's run of dates: "15 nights, 6:00–11:00 PM" when every one keeps the same
    /// hours, and "15 nights" when they differ or are not given.
    /// </summary>
    /// <remarks>
    /// The count is the dates themselves, never rounded to weeks: a run is booked by the date, and
    /// "6 dates" may be six Saturdays rather than a week of evenings.
    /// </remarks>
    /// <param name="noun">The event's own word for one date: "night" or "date".</param>
    /// <param name="nightly">Each date's start and end on the venue's clock.</param>
    public static string Run(string noun, IReadOnlyList<(TimeSpan? Starts, TimeSpan? Ends)> nightly)
    {
        var count = nightly.Count;
        var dates = $"{count} {(count == 1 ? noun : noun + "s")}";

        if (count == 0 || nightly[0].Starts is not { } from) return dates;
        if (nightly.Any(n => n.Starts != from || n.Ends != nightly[0].Ends)) return dates;

        return $"{dates}, {Clock(from, nightly[0].Ends)}";
    }

    /// <summary>
    /// A time or a span of times of day: "6:00 PM", "6:00–11:00 PM", "9:00 PM–1:00 AM". The AM or
    /// PM is said once when both ends share it.
    /// </summary>
    public static string Clock(TimeSpan from, TimeSpan? to)
    {
        var start = OnTheClock(from);
        if (to is not { } until) return Say(start, "h:mm tt");

        var end = OnTheClock(until);
        return Say(start, "tt") == Say(end, "tt")
            ? $"{Say(start, "h:mm")}–{Say(end, "h:mm tt")}"
            : $"{Say(start, "h:mm tt")}–{Say(end, "h:mm tt")}";
    }

    /// <summary>AM and PM whatever culture the server happens to run in.</summary>
    private static string Say(DateTime at, string format)
        => at.ToString(format, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A time of day as a DateTime to format, with no calendar date involved.</summary>
    private static DateTime OnTheClock(TimeSpan timeOfDay)
        => DateTime.MinValue.AddTicks(((timeOfDay.Ticks % TimeSpan.TicksPerDay) + TimeSpan.TicksPerDay) % TimeSpan.TicksPerDay);

    /// <summary>"3 nights", "1 week", "about 4 weeks", "about 3 months".</summary>
    private static string Count(int count, string noun)
    {
        if (count >= 60) return $"about {(int)Math.Round(count / 30.0)} months";
        if (count % 7 == 0) return count == 7 ? "1 week" : $"{count / 7} weeks";
        if (count > 21) return $"about {(int)Math.Round(count / 7.0)} weeks";
        return $"{count} {(count == 1 ? noun : noun + "s")}";
    }
}
