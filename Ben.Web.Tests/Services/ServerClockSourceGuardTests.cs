using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// No website source may read or convert through the server's clock.
/// </summary>
/// <remarks>
/// <para><b>What happened.</b> On 10/16/2026 Ben scheduled an investigation from Central for
/// 06:00 AM and the list showed 07:00 AM CDT. The window saved with <c>ToUniversalTime()</c>.
/// The site is Blazor Server: that code runs on the server, so it converted through the server's
/// zone (a fixed UTC-6 on production), not the reader's. The same mistake had seeded the check-in
/// time, the default start of the window, and several "today" defaults.</para>
///
/// <para><b>The rule.</b> A time somebody types is on their own clock
/// (<c>IBenUserState.ViewerTimeZone</c>) or on a clock the form names; it is stored as UTC; it is
/// shown converted. The helpers are <c>ToUtcFromViewerLocal</c>, <c>ToViewerLocalTime</c>,
/// <c>NowInViewerTimeZone</c>, <c>Zones.ToUtc</c>/<c>Zones.ToZone</c> and <c>EventClock</c>.
/// <c>DateTime.UtcNow</c> is always fine: it names no zone.</para>
///
/// <para><b>Comments are stripped first</b>, including Razor's <c>@* *@</c> blocks across lines.
/// Guards in this suite have fired on their own prose before, and the comments that explain this
/// rule necessarily spell out what it bans.</para>
/// </remarks>
public sealed class ServerClockSourceGuardTests
{
    private static readonly string[] Projects = ["Ben.Web.Website", "Ben.Web.Website.Library", "Ben.Web.Services"];

    /// <summary>Everything that reads the server machine's own zone.</summary>
    private static readonly (string Name, Regex Pattern)[] ServerClock =
    [
        (".ToUniversalTime()",  new Regex(@"\.ToUniversalTime\s*\(\s*\)")),
        (".ToLocalTime()",      new Regex(@"\.ToLocalTime\s*\(\s*\)")),
        ("DateTime.Now",        new Regex(@"\bDateTime\.Now\b")),
        ("DateTime.Today",      new Regex(@"\bDateTime\.Today\b")),
        ("DateTimeOffset.Now",  new Regex(@"\bDateTimeOffset\.Now\b")),
        ("TimeZoneInfo.Local",  new Regex(@"\bTimeZoneInfo\.Local\b")),
    ];

    /// <summary>
    /// Uses that are justified, by file (path relative to the repository root, forward slashes)
    /// and pattern name, each with its reason.
    /// </summary>
    /// <remarks>
    /// Empty on purpose: every use found on 10/16/2026 was either a bug or a placeholder that could
    /// name UTC instead. An entry added here must say why the server's clock is the right answer,
    /// and the guard fails if an entry no longer matches anything, so the list cannot rot.
    /// </remarks>
    private static readonly Dictionary<(string File, string Pattern), string> Allowed = new()
    {
    };

    /// <summary>Strips C#, Razor and block comments, keeping line numbers where they were.</summary>
    internal static string StripComments(string source)
    {
        static string Blank(Match m) => new(m.Value.Where(c => c == '\n').ToArray());

        var text = Regex.Replace(source, @"@\*.*?\*@", Blank, RegexOptions.Singleline);
        text = Regex.Replace(text, @"/\*.*?\*/", Blank, RegexOptions.Singleline);

        return string.Join('\n', text.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    /// <summary>Every use of the server's clock in <paramref name="source"/>, as (line, pattern).</summary>
    internal static IEnumerable<(int Line, string Pattern)> Offences(string source)
    {
        var text = StripComments(source);
        foreach (var (name, pattern) in ServerClock)
            foreach (Match m in pattern.Matches(text))
                yield return (text.Take(m.Index).Count(c => c == '\n') + 1, name);
    }

    private static IEnumerable<(string Relative, string Full)> Sources()
    {
        var root = RepoFiles.Root().FullName;
        var prefixes = Projects.Select(p => Path.Combine(root, p) + Path.DirectorySeparatorChar).ToArray();

        return RepoFiles.Paths("*.cs").Concat(RepoFiles.Paths("*.razor"))
            .Where(f => prefixes.Any(p => f.StartsWith(p, StringComparison.Ordinal)))
            .Select(f => (Path.GetRelativePath(root, f).Replace('\\', '/'), f));
    }

    [Fact]
    public void No_website_source_reads_the_servers_clock()
    {
        var offences = new List<string>();
        var seen = new HashSet<(string, string)>();
        var scanned = 0;

        foreach (var (relative, full) in Sources())
        {
            scanned++;
            foreach (var (line, pattern) in Offences(File.ReadAllText(full)))
            {
                if (Allowed.ContainsKey((relative, pattern))) { seen.Add((relative, pattern)); continue; }
                offences.Add($"{relative}:{line} — {pattern}");
            }
        }

        // A scan that read nothing proves nothing.
        Assert.True(scanned > 100, $"Only {scanned} source files were scanned; the guard cannot see the website.");

        Assert.True(offences.Count == 0,
            $"""
             {offences.Count} use(s) of the server's clock in website source.

             This site is Blazor Server: these run on the server and use ITS zone, not the reader's.
             A typed time is on the reader's clock (ToUtcFromViewerLocal / NowInViewerTimeZone) or
             on a clock the form names (Zones.ToUtc / Zones.ToZone). Store UTC; DateTime.UtcNow is
             always fine. If a use is genuinely right, add it to Allowed with its reason.

               {string.Join("\n  ", offences.Take(60))}
             """);

        var stale = Allowed.Keys.Where(k => !seen.Contains(k)).ToList();
        Assert.True(stale.Count == 0,
            $"Allowed entries that no longer match anything — remove them: {string.Join(", ", stale)}");
    }

    // ── The guard itself discriminates ───────────────────────────────────────────

    [Fact]
    public void It_catches_the_line_that_caused_the_bug()
    {
        const string bug = """
                           var request = new CreateOrgInvestigationRequest(
                               ScheduledDateTime: _when!.Value.ToUniversalTime(),
                               EndDateTime: EffectiveEnd()?.ToUniversalTime());
                           var t = DateTime.Now.AddDays(1);
                           """;

        var found = Offences(bug).ToList();

        Assert.Equal(3, found.Count);
        Assert.Contains((2, ".ToUniversalTime()"), found);
        Assert.Contains((3, ".ToUniversalTime()"), found);
        Assert.Contains((4, "DateTime.Now"), found);
    }

    [Fact]
    public void It_ignores_comments_and_the_viewer_helpers()
    {
        const string fine = """
                            @* Replaces .ToLocalTime(), which
                               on a server page is DateTime.Now's zone. *@
                            // never DateTime.Today
                            /* TimeZoneInfo.Local */
                            var now = UserState.NowInViewerTimeZone();
                            var utc = typed.ToUtcFromViewerLocal(UserState);
                            var stamp = DateTime.UtcNow;
                            """;

        Assert.Empty(Offences(fine));
    }

    [Fact]
    public void Stripping_a_multi_line_razor_comment_keeps_line_numbers()
    {
        const string source = "@* one\ntwo *@\nvar t = DateTime.Today;";
        Assert.Equal([(3, "DateTime.Today")], Offences(source).ToList());
    }
}
