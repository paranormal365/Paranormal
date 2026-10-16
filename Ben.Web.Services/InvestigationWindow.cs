namespace Ben.Web.Services;

/// <summary>
/// How the start and end of an investigation relate to each other.
/// </summary>
/// <remarks>
/// <para>
/// Ben's rule, 2026-09-09: <em>"you usually arrive like 3pm on one day and the investigation ends
/// 8am the next day, this is considered a single-day investigation"</em>. So a visit that crosses
/// midnight is still one day — one arrival, one departure — and the ordinary form asks for a
/// clock time rather than a second date. Anything longer is behind a checkbox.
/// </para>
/// <para>
/// Three screens need the same arithmetic — the case Investigations dialog, the Propose Dates
/// slots and the case-less Schedule window — and a rule copied three times is a rule that will
/// disagree with itself. Everything here is pure, and takes times already in the viewer's zone.
/// </para>
/// </remarks>
public static class InvestigationWindow
{
    /// <summary>
    /// The end a single-day investigation gets from a clock time. A time at or before the start's
    /// is the next morning.
    /// </summary>
    public static DateTime SingleDayEnd(DateTime start, TimeSpan timeOfDay)
    {
        var end = start.Date + timeOfDay;
        return end <= start ? end.AddDays(1) : end;
    }

    /// <summary>
    /// Whether an existing window needs the multi-day controls to be shown truthfully.
    /// </summary>
    /// <remarks>
    /// Asked as "could the single-day rule have produced this?" rather than by counting days,
    /// because both answers are one calendar day apart: 3pm to 8am the next morning is single-day,
    /// 3pm to 11pm the following night is not.
    /// </remarks>
    public static bool NeedsMultiDay(DateTime start, DateTime? end)
        => end is { } e && SingleDayEnd(start, e.TimeOfDay) != e;

    /// <summary>
    /// A window typed on the viewer's own clock, as the UTC instants it is stored as.
    /// </summary>
    /// <remarks>
    /// <para>Ben, 10/16/2026: "if I enter 7pm to 7am, it knows I am referring to in my location".
    /// Each end is converted on its own, through the viewer's zone, so a night that crosses a
    /// daylight-saving change comes out the right length: 7 PM on 10/31 to 7 AM on 11/1 in
    /// Central is thirteen hours, not twelve.</para>
    ///
    /// <para>Never <c>ToUniversalTime()</c>: on a Blazor Server page that converts through the
    /// SERVER's zone, which put every investigation scheduled on production an hour out.</para>
    /// </remarks>
    public static (DateTime StartUtc, DateTime? EndUtc) ToUtc(
        DateTime localStart, DateTime? localEnd, IBenUserState viewer)
        => (localStart.ToUtcFromViewerLocal(viewer), localEnd?.ToUtcFromViewerLocal(viewer));

    /// <summary>When a new visit starts unless somebody says otherwise: 7:00 PM tomorrow.</summary>
    /// <remarks>
    /// Investigations are evening work, and 7:00 PM is the site's usual start (a hosted event's
    /// doors open then unless a night says otherwise). Until 10/16/2026 the Schedule window
    /// suggested this moment tomorrow, to the minute, which read as "6:32 AM". No group keeps a
    /// usual start time of its own yet; this is where one would be read.
    /// </remarks>
    public static readonly TimeSpan UsualStart = new(19, 0, 0);

    /// <summary>
    /// The start a new visit is offered, on the viewer's own clock.
    /// </summary>
    /// <param name="viewerNow">Now, on the viewer's clock (<c>NowInViewerTimeZone()</c>).</param>
    public static DateTime DefaultStart(DateTime viewerNow)
        => DateTime.SpecifyKind(viewerNow.Date.AddDays(1) + UsualStart, DateTimeKind.Unspecified);

    /// <summary>
    /// The letters of the viewer's zone as it will be AT the typed start: "CST" for a November
    /// visit typed in October, not today's "CDT" (10/16/2026).
    /// </summary>
    /// <param name="localStart">The start as typed, on the viewer's clock; null when nothing is typed.</param>
    /// <param name="viewer">Whose clock.</param>
    /// <param name="utcNow">Used only when nothing has been typed yet.</param>
    public static string ViewerLetters(DateTime? localStart, IBenUserState viewer, DateTime utcNow)
    {
        var zone = viewer.ViewerTimeZone ?? viewer.BrowserTimeZone ?? TimeZoneInfo.Utc;
        var at = localStart is { } start ? start.ToUtcFromViewerLocal(viewer) : utcNow;
        return Ben.Data.Common.Helpers.Zones.Abbreviation(zone, at);
    }
}
