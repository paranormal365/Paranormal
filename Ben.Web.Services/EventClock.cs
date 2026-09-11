namespace Ben.Web.Services;

/// <summary>
/// The clock a date is shown on.
/// </summary>
/// <remarks>
/// <para>Ben's rule, 2026-09-10: "The dates and times may be recorded in UTC, but should render at
/// either UTC or at the time of the location where the evidence was collected or photo taken."
/// A walk that starts at eight in Nashville starts at eight for everybody reading about it — the
/// reader's own clock is the wrong answer, because it turns one night into a different night
/// depending on where the reader happens to be sitting, and it disagrees with the printed ticket,
/// the guest mail and the calendar file, all of which use the place's time.</para>
///
/// <para>So: the place's zone when the place's zone is recorded, and UTC when it is not. Never
/// silently the server's, which is what <c>DateTime.ToLocalTime()</c> gives and which is nobody's
/// clock in particular. The zone is always NAMED beside the time, so a reader never has to guess
/// whose seven o'clock they are looking at.</para>
///
/// <para>Today only a tour records its zone (<c>Tour.TimeZoneId</c>), so a tour date reads in the
/// walk's own time and any other event reads in UTC. Giving an ordinary calendar event a zone of
/// its own is the follow-on; nothing here changes when it arrives, because everything already asks
/// this one method.</para>
/// </remarks>
public static class EventClock
{
    /// <summary>
    /// A stored UTC instant as the place it happens would read it, with the name of that clock.
    /// </summary>
    public static (DateTime At, string Zone) InPlaceTime(DateTime utc, string? ianaZoneId)
    {
        var zone = ZoneOf(ianaZoneId);
        var at = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);
        return (at, Abbreviate(zone, at));
    }

    /// <summary>Just the instant, for a caller that shows the zone somewhere else on the page.</summary>
    public static DateTime At(DateTime utc, string? ianaZoneId) => InPlaceTime(utc, ianaZoneId).At;

    /// <summary>
    /// The zone by its IANA id, or UTC when the id is missing or this machine has never heard of
    /// it.
    /// </summary>
    /// <remarks>
    /// Never throws. A page that shows a time in the wrong zone is bad; a page that will not
    /// render at all because a zone database is missing an entry is worse.
    /// </remarks>
    public static TimeZoneInfo ZoneOf(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    /// <summary>"CDT", "EST", "UTC" — the initials of the long name the platform gives.</summary>
    private static string Abbreviate(TimeZoneInfo zone, DateTime localTime)
    {
        if (zone == TimeZoneInfo.Utc) return "UTC";

        var name = zone.IsDaylightSavingTime(localTime) ? zone.DaylightName : zone.StandardName;
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // "Central Daylight Time" → "CDT". A one-word name (some zones have none) is its own label.
        return words.Length >= 2 ? new string([.. words.Select(w => w[0])]) : name;
    }
}
