using Ben.Data.Common.Constants;

namespace Ben.Data.Common.Helpers;

/// <summary>
/// The one place a time zone id is checked, resolved and inherited (2026-09-28).
/// </summary>
/// <remarks>
/// <para><b>Why one place.</b> Before this, "find this zone or fall back" was written out nine times
/// — the calendar sync fell back to UTC, the event clock to Chicago, the mailers each their own way —
/// so the same event could read in two zones depending on which screen drew it. People, groups,
/// cases and investigations now have zones as well, and they inherit from each other; that chain is
/// only trustworthy if every caller walks it the same way.</para>
///
/// <para><b>Ids are IANA</b> ("America/Chicago"), stored as given. .NET resolves IANA ids on
/// Windows as well as Linux and macOS, so the production host needs no mapping table; a Windows id
/// that arrives anyway is converted to its IANA name before it is stored, so nothing written here
/// only works on one operating system.</para>
/// </remarks>
public static class Zones
{
    /// <summary>The column width every zone id is stored in.</summary>
    public const int MaxLength = 64;

    /// <summary>
    /// The id as it should be stored — trimmed, IANA — or null when the platform does not know it.
    /// </summary>
    public static string? Normalize(string? id)
    {
        var trimmed = id?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength) return null;
        if (!TryFind(trimmed, out _)) return null;
        // A Windows id ("Central Standard Time") resolves on Windows only; store its IANA name.
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(trimmed, out var iana)) return iana;
        return trimmed;
    }

    /// <summary>Whether <paramref name="id"/> names a zone the platform knows.</summary>
    public static bool IsKnown(string? id) => Normalize(id) is not null;

    /// <summary>
    /// The first id in <paramref name="chain"/> the platform knows, or the house clock. The chain is
    /// the inheritance order: an investigation's own zone, then its case's, then its group's.
    /// </summary>
    public static string Effective(params string?[] chain)
    {
        foreach (var id in chain)
            if (Normalize(id) is { } known) return known;
        return HouseClock.ZoneId;
    }

    /// <summary>The zone the chain resolves to, falling back as <see cref="Effective"/> does.</summary>
    public static TimeZoneInfo Find(params string?[] chain) =>
        TryFind(Effective(chain), out var zone) ? zone : TimeZoneInfo.Utc;

    /// <summary>An instant, stored as UTC, as the wall-clock time in <paramref name="zone"/>.</summary>
    public static DateTime ToZone(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    /// <summary>
    /// A wall-clock time in <paramref name="zone"/> as the UTC instant to store. A time that never
    /// happened there (the hour clocks skip in spring) moves forward to the first one that did.
    /// </summary>
    public static DateTime ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var wall = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(wall)) wall = wall.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(wall, zone);
    }

    /// <summary>
    /// What a reader calls the zone at that moment: "CDT", "CST", "MST" — or "UTC-3" where the
    /// platform has no letters for it. Daylight saving decides which, so it needs the instant.
    /// </summary>
    public static string Abbreviation(TimeZoneInfo zone, DateTime utc)
    {
        var at = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var daylight = zone.IsDaylightSavingTime(at);
        if (Letters.TryGetValue(IanaId(zone), out var pair)) return daylight ? pair.Daylight : pair.Standard;
        if (zone == TimeZoneInfo.Utc || zone.Id is "UTC" or "Etc/UTC") return "UTC";
        var offset = zone.GetUtcOffset(at);
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var abs = offset.Duration();
        return abs.Minutes == 0 ? $"UTC{sign}{abs.Hours}" : $"UTC{sign}{abs.Hours}:{abs.Minutes:00}";
    }

    /// <summary>The IANA id of a resolved zone, whichever form the platform handed back.</summary>
    public static string IanaId(TimeZoneInfo zone) =>
        TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var iana) ? iana : zone.Id;

    private static bool TryFind(string id, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            zone = TimeZoneInfo.Utc;
            return false;
        }
    }

    // The platform's own abbreviations vary by operating system (Windows has none), and a reader in
    // the US expects the letters on their clock. The zones most of this site's groups keep.
    private static readonly Dictionary<string, (string Standard, string Daylight)> Letters = new()
    {
        ["America/New_York"]    = ("EST", "EDT"),
        ["America/Detroit"]     = ("EST", "EDT"),
        ["America/Indiana/Indianapolis"] = ("EST", "EDT"),
        ["America/Kentucky/Louisville"]  = ("EST", "EDT"),
        ["America/Chicago"]     = ("CST", "CDT"),
        ["America/Denver"]      = ("MST", "MDT"),
        ["America/Boise"]       = ("MST", "MDT"),
        ["America/Phoenix"]     = ("MST", "MST"),
        ["America/Los_Angeles"] = ("PST", "PDT"),
        ["America/Anchorage"]   = ("AKST", "AKDT"),
        ["Pacific/Honolulu"]    = ("HST", "HST"),
        ["America/Puerto_Rico"] = ("AST", "AST"),
        ["America/Halifax"]     = ("AST", "ADT"),
        ["America/Toronto"]     = ("EST", "EDT"),
        ["America/Vancouver"]   = ("PST", "PDT"),
        ["Europe/London"]       = ("GMT", "BST"),
        ["Europe/Dublin"]       = ("GMT", "IST"),
    };
}
