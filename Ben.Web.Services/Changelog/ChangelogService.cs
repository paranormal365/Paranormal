using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

namespace Ben.Web.Services.Changelog;

/// <summary>Which project a change belongs to: what the filter on <c>/changes</c> offers first.</summary>
/// <remarks>
/// Ben, 10/14/2026: "use Website, API, iOS, Sidecar as projects then if you pick Website, you can
/// subfilter by WebApp, Canvas Board, Video Editor or Audio Editor" — and the CMS, "as its own
/// project as well, like the video and audio editor."
/// </remarks>
public enum ChangelogProject
{
    Website,
    Api,
    Ios,
    Sidecar,
}

/// <summary>Which product a change belongs to; each has its own file and its own version.</summary>
/// <remarks>
/// One per thing that is numbered on its own (Ben, 10/14/2026). The website is five of them — the
/// web app, its three editors and the CMS, which grow at their own pace — and the service, the
/// Sidecar on people's computers and the iPhone and iPad app, which reaches phones only when Apple
/// approves a build, are one each. A single merged list would tell somebody on an iPhone about a
/// change they cannot have for a fortnight.
/// </remarks>
public enum ChangelogStream
{
    WebApp,
    Canvas,
    VideoEditor,
    AudioEditor,
    Cms,
    Api,
    Ios,
    Sidecar,
}

public static class ChangelogStreamNames
{
    public static ChangelogProject Project(this ChangelogStream stream) => stream switch
    {
        ChangelogStream.Api => ChangelogProject.Api,
        ChangelogStream.Ios => ChangelogProject.Ios,
        ChangelogStream.Sidecar => ChangelogProject.Sidecar,
        _ => ChangelogProject.Website,
    };

    /// <summary>The products a project is made of, in the order the filter lists them.</summary>
    public static IReadOnlyList<ChangelogStream> Streams(this ChangelogProject project) =>
        Enum.GetValues<ChangelogStream>().Where(s => s.Project() == project).ToList();

    public static string Title(this ChangelogProject project) => project switch
    {
        ChangelogProject.Website => "Website",
        ChangelogProject.Api => "API",
        ChangelogProject.Ios => "iOS",
        ChangelogProject.Sidecar => "Sidecar",
        _ => project.ToString(),
    };

    public static string Title(this ChangelogStream stream) => stream switch
    {
        ChangelogStream.WebApp => "Web App",
        ChangelogStream.Canvas => "Canvas Board",
        ChangelogStream.VideoEditor => "Video Editor",
        ChangelogStream.AudioEditor => "Audio Editor",
        ChangelogStream.Cms => "CMS",
        _ => stream.Project().Title(),
    };

    /// <summary>What the reader is being told about, in a sentence.</summary>
    public static string Description(this ChangelogProject project) => project switch
    {
        ChangelogProject.Website => "Everything you do in a browser: the site itself, the editors it carries and the CMS.",
        ChangelogProject.Api => "The service behind the site and the apps.",
        ChangelogProject.Ios => "The iPhone and iPad app, as each version reaches the App Store.",
        ChangelogProject.Sidecar => "The small app that does the video editor's heavy work on your own computer.",
        _ => string.Empty,
    };

    /// <summary>What the reader is being told about, in a sentence.</summary>
    public static string Description(this ChangelogStream stream) => stream switch
    {
        ChangelogStream.WebApp => "Pages, forms and everything else on the site that is not one of its editors.",
        ChangelogStream.Canvas => "The research boards on a case, where a place's history is worked out.",
        ChangelogStream.VideoEditor => "The video editor, on the site and at its own address.",
        ChangelogStream.AudioEditor => "The audio player and editor: regions, the spectrogram, EVP scanning, clips and the case mixer.",
        ChangelogStream.Cms => "The pages a group builds for its public site.",
        _ => stream.Project().Description(),
    };

    /// <summary>The name in the address (<c>/changes?project=website</c>).</summary>
    public static string Slug(this ChangelogProject project) => project.ToString().ToLowerInvariant();

    /// <summary>The name in the address (<c>&amp;part=video-editor</c>) and of its file.</summary>
    public static string Slug(this ChangelogStream stream) => stream switch
    {
        ChangelogStream.VideoEditor => "video-editor",
        ChangelogStream.AudioEditor => "audio-editor",
        _ => stream.ToString().ToLowerInvariant(),
    };
}

