using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// No website source may write a clock time with a padded hour for a person to read.
/// </summary>
/// <remarks>
/// <para><b>What happened.</b> On 10/16/2026 a hosted event's Program rows read
/// "06:00 PM – 08:00 PM CDT" and its What's On cards "07:00 PM", while the tiles at the top of
/// the same page said "7:00 PM". The shared patterns on <c>DateTimeViewerExtensions</c> padded the
/// hour, so everything that used them did, and the few places that typed their own pattern did
/// not. People write "7:00 PM".</para>
///
/// <para><b>The rule.</b> Clock times for people go through <c>ToDisplayTime</c>,
/// <c>ToDisplayDateTime</c> (and their <c>WithSeconds</c> versions) or <c>BenTime</c>, which read
/// "7:00 PM" and "10/16/2026 7:00 PM". The date stays MM/DD/YYYY. A padded or 24-hour hour is
/// right only where a machine reads it — an <c>&lt;input type="time"&gt;</c> value, a log line, a
/// sortable UTC stamp — and each of those is listed in <see cref="Allowed"/> with its reason.</para>
///
/// <para><b>Comments are stripped first</b>, including Razor's <c>@* *@</c> blocks across lines,
/// and the block-comment pattern is anchored so a file input's <c>accept="image/*,audio/*"</c> is
/// not read as a comment and swallow the code after it.</para>
/// </remarks>
public sealed class ClockHourSourceGuardTests
{
    private static readonly string[] Projects = ["Ben.Web.Website", "Ben.Web.Website.Library", "Ben.Web.Services"];

    /// <summary>A two-digit hour followed by minutes, 12-hour or 24-hour, in a DateTime or TimeSpan format.</summary>
    private static readonly (string Name, Regex Pattern)[] PaddedHour =
    [
        ("hh:mm", new Regex(@"(?<![A-Za-z])hh\\?:mm")),
        ("HH:mm", new Regex(@"(?<![A-Za-z])HH\\?:mm")),
    ];

    /// <summary>
    /// Uses where a machine reads the time, by file (path relative to the repository root, forward
    /// slashes) and pattern name: how many there are, and why. No display code is on this list.
    /// </summary>
    /// <remarks>
    /// The count is exact. One more in the same file is a new use nobody justified; one fewer, or
    /// none at all, and the entry is stale and the guard says so, so the list cannot rot.
    /// </remarks>
    internal static readonly Dictionary<(string File, string Pattern), (int Count, string Why)> Allowed = new()
    {
        [("Ben.Web.Website/Program.cs", "HH:mm")] =
            (1, "Serilog's console line template. A log line, read by whoever tails the host."),
        [("Ben.Web.Website.Library/SuperAdmin/AdminAuditLog.razor", "HH:mm")] =
            (2, "The audit record copied into an email, stamped in UTC as yyyy-MM-dd HH:mm so it matches the AuditLogs table it came from."),
        [("Ben.Web.Website.Library/Organization/NewInvestigationWindow.razor", "HH:mm")] =
            (1, "The value of an <input type=\"time\">, which the browser only accepts as 24-hour HH:mm."),
        [("Ben.Web.Website.Library/Manage/Events/OrgEventPage.razor", "hh:mm")] =
            (1, "A TimeSpan written into an <input type=\"time\"> value. A TimeSpan's hh is the 24-hour hour, which the input requires."),
    };

    /// <summary>Strips C#, Razor and block comments, keeping line numbers where they were.</summary>
    internal static string StripComments(string source)
    {
        static string Blank(Match m) => new(m.Value.Where(c => c == '\n').ToArray());

        var text = Regex.Replace(source, @"@\*.*?\*@", Blank, RegexOptions.Singleline);
        // Anchored: "/*" opens a comment only where it starts a token, not inside image/*.
        text = Regex.Replace(text, @"(?<![\w""'/*])/\*.*?\*/", Blank, RegexOptions.Singleline);

        return string.Join('\n', text.Split('\n').Select(line =>
        {
            // Not the "//" of an address.
            var slashes = Regex.Match(line, @"(?<!:)//");
            return slashes.Success ? line[..slashes.Index] : line;
        }));
    }

    /// <summary>Every padded hour in <paramref name="source"/>, as (line, pattern).</summary>
    internal static IEnumerable<(int Line, string Pattern)> Offences(string source)
    {
        var text = StripComments(source);
        foreach (var (name, pattern) in PaddedHour)
            foreach (Match m in pattern.Matches(text))
                yield return (text.Take(m.Index).Count(c => c == '\n') + 1, name);
    }

    /// <summary>
    /// Weighs what was found against an allow-list: what is not allowed, and what the list says
    /// that is no longer true.
    /// </summary>
    internal static (List<string> Offences, List<string> Stale) Judge(
        IEnumerable<(string Relative, string Source)> sources,
        IReadOnlyDictionary<(string File, string Pattern), (int Count, string Why)> allowed)
    {
        var offences = new List<string>();
        var found = new Dictionary<(string, string), List<int>>();

        foreach (var (relative, source) in sources)
            foreach (var (line, pattern) in Offences(source))
            {
                if (!found.TryGetValue((relative, pattern), out var lines)) found[(relative, pattern)] = lines = [];
                lines.Add(line);
            }

        foreach (var ((file, pattern), lines) in found)
        {
            if (allowed.TryGetValue((file, pattern), out var allow) && allow.Count == lines.Count) continue;

            var expected = allowed.TryGetValue((file, pattern), out var a) ? a.Count : 0;
            offences.AddRange(lines.Select(l => $"{file}:{l} — {pattern}"
                + (expected > 0 ? $" (Allowed lists {expected} in this file, found {lines.Count})" : "")));
        }

        var stale = allowed
            .Where(kv => !found.TryGetValue(kv.Key, out var lines) || lines.Count < kv.Value.Count)
            .Select(kv => $"{kv.Key.File} — {kv.Key.Pattern}")
            .ToList();

        return (offences, stale);
    }

