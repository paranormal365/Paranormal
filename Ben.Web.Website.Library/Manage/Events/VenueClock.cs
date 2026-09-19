namespace Ben.Web.Website.Library.Manage.Events;

/// <summary>
/// Times on the venue's clock, for pages that show an event's programme (item 235 phase 10).
/// </summary>
/// <remarks>
/// A guest flying in needs "9 PM at the hotel", not 9 PM wherever their browser is. A zone the
/// server does not know falls back to UTC rather than throwing, and the time is still a time.
/// </remarks>
public static class VenueClock
{
    public static TimeZoneInfo Zone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone))
            return zone;
        return TimeZoneInfo.Utc;
    }

    /// <summary>The venue's local time for a UTC instant, whatever kind the value arrived as.</summary>
    public static DateTime Local(DateTime utc, string? timeZoneId)
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone(timeZoneId));

    /// <summary>"9:00 PM–10:00 PM".</summary>
    public static string Span(DateTime startsUtc, DateTime endsUtc, string? timeZoneId)
        => $"{Local(startsUtc, timeZoneId):h:mm tt}–{Local(endsUtc, timeZoneId):h:mm tt}";
}
