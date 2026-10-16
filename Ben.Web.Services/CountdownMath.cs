using Ben.Service.Models.Entities;

namespace Ben.Web.Services;

/// <summary>Where an event stands, as a countdown says it.</summary>
public enum CountdownPhase
{
    /// <summary>Two or more days away, counted in calendar days on the event's own clock.</summary>
    Later,

    /// <summary>The day before, on the event's own clock.</summary>
    Tomorrow,

    /// <summary>Today, starting before the evening.</summary>
    Today,

    /// <summary>Today, starting in the evening.</summary>
    Tonight,

    /// <summary>Started and not yet ended.</summary>
    Running,

    /// <summary>Ended.</summary>
    Over,
}

/// <summary>
/// From this moment on, the countdown reads <paramref name="Words"/> in <paramref name="Phase"/>.
/// </summary>
public sealed record CountdownStep(DateTime FromUtc, CountdownPhase Phase, string Words);

/// <summary>
/// The arithmetic behind every countdown on the site: the home card and the event lists.
/// </summary>
/// <remarks>
/// <para><b>All the clock work is done here, once, in C#.</b> "Tomorrow", "Tonight" and "Starts in 2
/// days" are calendar facts on the event's own clock (stored UTC, read in the event's zone — Ben's
/// rule), and a day is not always 24 hours: the clocks go back on 11/01/2026. So the server works out
/// the exact UTC moments at which the words change (<see cref="Steps"/>), and the browser's ticker
/// (wwwroot/js/eventCountdown.js) only compares those moments with its own clock. It never has to
/// know a time zone, and the two can never disagree about what day it is.</para>
///
/// <para>Never reads the server's local clock: every method takes the "now" it is asked about.</para>
/// </remarks>
public static class CountdownMath
{
    /// <summary>From this time of day on the event's clock, "Today" reads "Tonight".</summary>
    public static readonly TimeSpan EveningStarts = TimeSpan.FromHours(17);

    /// <summary>
    /// How many days before the start a step is written for. Beyond this the words are worked out
    /// when the page is drawn ("Starts in 20 days") and only go stale on a page left open overnight.
    /// </summary>
    public const int DaysStepped = 14;

    // ── The progress bar's starting point (Ben may want to change this) ───────────

    /// <summary>The bar's window when nothing says when the event was listed.</summary>
    public static readonly TimeSpan UnlistedWindow = TimeSpan.FromDays(30);

    /// <summary>The shortest bar: an event listed ten minutes before it starts is not shown as full.</summary>
    public static readonly TimeSpan ShortestWindow = TimeSpan.FromHours(1);

    /// <summary>
    /// Where the progress bar starts: when the event was listed (or the person signed up), so the bar
    /// says how much of the wait has gone.
    /// </summary>
    /// <remarks>
    /// THE ONE PLACE THE BASELINE IS DECIDED. The honest reading of a bar that fills toward the start
    /// is "how much of the wait since it was announced is behind you", so that is the default; with
    /// no listing date the bar covers the last <see cref="UnlistedWindow"/>. To make the bar a fixed
    /// window instead (say the last seven days), return <c>startUtc - TimeSpan.FromDays(7)</c> here.
    /// </remarks>
    public static DateTime Baseline(DateTime? listedUtc, DateTime startUtc)
    {
        var baseline = listedUtc ?? startUtc - UnlistedWindow;
        var latest = startUtc - ShortestWindow;
        return baseline > latest ? latest : baseline;
    }

    /// <summary>How full the bar is, 0 to 1: empty at the baseline, full at the start and while it runs.</summary>
    public static double Progress(DateTime baselineUtc, DateTime startUtc, DateTime nowUtc)
    {
        if (nowUtc >= startUtc) return 1;
        if (nowUtc <= baselineUtc) return 0;
        var whole = (startUtc - baselineUtc).TotalMilliseconds;
        return whole <= 0 ? 1 : Math.Clamp((nowUtc - baselineUtc).TotalMilliseconds / whole, 0, 1);
    }

    // ── What it says ─────────────────────────────────────────────────────────────

    /// <summary>Calendar days from today to the start, both read on the event's own clock.</summary>
    public static int DaysAway(DateTime startUtc, string? zoneId, DateTime nowUtc)
    {
        var start = EventClock.At(startUtc, zoneId).Date;
        var today = EventClock.At(nowUtc, zoneId).Date;
        return (start - today).Days;
    }