    private static IEnumerable<(string Relative, string Source)> Sources()
    {
        var root = RepoFiles.Root().FullName;
        var prefixes = Projects.Select(p => Path.Combine(root, p) + Path.DirectorySeparatorChar).ToArray();

        return RepoFiles.Paths("*.cs").Concat(RepoFiles.Paths("*.razor")).Concat(RepoFiles.Paths("*.cshtml"))
            .Where(f => prefixes.Any(p => f.StartsWith(p, StringComparison.Ordinal)))
            .Select(f => (Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllText(f)));
    }

    [Fact]
    public void No_website_source_pads_the_hour_of_a_clock_time()
    {
        var sources = Sources().ToList();

        // A scan that read nothing proves nothing.
        Assert.True(sources.Count > 100, $"Only {sources.Count} source files were scanned; the guard cannot see the website.");

        var (offences, stale) = Judge(sources, Allowed);

        Assert.True(offences.Count == 0,
            $"""
             {offences.Count} padded hour(s) in website source.

             People read "7:00 PM", not "07:00 PM". Use ToDisplayTime / ToDisplayDateTime (or the
             WithSeconds versions) from DateTimeViewerExtensions, or <BenTime>. If a machine reads
             this time — an <input type="time"> value, a log line, a sortable UTC stamp — add it
             to Allowed with the count and the reason.

               {string.Join("\n  ", offences.Take(60))}
             """);

        Assert.True(stale.Count == 0,
            $"Allowed entries that no longer match what is there — fix or remove them: {string.Join(", ", stale)}");
    }

    [Fact]
    public void Every_allowed_use_says_why()
    {
        Assert.All(Allowed, kv =>
        {
            Assert.True(kv.Value.Count > 0, $"{kv.Key} allows nothing.");
            Assert.False(string.IsNullOrWhiteSpace(kv.Value.Why), $"{kv.Key} has no reason.");
        });
    }

    // ── The guard itself discriminates ───────────────────────────────────────────

    [Fact]
    public void It_catches_the_patterns_that_padded_the_program()
    {
        const string bug = """
                           public const string TimePattern = "hh:mm tt";
                           var when = $"{start:MM/dd/yyyy hh:mm tt} {zone}";
                           var posted = published.ToString("MMM d, yyyy, HH:mm");
                           var clock = span.ToString(@"hh\:mm");
                           """;

        var found = Offences(bug).ToList();

        Assert.Equal([(1, "hh:mm"), (2, "hh:mm"), (4, "hh:mm"), (3, "HH:mm")], found);
    }

    [Fact]
    public void It_leaves_the_unpadded_hour_comments_and_lookalikes_alone()
    {
        const string fine = """
                            @* Was "hh:mm tt", which printed
                               07:00 PM. *@
                            // never HH:mm here
                            /* hh:mm */
                            var t = local.ToString("h:mm tt");
                            var s = local.ToString("MM/dd/yyyy h:mm:ss tt");
                            var elapsed = ts.ToString(@"h\:mm\:ss");
                            var path = "https://example.com/hh:mm";
                            """;

        // The address is a string, not a format: the guard still sees it, which is right — it is
        // only the "//" that must not end the line early.
        Assert.Equal([(8, "hh:mm")], Offences(fine).ToList());
    }

    [Fact]
    public void A_file_inputs_accept_list_is_not_a_comment()
    {
        const string source = """
                              <input type="file" accept="image/*,audio/*,video/*" />
                              @when.ToString("hh:mm tt")
                              <p>*/</p>
                              """;

        Assert.Equal([(2, "hh:mm")], Offences(source).ToList());
    }

    [Fact]
    public void Stripping_a_multi_line_razor_comment_keeps_line_numbers()
    {
        const string source = "@* one\ntwo *@\nvar t = x.ToString(\"hh:mm tt\");";
        Assert.Equal([(3, "hh:mm")], Offences(source).ToList());
    }

    [Fact]
    public void An_empty_allow_list_reports_every_use()
    {
        var (offences, stale) = Judge(
            [("A.razor", "@x.ToString(\"hh:mm tt\")"), ("B.cs", "var v = t.ToString(\"HH:mm\");")],
            new Dictionary<(string, string), (int, string)>());

        Assert.Equal(["A.razor:1 — hh:mm", "B.cs:1 — HH:mm"], offences.Order());
        Assert.Empty(stale);
    }

    [Fact]
    public void An_allow_list_entry_that_matches_nothing_fails()
    {
        var (offences, stale) = Judge(
            [("A.razor", "@x.ToString(\"h:mm tt\")")],
            new Dictionary<(string, string), (int, string)> { [("A.razor", "hh:mm")] = (1, "was an input value") });

        Assert.Empty(offences);
        Assert.Equal(["A.razor — hh:mm"], stale);
    }

    [Fact]
    public void One_more_use_than_the_allow_list_counts_fails()
    {
        var (offences, _) = Judge(
            [("A.razor", "value=\"@t.ToString(\"HH:mm\")\"\n<span>@t.ToString(\"HH:mm\")</span>")],
            new Dictionary<(string, string), (int, string)> { [("A.razor", "HH:mm")] = (1, "an input value") });

        Assert.Equal(2, offences.Count);
        Assert.All(offences, o => Assert.Contains("Allowed lists 1 in this file, found 2", o));
    }
}
