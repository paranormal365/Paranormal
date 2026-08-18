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
}
