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
    // One place decides how a date looks, so pages cannot drift apart the way they had: the same
    // screens were mixing "yyyy-MM-dd", "d MMM yyyy", "MMM d, yyyy h:mm tt" and "d MMM, HH:mm".
    //
    // These format an already-local value and deliberately do not convert: each call site keeps
    // whatever timezone handling it already had, so this changes appearance only. Pair them with
    // ToViewerLocalTime when the source is UTC.

    /// <summary>A date on its own: <c>08/04/2026</c>.</summary>
    public static string ToDisplayDate(this DateTime local) =>
        local.ToString("MM/dd/yyyy", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A date and time: <c>08/04/2026 09:30:00 PM</c>.</summary>
    public static string ToDisplayDateTime(this DateTime local) =>
        local.ToString("MM/dd/yyyy hh:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A time on its own: <c>09:30:00 PM</c>.</summary>
    public static string ToDisplayTime(this DateTime local) =>
        local.ToString("hh:mm:ss tt", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Nullable overloads, so call sites keep their own placeholder for "not set".</summary>
    public static string? ToDisplayDate(this DateTime? local) => local?.ToDisplayDate();

    public static string? ToDisplayDateTime(this DateTime? local) => local?.ToDisplayDateTime();
}
