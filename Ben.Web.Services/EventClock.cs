using Ben.Data.Common.Constants;

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
/// <para><b>An event with no zone reads in the house zone, not UTC</b> (2026-09-20). The
/// first-run walk found the public What's On list and the event's own public page both printing
/// "8:00 PM UTC" for a night walk at a cave in Adams, Tennessee — five hours out, to a stranger
/// deciding whether to come. UTC was chosen here as the safe answer for a missing zone, and it is
/// safe for arithmetic and wrong for a published time: nobody on this site runs an event on
/// Greenwich's clock. <c>TourController</c> and <c>HostedEventController</c> have always stamped
/// <c>America/Chicago</c> when no zone is given, so the house already has a default; this uses the
/// same one rather than inventing a second.</para>
///
/// <para>The guess is never silent: the zone is still NAMED beside every time, so a reader in
/// Nashville sees "CDT" and a reader who knows better can see that the site has assumed.</para>
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
    /// The zone by its IANA id; the house zone when the id is missing or this machine has never
    /// heard of it.
    /// </summary>
    /// <remarks>
    /// <para>Never throws. A page that shows a time in the wrong zone is bad; a page that will not
    /// render at all because a zone database is missing an entry is worse.</para>
    ///
    /// <para>UTC survives as the answer of last resort, for a machine whose zone database does not
    /// contain the house zone either. At that point there is nothing left to guess with, and a
    /// clock labelled UTC is at least labelled honestly.</para>
    /// </remarks>
    public static TimeZoneInfo ZoneOf(string? id)
    {
        if (!string.IsNullOrWhiteSpace(id) && Find(id) is { } named) return named;
        return Find(HouseClock.ZoneId) ?? TimeZoneInfo.Utc;
    }

    private static TimeZoneInfo? Find(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
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