/// <summary>One version of one product, and what changed in it.</summary>
/// <param name="Version">"2.11.0"; null only for the unreleased section.</param>
/// <param name="Date">
/// When it was released, if the heading says. A bonus: the version is what is tracked (Ben,
/// 10/14/2026).
/// </param>
/// <param name="Entries">What changed for everybody.</param>
/// <param name="Unreleased">Merged but not released yet; shown to SuperAdmins only.</param>
public sealed record ChangelogRelease(ChangelogStream Stream, string? Version, DateOnly? Date,
                                      ImmutableArray<string> Entries, bool Unreleased = false)
{
    /// <summary>
    /// What changed in administration — its screens, and the concepts and ideas behind running the
    /// site — from the version's <c>### Administration</c> section. SuperAdmins only (Ben,
    /// 10/14/2026).
    /// </summary>
    public ImmutableArray<string> AdminEntries { get; init; } = [];

    /// <summary>Whether anybody but a SuperAdmin has something to read here.</summary>
    public bool IsPublic => !Unreleased && !Entries.IsEmpty;
}

/// <summary>
/// The public record of what changed, newest first.
/// </summary>
/// <remarks>
/// <para><b>Written by hand, from the history.</b> The markdown files under
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
/// <para><b>Versions, not days</b> (Ben, 10/14/2026). Each product is numbered on its own, one
/// heading per version; a date after it is shown when it is there. A major change moves the first
/// number, something new that is not an overhaul the middle one, fixes and everyday updates the
/// last. The tests hold each product's newest version equal to the one its build carries.</para>
///
/// <para>Embedded in the assembly, parsed once and cached, for the same reasons as
/// <see cref="Help.HelpContentService"/>: the content cannot change without a redeploy, and a
/// file under wwwroot is served raw to anybody who guesses its name.</para>
/// </remarks>
public sealed class ChangelogService
{
    private static readonly string ResourcePrefix =
        typeof(ChangelogService).Assembly.GetName().Name + ".Changelog.Content.";

    /// <summary>The heading for work merged but not yet released.</summary>
    public const string UnreleasedHeading = "Unreleased";

    /// <summary>The sub-heading, inside a version, for what only SuperAdmins see.</summary>
    public const string AdministrationHeading = "Administration";

    private readonly Lazy<ImmutableArray<ChangelogRelease>> _all;

    public ChangelogService()
    {
        _all = new Lazy<ImmutableArray<ChangelogRelease>>(LoadAll);
    }

    /// <summary>
    /// What has been released, newest first, optionally for one product only. Never the
    /// "Unreleased" section. Includes versions whose only changes are administration ones: the
    /// numbering is the product's, whoever may read it — the page decides what a viewer sees
    /// (<see cref="ChangelogRelease.IsPublic"/>).
    /// </summary>
    public ImmutableArray<ChangelogRelease> Releases(ChangelogStream? stream = null)
        => _all.Value.Where(r => !r.Unreleased && (stream is null || r.Stream == stream)).ToImmutableArray();

    /// <summary>
    /// What is merged but not yet released — the "## Unreleased" section at the top of a file, at
    /// most one per product. For SuperAdmins only (the way benclark.net keeps it): the
    /// public page says what people can already use, never what is coming.
    /// </summary>
    public ImmutableArray<ChangelogRelease> Unreleased(ChangelogStream? stream = null)
        => _all.Value.Where(r => r.Unreleased && (stream is null || r.Stream == stream)).ToImmutableArray();

    /// <summary>The newest released version of a product, or null before it had one.</summary>
    public string? CurrentVersion(ChangelogStream stream)
        => Releases(stream).FirstOrDefault()?.Version;

    /// <summary>What has been released for a project, all of its products together, newest first.</summary>
    public ImmutableArray<ChangelogRelease> Releases(ChangelogProject project)
        => Releases().Where(r => r.Stream.Project() == project).ToImmutableArray();

    /// <summary>The newest release date, or null when none is dated.</summary>
    /// <param name="includeAdministration">False for anybody but a SuperAdmin: a version they
    /// cannot see must not move the date they are told.</param>
    public DateOnly? LastChanged(ChangelogStream? stream = null, bool includeAdministration = false)
        => Releases(stream).Where(r => includeAdministration || r.IsPublic)
                           .Select(r => r.Date).FirstOrDefault(d => d is not null);

