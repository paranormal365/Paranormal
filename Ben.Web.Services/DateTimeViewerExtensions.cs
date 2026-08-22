namespace Ben.Web.Services;

/// <summary>
/// Converts between UTC (how every DateTime is stored) and the viewer's browser-resolved
/// local time (<see cref="IBenUserState.BrowserTimeZone"/>). Replaces `.ToLocalTime()`, which
/// under Blazor Server Interactive render mode converts to the SERVER's OS timezone, not the
/// viewing browser's.
/// </summary>
public static class DateTimeViewerExtensions
{
    /// <summary>Converts a stored UTC value to the viewer's local wall-clock time.</summary>
    public static DateTime ToViewerLocalTime(this DateTime utc, IBenUserState userState) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), userState.BrowserTimeZone);

    /// <summary>
    /// Converts a viewer-local wall-clock value (e.g. from a picker bound to a viewer-local
    /// display value) back to UTC for persistence. A value that falls in a DST spring-forward
    /// gap (and so never actually occurred in the viewer's timezone) is nudged forward minute by
    /// minute until it lands on a real instant, rather than throwing.
    /// </summary>
    public static DateTime ToUtcFromViewerLocal(this DateTime viewerLocal, IBenUserState userState)
    {
        var zone = userState.BrowserTimeZone;
        var local = DateTime.SpecifyKind(viewerLocal, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(local))
            local = local.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>The current instant, expressed in the viewer's local wall-clock time.</summary>
    public static DateTime NowInViewerTimeZone(this IBenUserState userState) =>
        DateTime.UtcNow.ToViewerLocalTime(userState);

    // ── Display formats ──────────────────────────────────────────────────────
    // One place decides how a date looks, so pages cannot drift apart the way they had.
    //
    // "One place" was not true for a long time, and saying it here did not make it so. A constant
    // governs only what refers to it, and a Telerik picker or grid column takes a format STRING
    // attribute — so 74 call sites across 28 files carried their own hand-typed day-first pattern
    // while these constants sat here saying month-first and looking authoritative. Ben reported it
    // as day-first a fourth time on 2026-08-21 and was right every time. The call sites now
    // reference these constants, and DateFormatSourceGuardTests fails the build if a new literal
    // date format appears anywhere.
    //
    // **US format, month first.** This is Ben's stated preference and it is not a style question
    // to be re-litigated: the site's users, its groups and its cases are all American, and
    // "08/04/2026" means August 4th to every one of them. These constants were day-first until
    // 2026-08-21 — a previous session chose that deliberately, commented it "Day first", and wrote
    // DisplayDateFormatTests to pin it, including a test named Date_IsDayFirst. So the format was
    // asserted all along; it was asserted wrong, which is worse than unasserted: the suite was
    // green and actively defending the mistake.
    //
    // These format an already-local value and deliberately do not convert: each call site keeps
    // whatever timezone handling it already had, so this changes appearance only. Pair them with
    // ToViewerLocalTime when the source is UTC.

    /// <summary>US month-first numeric date, used by every grid column and date control.</summary>
    public const string DatePattern = "MM/dd/yyyy";

    /// <summary>Numeric date with a 12-hour clock and seconds.</summary>
    public const string DateTimePattern = "MM/dd/yyyy hh:mm:ss tt";

    /// <summary>Numeric date and time without seconds, where seconds carry no meaning.</summary>
    public const string DateTimeNoSecondsPattern = "MM/dd/yyyy hh:mm tt";

    /// <summary>Written-out date for prose: <c>August 4, 2026</c>.</summary>
    public const string LongDatePattern = "MMMM d, yyyy";

    /// <summary>Time on its own, 12-hour with seconds.</summary>
    public const string TimePattern = "hh:mm:ss tt";

    /// <summary>
    /// A date short enough for a chart axis, where thirty of them sit side by side and a slash
    /// pattern will not fit. Month first, like every other date on the site — an axis is not
    /// exempt from that just because a charting library draws it.
    /// </summary>
    public const string ChartDayPattern = "MMM d";

    /// <summary>
    /// A date with the month spelled short: <c>Aug 4, 2026</c>. For prose — emails, bylines,
    /// "published on" lines — where slashes read as data rather than a sentence.
    /// </summary>
    public const string MediumDatePattern = "MMM d, yyyy";

    /// <summary>
    /// The date pattern wrapped for a Telerik <c>DisplayFormat</c>, which wants <c>{0:...}</c>.
    /// </summary>
    /// <remarks>
    /// These exist because the constants above could not reach a grid column or a picker: those
    /// take a format STRING attribute, so every one of them carried its own hand-typed pattern.
    /// Seventy-four of them were day-first while the shared constants said month-first, and the
    /// constants looked authoritative the whole time. A constant only governs what refers to it.
    /// </remarks>
    public const string GridDateFormat = "{0:" + DatePattern + "}";

    /// <summary>The date-and-time pattern wrapped for a Telerik <c>DisplayFormat</c>.</summary>
    public const string GridDateTimeFormat = "{0:" + DateTimeNoSecondsPattern + "}";

    /// <summary>The short month-first label used on chart axes.</summary>
    public static string ToChartDay(this DateTime local) =>
        local.ToString(ChartDayPattern, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The same label for a plain day, which is what a daily series actually is.</summary>
    public static string ToChartDay(this DateOnly day) =>
        day.ToString(ChartDayPattern, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A date on its own: <c>08/04/2026</c>. Month first.</summary>
    public static string ToDisplayDate(this DateTime local) =>
        local.ToString(DatePattern, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A date and time: <c>08/04/2026 09:30:00 PM</c>.</summary>
    public static string ToDisplayDateTime(this DateTime local) =>
        local.ToString(DateTimePattern, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// The same date written out: <c>August 4, 2026</c>. For prose and cards — anywhere the date
    /// is being read rather than scanned down a column. Grids and date controls stay numeric, so a
    /// column of dates still lines up and compares at a glance.
    /// </summary>
    public static string ToDisplayDateLong(this DateTime local) =>
        local.ToString(LongDatePattern, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A time on its own: <c>09:30:00 PM</c>.</summary>
    public static string ToDisplayTime(this DateTime local) =>
        local.ToString(TimePattern, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Nullable overloads, so call sites keep their own placeholder for "not set".</summary>
    public static string? ToDisplayDate(this DateTime? local) => local?.ToDisplayDate();

    public static string? ToDisplayDateTime(this DateTime? local) => local?.ToDisplayDateTime();

    public static string? ToDisplayDateLong(this DateTime? local) => local?.ToDisplayDateLong();
}