    /// <summary>
    /// Every moment the words change, in order: the days counting down, the day itself, the start
    /// and the end.
    /// </summary>
    /// <remarks>
    /// The first step starts at <see cref="DateTime.MinValue"/> and carries the words for "now", so
    /// the list is never empty and the browser always has something to show.
    /// </remarks>
    public static IReadOnlyList<CountdownStep> Steps(
        DateTime startUtc, DateTime endUtc, string? zoneId, DateTime nowUtc)
    {
        if (endUtc <= startUtc) endUtc = startUtc.AddMinutes(1);

        var zone = EventClock.ZoneOf(zoneId);
        var startLocal = EventClock.At(startUtc, zoneId);
        var day = startLocal.Date;
        var dayWords = startLocal.TimeOfDay >= EveningStarts
            ? (CountdownPhase.Tonight, "Tonight")
            : (CountdownPhase.Today, "Today");
        var dayBeforeWords = startLocal.TimeOfDay >= EveningStarts ? "Tomorrow night" : "Tomorrow";

        var steps = new List<CountdownStep>();

        var away = Math.Max(DaysAway(startUtc, zoneId, nowUtc), DaysStepped + 1);
        steps.Add(new CountdownStep(DateTime.MinValue, CountdownPhase.Later, StartsIn(away)));

        for (var k = DaysStepped; k >= 0; k--)
        {
            var from = MidnightUtc(day.AddDays(-k), zone);
            var (phase, words) = k switch
            {
                0 => dayWords,
                1 => (CountdownPhase.Tomorrow, dayBeforeWords),
                _ => (CountdownPhase.Later, StartsIn(k)),
            };
            steps.Add(new CountdownStep(from, phase, words));
        }

        steps.Add(new CountdownStep(startUtc, CountdownPhase.Running, "Happening now"));
        steps.Add(new CountdownStep(endUtc, CountdownPhase.Over, "Over"));
        return steps;
    }

    /// <summary>The step in force at <paramref name="nowUtc"/>.</summary>
    public static CountdownStep At(IReadOnlyList<CountdownStep> steps, DateTime nowUtc)
    {
        var current = steps[0];
        foreach (var step in steps)
        {
            if (step.FromUtc <= nowUtc) current = step;
            else break;
        }
        return current;
    }

    /// <summary>Where it stands at <paramref name="nowUtc"/>.</summary>
    public static CountdownStep Describe(DateTime startUtc, DateTime endUtc, string? zoneId, DateTime nowUtc)
        => At(Steps(startUtc, endUtc, zoneId, nowUtc), nowUtc);

    /// <summary>
    /// The time left, as the ticker writes it: days, hours and minutes, and seconds only in the last
    /// hour (never, for a reader who asked for less motion).
    /// </summary>
    public static string Remaining(TimeSpan left, bool seconds = true)
    {
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        var (d, h, m, s) = Units(left);
        if (d > 0) return $"{d}d {h:00}h {m:00}m";
        if (h > 0) return $"{h}h {m:00}m";
        if (seconds) return $"{m}m {s:00}s";
        // Rounded up: "0 min" one minute before the start reads as already late.
        return $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} min";
    }

    /// <summary>Whole days, hours, minutes and seconds left.</summary>
    public static (int Days, int Hours, int Minutes, int Seconds) Units(TimeSpan left)
    {
        if (left < TimeSpan.Zero) left = TimeSpan.Zero;
        var total = (long)Math.Floor(left.TotalSeconds);
        return ((int)(total / 86400), (int)(total % 86400 / 3600), (int)(total % 3600 / 60), (int)(total % 60));
    }

    /// <summary>
    /// The start in words, for a screen reader: "Starts Saturday, 10/31/2026 at 7:00 PM CDT".
    /// </summary>
    /// <remarks>Read once, never as a ticking live region.</remarks>
    public static string Spoken(DateTime startUtc, DateTime endUtc, string? zoneId, DateTime nowUtc)
    {
        var (at, letters) = EventClock.InPlaceTime(startUtc, zoneId);
        var when = $"{at:dddd}, {at.ToDisplayDate()} at {at.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)} {letters}";
        return nowUtc >= endUtc ? $"Started {when}; it is over"
             : nowUtc >= startUtc ? $"Started {when}; happening now"
             : $"Starts {when}";
    }

    // ── The home card's choice ───────────────────────────────────────────────────

    /// <summary>
    /// Which of the person's things the card shows: the one running now, or else the next to start;
    /// and how many others there are.
    /// </summary>
    /// <returns>Null when nothing is upcoming or running: the card is then not drawn at all.</returns>
    public static (MyNextEventItem Next, int More)? PickNext(
        IEnumerable<MyNextEventItem> items, int total, DateTime nowUtc)
    {
        var live = items.Where(i => i.EndsAtUtc > nowUtc).OrderBy(i => i.StartsAtUtc).ThenBy(i => i.EndsAtUtc).ToList();
        if (live.Count == 0) return null;

        // The total counts what the server knew of when it answered; anything already over since is
        // taken off, never below the ones still in hand.
        var ended = items.Count(i => i.EndsAtUtc <= nowUtc);
        var remaining = Math.Max(live.Count, total - ended);
        return (live[0], remaining - 1);
    }

    private static string StartsIn(int days) => days == 1 ? "Starts in 1 day" : $"Starts in {days} days";

    /// <summary>Midnight starting <paramref name="localDay"/> on the zone's clock, as UTC.</summary>
    /// <remarks>
    /// A few zones move their clocks AT midnight, so midnight can be a time that does not exist; the
    /// day then starts at the first minute that does (Zones.ToUtc).
    /// </remarks>
    private static DateTime MidnightUtc(DateTime localDay, TimeZoneInfo zone)
        => Ben.Data.Common.Helpers.Zones.ToUtc(localDay.Date, zone);
}
