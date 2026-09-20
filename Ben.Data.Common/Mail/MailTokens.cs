using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace Ben.Data.Common.Mail;

/// <summary>
/// What a template is allowed to say, and what those words become (item 246).
/// </summary>
/// <remarks>
/// <para><b>Two kinds of token.</b> <c>{AppUsers.DisplayName}</c> reads a column of a row the
/// letter is already carrying; <c>{FullDate}</c> and its siblings are worked out here. Both are
/// resolved in the reader's own time zone, and both are HTML-escaped on the way in.</para>
///
/// <para><b>Escaping is not optional.</b> A display name is whatever somebody typed, and a
/// template is HTML by the time it reaches a mail client. A value dropped in raw is a stored
/// cross-site scripting hole aimed at whoever opens the letter — and unlike a page, an email is
/// read in software nobody here controls.</para>
///
/// <para><b>An unresolved token renders as nothing, and the editor refuses to save one.</b> Those
/// two rules go together: catching it at authoring time is what makes rendering it blank safe
/// rather than silently lossy, and showing a guest a literal <c>{AppUsers.DisplayName}</c> is the
/// one outcome nobody would choose.</para>
/// </remarks>
public static class MailTokens
{
    /// <summary>A token: letters, digits and at most one dot.</summary>
    private static readonly Regex Pattern = new(
        @"\{(?<name>[A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)?)\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The tokens that need no table, with what each one looks like.</summary>
    /// <remarks>
    /// US formats throughout, matching the rest of the site (MM/DD/YYYY). They are worked out in
    /// the READER's zone, not the server's: a letter written at 9:05 AM in Nashville is 10:05 AM
    /// to somebody in New York, and telling them 9:05 would be wrong by an hour in the direction
    /// that makes people late.
    /// </remarks>
    public static readonly IReadOnlyList<(string Name, string Looks, string What)> Common =
    [
        ("Date",         "09/20/2026",                  "Today, in the reader's time zone."),
        ("Time",         "9:05 AM",                     "The time the letter was written, where the reader is."),
        ("FullDate",     "September 20, 2026",          "Today, written out."),
        ("FullDateTime", "September 20, 2026 9:05 AM",  "Both, written out."),
        ("Year",         "2026",                        "This year — for a footer that should not go stale."),
        ("SiteName",     "IsHaunted.com",               "What the site calls itself."),
        ("SiteUrl",      "https://ishaunted.com",       "The site's address."),
    ];

    /// <summary>Everything one letter knows, for one reader.</summary>
    /// <param name="Tables">
    /// Table name to its column values — exactly the tables the kind declares, and nothing else.
    /// </param>
    /// <param name="Zone">
    /// The reader's zone. Their own when they have set one, the site's when they have not, which
    /// is the trade Ben chose (2026-09-20): right once somebody says, and never silently personal.
    /// </param>
    public sealed record Context(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> Tables,
        TimeZoneInfo Zone,
        DateTime NowUtc,
        string SiteName,
        string SiteUrl);

    /// <summary>Fills a template in.</summary>
    public static string Render(string? template, Context context)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        return Pattern.Replace(template, match =>
        {
            var name = match.Groups["name"].Value;
            var value = Resolve(name, context);

            // Null means nothing resolved it. Rendering the token back would show a reader the
            // machinery; rendering nothing is the same thing the absent value would have looked
            // like anyway.
            return value is null ? string.Empty : WebUtility.HtmlEncode(value);
        });
    }

    /// <summary>
    /// The tokens in this template that nothing would fill in, for the editor to refuse.
    /// </summary>
    /// <param name="kind">
    /// Whose context decides which tables are allowed. Null checks the built-ins only.
    /// </param>
    public static IReadOnlyList<string> Unresolvable(string? template, MailKindInfo? kind)
    {
        if (string.IsNullOrEmpty(template)) return [];

        var allowed = kind?.Context ?? [];
        var bad = new List<string>();

        foreach (Match match in Pattern.Matches(template))
        {
            var name = match.Groups["name"].Value;

            if (Common.Any(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;

            var dot = name.IndexOf('.');
            if (dot < 0) { bad.Add(name); continue; }

            var table = name[..dot];
            if (!allowed.Any(t => t.Equals(table, StringComparison.OrdinalIgnoreCase))) bad.Add(name);
        }

        return bad.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? Resolve(string name, Context context)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(context.NowUtc, DateTimeKind.Utc), context.Zone);

        switch (name.ToLowerInvariant())
        {
            case "date":         return local.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
            case "time":         return local.ToString("h:mm tt", CultureInfo.InvariantCulture);
            case "fulldate":     return local.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
            case "fulldatetime": return local.ToString("MMMM d, yyyy h:mm tt", CultureInfo.InvariantCulture);
            case "year":         return local.Year.ToString(CultureInfo.InvariantCulture);
            case "sitename":     return context.SiteName;
            case "siteurl":      return context.SiteUrl;
        }

        var dot = name.IndexOf('.');
        if (dot < 0) return null;

        var table = name[..dot];
        var column = name[(dot + 1)..];

        var row = context.Tables.FirstOrDefault(
            t => t.Key.Equals(table, StringComparison.OrdinalIgnoreCase)).Value;
        if (row is null) return null;

        var cell = row.FirstOrDefault(
            c => c.Key.Equals(column, StringComparison.OrdinalIgnoreCase));

        return Format(cell.Value, context.Zone);
    }

    /// <summary>
    /// A column's value as a reader should see it.
    /// </summary>
    /// <remarks>
    /// Dates get the same treatment as <c>{FullDateTime}</c> and for the same reason: a column
    /// holding a UTC instant rendered raw would show a reader a time they were never in, plus a
    /// "T" and a "Z" that mean nothing to them.
    /// </remarks>
    private static string? Format(object? value, TimeZoneInfo zone) => value switch
    {
        null                 => null,
        string s             => s,
        bool b               => b ? "Yes" : "No",
        DateTime d           => TimeZoneInfo.ConvertTimeFromUtc(
                                    DateTime.SpecifyKind(d, DateTimeKind.Utc), zone)
                                .ToString("MMMM d, yyyy h:mm tt", CultureInfo.InvariantCulture),
        DateTimeOffset o     => TimeZoneInfo.ConvertTime(o, zone)
                                .ToString("MMMM d, yyyy h:mm tt", CultureInfo.InvariantCulture),
        decimal m            => m.ToString("0.00", CultureInfo.InvariantCulture),
        IFormattable f       => f.ToString(null, CultureInfo.InvariantCulture),
        _                    => value.ToString(),
    };
}
