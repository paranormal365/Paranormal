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
}
