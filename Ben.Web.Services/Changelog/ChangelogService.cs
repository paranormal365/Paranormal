using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

namespace Ben.Web.Services.Changelog;

/// <summary>Which product a change belongs to.</summary>
/// <remarks>
/// Three because they ship separately and on different clocks: the website and the API deploy
/// together most days, and the apps only when Apple approves a build. A single merged list would
/// tell somebody on an iPhone about a change they cannot have for a fortnight.
/// </remarks>
public enum ChangelogStream
{
    Website,
    Api,
    Apps,
}

public static class ChangelogStreamNames
{
    public static string Title(this ChangelogStream stream) => stream switch
    {
        ChangelogStream.Website => "Website",
        ChangelogStream.Api => "Service",
        ChangelogStream.Apps => "iPhone and iPad",
        _ => stream.ToString(),
    };

    /// <summary>What the reader is being told about, in a sentence.</summary>
    public static string Description(this ChangelogStream stream) => stream switch
    {
        ChangelogStream.Website => "Pages, forms and everything you do in a browser.",
        ChangelogStream.Api => "The service behind the site and the apps.",
        ChangelogStream.Apps => "The iPhone and iPad app, as each version reaches the App Store.",
        _ => string.Empty,
    };

    public static string Slug(this ChangelogStream stream) => stream.ToString().ToLowerInvariant();
}

/// <summary>One day's changes to one product, and the release that carried them.</summary>
/// <param name="Version">
/// "2.11.0", or null for the history before the product went live (Ben, 2026-09-28: "Go back to
/// when it went live and call that 1.0.0").
/// </param>
public sealed record ChangelogDay(ChangelogStream Stream, DateOnly Date, ImmutableArray<string> Entries,
                                  string? Version = null);

/// <summary>
/// The public record of what changed, newest first.
/// </summary>
/// <remarks>
/// <para><b>Written by hand, from the history.</b> The three markdown files under
/// <c>Changelog/Content</c> are the record; git is where they came from, not what they are. A
/// changelog generated from commit subjects reads like a build log and leaks the shape of the
/// codebase — half of it would be refactors, test fixtures and deployment plumbing that mean
/// nothing to the person reading, and the other half would name files and branches.</para>
///
/// <para><b>Nothing sensitive goes in.</b> These pages are public and anonymous, so an entry says
/// what changed for the reader and never how: no security-fix detail that reads as a recipe, no
/// internal names, no infrastructure, no customer or group named. The rule is written at the top
/// of each file, where the next person to add a line will see it.</para>
///
/// <para>Embedded in the assembly, parsed once and cached, for the same reasons as
/// <see cref="Help.HelpContentService"/>: the content cannot change without a redeploy, and a
/// file under wwwroot is served raw to anybody who guesses its name.</para>
/// </remarks>
public sealed class ChangelogService
{
    private static readonly string ResourcePrefix =
        typeof(ChangelogService).Assembly.GetName().Name + ".Changelog.Content.";

    private readonly Lazy<ImmutableArray<ChangelogDay>> _days;

    public ChangelogService()
    {
        _days = new Lazy<ImmutableArray<ChangelogDay>>(LoadAll);
    }

    /// <summary>Every day's changes, newest first, optionally for one product only.</summary>
    public ImmutableArray<ChangelogDay> Days(ChangelogStream? stream = null)
        => stream is null
            ? _days.Value
            : _days.Value.Where(d => d.Stream == stream).ToImmutableArray();

    /// <summary>When anything last changed, or null when there is nothing to show.</summary>
    public DateOnly? LastChanged(ChangelogStream? stream = null)
    {
        var days = Days(stream);
        return days.IsEmpty ? null : days[0].Date;
    }

    private ImmutableArray<ChangelogDay> LoadAll()
    {
        var days = new List<ChangelogDay>();
        foreach (var stream in Enum.GetValues<ChangelogStream>())
        {
            var text = Read(stream);
            if (text is null) continue;
            days.AddRange(Parse(stream, text));
        }

        // Newest first across all three, then by product so a single day reads in a stable order
        // rather than in whatever order the files happened to load.
        return days
            .OrderByDescending(d => d.Date)
            .ThenBy(d => d.Stream)
            .ToImmutableArray();
    }