    private ImmutableArray<ChangelogRelease> LoadAll()
    {
        var all = new List<(ChangelogRelease Release, DateOnly SortDate)>();
        foreach (var stream in Enum.GetValues<ChangelogStream>())
        {
            var text = Read(stream);
            if (text is null) continue;

            // Within a product the file's order is the truth. To interleave every product on one
            // list, an undated version sorts with the nearest dated one below it — the version it
            // came after — so it never drops to the bottom of everything.
            var releases = Parse(stream, text).ToList();
            var sortDates = new DateOnly[releases.Count];
            var carried = DateOnly.MinValue;
            for (var n = releases.Count - 1; n >= 0; n--)
            {
                if (releases[n].Date is { } d) carried = d;
                sortDates[n] = releases[n].Unreleased ? DateOnly.MaxValue : carried;
            }
            all.AddRange(releases.Select((r, n) => (r, sortDates[n])));
        }

        // Newest first across all of them, then by product so a single day reads in a stable order.
        // OrderBy is stable, so a product's own versions keep the file's order within a day.
        return all
            .OrderByDescending(x => x.SortDate)
            .ThenBy(x => x.Release.Stream)
            .Select(x => x.Release)
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
    /// Reads the one shape these files are allowed to have: <c>## x.y.z</c> headings, optionally
    /// with the release date after a middle dot (<c>## 2.11.0 · 2026-09-28</c>), or one
    /// <c>## Unreleased</c> at the top — each followed by <c>- </c> lines, and optionally a
    /// <c>### Administration</c> sub-heading whose lines only SuperAdmins see.
    /// </summary>
    /// <remarks>
    /// Deliberately strict and deliberately silent about anything else. A heading it cannot read is
    /// skipped rather than guessed at — a version that is not three numbers, or a date that is not
    /// a date — and a test fails on any file with such a heading, so it never ships quietly
    /// missing. Everything before the first heading is the file's own preamble, including the rule
    /// about what may go in it.
    /// </remarks>
    internal static IEnumerable<ChangelogRelease> Parse(ChangelogStream stream, string markdown)
    {
        ChangelogRelease? current = null;
        var entries = new List<string>();
        var adminEntries = new List<string>();
        var target = entries;

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r').Trim();

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                if (current is not null && entries.Count + adminEntries.Count > 0)
                    yield return current with { Entries = [.. entries], AdminEntries = [.. adminEntries] };

                entries = [];
                adminEntries = [];
                target = entries;
                current = ReadHeading(stream, line[3..].Trim());
                continue;
            }

            if (current is null) continue;

            // Everything after "### Administration", to the next version, is for SuperAdmins. Any
            // other sub-heading is not one the page knows, so its lines are not read at all rather
            // than published under the wrong audience.
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                target = string.Equals(line[4..].Trim(), AdministrationHeading, StringComparison.OrdinalIgnoreCase)
                    ? adminEntries
                    : [];
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                var entry = line[2..].Trim();
                if (entry.Length > 0) target.Add(entry);
                continue;
            }

            // A wrapped entry continues on the next line. Without this the file has to be written
            // with one very long line per change, and the first person to let an editor wrap one
            // would silently publish half a sentence.
            if (line.Length > 0 && target.Count > 0 && !line.StartsWith('#'))
                target[^1] = target[^1] + " " + line;
        }

        if (current is not null && entries.Count + adminEntries.Count > 0)
            yield return current with { Entries = [.. entries], AdminEntries = [.. adminEntries] };
    }

    /// <summary>A heading, read; or null when it is not one of the shapes above.</summary>
    internal static ChangelogRelease? ReadHeading(ChangelogStream stream, string heading)
    {
        // "## Unreleased" collects what is merged and waiting. At release it becomes
        // "## x.y.z · yyyy-MM-dd", in the same commit that sets the product's version.
        if (string.Equals(heading, UnreleasedHeading, StringComparison.OrdinalIgnoreCase))
            return new ChangelogRelease(stream, null, null, [], Unreleased: true);

        var parts = heading.Split('·', 2, StringSplitOptions.TrimEntries);
        if (!ReleaseVersion.TryParse(parts[0], out var version)) return null;

        DateOnly? date = null;
        if (parts.Length > 1)
        {
            if (!DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                        DateTimeStyles.None, out var parsed))
                return null;
            date = parsed;
        }

        return new ChangelogRelease(stream, version.ToString(), date, []);
    }
}

/// <summary>
/// A release number: major.minor.patch.
/// </summary>
/// <remarks>
/// <para>Ben, 10/14/2026: "Major changes take a 1.0.0 to 2.0.0, not overhaul would take a 1.0.0 to
/// 1.1.0 and minor or everyday updates take a 1.0.0 to 1.0.1." Each product is numbered on its
/// own; the apps use the number on the App Store.</para>
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
