using System.Globalization;

namespace Ben.Canvas.Core.Formatting;

/// <summary>
/// Dates shown to people and dates stored in cards, with explicit patterns only.
/// </summary>
/// <remarks>
/// The WebAssembly host runs with invariant globalization, so <c>ToString("g")</c> would read en-US for
/// everybody and a French browser culture would still change separators wherever a culture leaks in.
/// Every call names its pattern and passes <see cref="CultureInfo.InvariantCulture"/>.
/// </remarks>
public static class BcDateFormat
{
    public const string DatePattern = "MMM d, yyyy";
    public const string DateTimePattern = "MMM d, yyyy h:mm tt";
    public const string CardValuePattern = "yyyy-MM-dd";

    /// <summary>A calendar date, e.g. "Sep 14, 2026".</summary>
    public static string Date(DateOnly date) => date.ToString(DatePattern, CultureInfo.InvariantCulture);

    /// <summary>
    /// A message timestamp in the reader's local time. WebAssembly has no local time zone, so the offset
    /// comes from the browser.
    /// </summary>
    public static string MessageTime(DateTime utc, TimeSpan utcOffset)
    {
        var local = DateTime.SpecifyKind(utc, DateTimeKind.Utc).Add(utcOffset);
        return local.ToString(DateTimePattern, CultureInfo.InvariantCulture);
    }

    /// <summary>How a card's date field is stored: ISO "yyyy-MM-dd".</summary>
    public static string ToCardValue(DateOnly date) => date.ToString(CardValuePattern, CultureInfo.InvariantCulture);

    /// <summary>Reads a card's stored date; false for anything that is not exactly "yyyy-MM-dd".</summary>
    public static bool TryParseCardValue(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, CardValuePattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}

/// <summary>"2 min ago" and friends, for the save indicator.</summary>
public static class BcRelativeTime
{
    /// <summary>
    /// A short relative phrase for recent times, or null for a week or more (the caller then shows the date).
    /// A future time - a clock that disagrees with the server - reads "just now" rather than "in 3 min".
    /// </summary>
    public static string? Format(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.FromSeconds(45)) return "just now";
        if (elapsed < TimeSpan.FromMinutes(60))
            return string.Format(CultureInfo.InvariantCulture, "{0} min ago", Math.Max(1, (int)Math.Round(elapsed.TotalMinutes)));
        if (elapsed < TimeSpan.FromHours(24))
            return string.Format(CultureInfo.InvariantCulture, "{0} hr ago", (int)elapsed.TotalHours);
        if (elapsed < TimeSpan.FromDays(7))
        {
            var days = (int)elapsed.TotalDays;
            return days == 1 ? "1 day ago" : string.Format(CultureInfo.InvariantCulture, "{0} days ago", days);
        }

        return null;
    }
}

/// <summary>File sizes, with a dot whatever the culture.</summary>
public static class BcFileSize
{
    /// <summary>"512 B", "1.5 KB", "3.2 MB", "1.1 GB", in steps of 1024.</summary>
    public static string Format(long bytes)
    {
        if (bytes < 1024) return string.Format(CultureInfo.InvariantCulture, "{0} B", Math.Max(0, bytes));

        double value = bytes;
        string[] units = ["KB", "MB", "GB"];
        var unit = -1;
        do
        {
            value /= 1024;
            unit++;
        } while (value >= 1024 && unit < units.Length - 1);

        return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + units[unit];
    }
}