    private static string? Read(ChangelogStream stream)
    {
        var assembly = typeof(ChangelogService).Assembly;
        var name = ResourcePrefix + stream.Slug() + ".md";
        using var resource = assembly.GetManifestResourceStream(name);
        if (resource is null) return null;
        using var reader = new StreamReader(resource);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads the one shape these files are allowed to have: <c>## yyyy-MM-dd</c> headings — with the
    /// release's version after a middle dot, <c>## yyyy-MM-dd · 2.11.0</c>, from go-live on — each
    /// followed by <c>- </c> lines.
    /// </summary>
    /// <remarks>
    /// Deliberately strict and deliberately silent about anything else. A date it cannot parse is
    /// skipped rather than guessed at, because a changelog that invents a date is worse than one
    /// missing a day — and everything before the first heading is the file's own preamble,
    /// including the rule about what may go in it.
    /// </remarks>
    internal static IEnumerable<ChangelogDay> Parse(ChangelogStream stream, string markdown)
    {
        DateOnly? date = null;
        string? version = null;
        var entries = new List<string>();

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (date is not null && entries.Count > 0)
                    yield return new ChangelogDay(stream, date.Value, entries.ToImmutableArray(), version);

                entries = [];
                var heading = line[3..].Split('·', 2, StringSplitOptions.TrimEntries);
                date = DateOnly.TryParseExact(heading[0], "yyyy-MM-dd",
                                              CultureInfo.InvariantCulture,
                                              DateTimeStyles.None, out var parsed)
                    ? parsed
                    : null;
                // A version that is not three numbers is dropped rather than shown half-right; the
                // tests refuse such a file before it ships.
                version = heading.Length > 1 && ReleaseVersion.TryParse(heading[1], out var v) ? v.ToString() : null;
                continue;
            }

            if (date is null) continue;

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var entry = line[2..].Trim();
                if (entry.Length > 0) entries.Add(entry);
                continue;
            }

            // A wrapped entry continues on the next line. Without this the file has to be written
            // with one very long line per change, and the first person to let an editor wrap one
            // would silently publish half a sentence.
            if (line.Length > 0 && entries.Count > 0 && !line.StartsWith('#'))
                entries[^1] = entries[^1] + " " + line;
        }

        if (date is not null && entries.Count > 0)
            yield return new ChangelogDay(stream, date.Value, entries.ToImmutableArray(), version);
    }
}

/// <summary>
/// A release number: major.minor.patch (Ben, 2026-09-28).
/// </summary>
/// <remarks>
/// <para>"Minor releases are like 1.0.1. Larger releases are 1.1.0. Huge and major releases are
/// 2.0.0." A day of fixes moves the last number; a day that adds something people can use moves
/// the middle one; a whole new part of the product moves the first. The website and the service are
/// numbered on their own from the day they went live; the apps use the number on the App Store.</para>
/// </remarks>
public readonly record struct ReleaseVersion(int Major, int Minor, int Patch) : IComparable<ReleaseVersion>
{
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;
        var parts = text?.Trim().Split('.');
        if (parts is not { Length: 3 }) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
            return false;
        version = new ReleaseVersion(major, minor, patch);
        return true;
    }

    /// <summary>Whether <paramref name="next"/> is exactly one step on: a patch, a minor or a major bump.</summary>
    public bool IsNextStep(ReleaseVersion next) =>
        next == this with { Patch = Patch + 1 }
        || next == new ReleaseVersion(Major, Minor + 1, 0)
        || next == new ReleaseVersion(Major + 1, 0, 0);

    public int CompareTo(ReleaseVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major)
        : Minor != other.Minor ? Minor.CompareTo(other.Minor)
        : Patch.CompareTo(other.Patch);

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>
/// An entry as the page draws it: its <c>**bold**</c>, <c>*italic*</c> and <c>`code`</c>, and nothing else.
/// </summary>
/// <remarks>
/// The files have used those three since the start, and the page printed the asterisks (found
/// 2026-09-28). The text is HTML-encoded first and only those markers become tags, so an entry can
/// never put markup of its own on a public page.
/// </remarks>
public static class ChangelogText
{
    public static string ToHtml(string entry)
    {
        var html = System.Net.WebUtility.HtmlEncode(entry);
        html = System.Text.RegularExpressions.Regex.Replace(html, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
        html = System.Text.RegularExpressions.Regex.Replace(html, @"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", "<em>$1</em>");
        html = System.Text.RegularExpressions.Regex.Replace(html, @"`(.+?)`", "<code>$1</code>");
        return html;
    }
}
