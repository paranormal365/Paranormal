using System.Text.RegularExpressions;
using Ben.Web.Services.Changelog;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The public record of what changed.
/// </summary>
/// <remarks>
/// Two jobs here, and the second is the one that matters. The first is that the files parse into
/// what the page draws. The second is that the files stay publishable: they are rendered
/// anonymously, so a line naming a server, a table or the shape of a security fix would be a leak
/// shipped by whoever added it in a hurry — and a test is the only thing that will notice.
/// </remarks>
public sealed class ChangelogServiceTests
{
    private readonly ChangelogService _changelog = new();

    /// <summary>Released and unreleased alike: an unreleased line is public the day it ships.</summary>
    private IEnumerable<ChangelogRelease> Everything => _changelog.Releases().Concat(_changelog.Unreleased());

    /// <summary>
    /// Every line, with administration ones as well. Only SuperAdmins read those, but the page is
    /// still a page: no secrets, no internal references, whole sentences.
    /// </summary>
    private IEnumerable<(ChangelogRelease Release, string Entry)> EveryLine =>
        Everything.SelectMany(r => r.Entries.Concat(r.AdminEntries).Select(e => (r, e)));

    // ── The files themselves ─────────────────────────────────────────────────

    [Fact]
    public void EveryStreamHasContent()
    {
        foreach (var stream in Enum.GetValues<ChangelogStream>())
        {
            var releases = _changelog.Releases(stream);
            Assert.True(releases.Length > 0, $"{stream} has no entries — its file is missing or unreadable");
            Assert.All(releases, r => Assert.NotEmpty(r.Entries));
        }
    }

    [Theory]
    [MemberData(nameof(Products))]
    public void Every_heading_in_a_file_is_read(ChangelogStream stream)
    {
        // The parser skips a heading it cannot read rather than guess, so a typo — or a heading
        // left in the old "date · version" shape — would drop a whole version off the page without
        // a word. Every "## " line has to come back as a release or as Unreleased.
        var headings = Headings(stream);
        var unread = headings.Where(h => ChangelogService.ReadHeading(stream, h) is null).ToList();
        Assert.True(unread.Count == 0, $"{stream.Slug()}.md has headings that are not read: {string.Join(", ", unread)}");
        Assert.Equal(headings.Count, _changelog.Releases(stream).Length + _changelog.Unreleased(stream).Length);
    }

    [Fact]
    public void TheWholeListIsNewestFirst()
    {
        // The page draws them in the order they arrive, so the ordering is this class's promise
        // rather than the page's. Dated releases never go up the further down the list they are.
        var dates = _changelog.Releases().Where(r => r.Date is not null).Select(r => r.Date!.Value).ToList();
        Assert.Equal(dates.OrderByDescending(d => d).ToList(), dates);
    }

    [Fact]
    public void FilteringByStreamReturnsOnlyThatStream()
    {
        foreach (var stream in Enum.GetValues<ChangelogStream>())
            Assert.All(_changelog.Releases(stream), r => Assert.Equal(stream, r.Stream));
    }

    [Fact]
    public void NothingIsDatedInTheFuture()
    {
        // A changelog that announces tomorrow is a typo somebody will believe.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        Assert.All(_changelog.Releases().Where(r => r.Date is not null),
                   r => Assert.True(r.Date <= today, $"{r.Stream} {r.Version} is dated {r.Date}, in the future"));
    }

    // ── What may not appear on a public page ─────────────────────────────────

    /// <summary>
    /// Words that mean the line is describing the inside of the system rather than the outside.
    /// </summary>
    /// <remarks>
    /// Not a security control — anybody adding a line can work around a word list. It is a
    /// tripwire for the ordinary mistake, which is pasting a commit subject in and moving on.
    /// </remarks>
    public static readonly string[] Forbidden =
    [
        "localhost", "appsettings", "connection string", "password", "api key", "secret",
        "migration", "sql server", "database table", "stack trace", "refactor", "branch",
        ".cs", ".razor", ".swift", "nuget", "iis", "kestrel", "deploy script",
    ];

    [Fact]
    public void NoEntryTalksAboutTheInsideOfTheSystem()
    {
        foreach (var (day, entry) in EveryLine)
        {
            foreach (var word in Forbidden)
            {
                Assert.False(entry.Contains(word, StringComparison.OrdinalIgnoreCase),
                    $"{day.Stream} {day.Version ?? "Unreleased"}: \"{entry}\" mentions \"{word}\", which does not "
                    + "belong on a page a stranger reads");
            }
        }
    }

    /// <summary>
    /// Words that name an administration surface rather than something the reader can use.
    /// </summary>
    /// <remarks>
    /// Ben, 2026-09-21: <i>"On the changelog, don't show changes to administration pages or
    /// methods."</i> This page is read by somebody deciding whether to sign up. A line about the
    /// administration dashboard or a SuperAdmin screen tells them about a room they will never be
    /// in, and it pads a page whose whole value is that every line is about them.
    /// <para>Ben, 10/14/2026: such changes are tracked after all, under a version's
    /// <c>### Administration</c> heading, which only SuperAdmins see. So this holds for the lines
    /// everybody reads, and an administration change has somewhere to go.</para>
    /// </remarks>
    private static readonly string[] AdministrationWords =
    [
        "administrator", "administration", "superadmin", "super admin", "/admin",
    ];

    /// <summary>
    /// Entries written before the rule, with what each names.
    /// </summary>
    /// <remarks>
    /// A ratchet, not a rewrite. These are published lines a reader may have already seen, and
    /// silently editing a public changelog is worse than an old entry that does not meet a rule
    /// written after it. The list may only get shorter.
    /// </remarks>
    /// <remarks>
    /// Sixteen is the count on the day the rule was written, not a figure anybody chose. A ceiling
    /// picked by guesswork would either pass while the problem grew or fail on day one for no
    /// reason a reader could act on.
    /// </remarks>
    private static readonly int LegacyAdministrationEntries = 16;

    [Fact]
    public void NoEntryIsAboutAnAdministrationScreen()
    {
        var offenders = Everything
            .SelectMany(day => day.Entries.Select(entry => (day, entry)))
            .Where(pair => AdministrationWords.Any(
                w => pair.entry.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => $"{pair.day.Stream} {pair.day.Version ?? "Unreleased"}: \"{Short(pair.entry)}\"")
            .ToList();

        Assert.True(offenders.Count <= LegacyAdministrationEntries,
            $"{offenders.Count} entries name an administration surface, and the agreed ceiling is "
          + $"{LegacyAdministrationEntries}. The changelog is read by somebody deciding whether to "
          + "sign up, so a line about a screen they will never see is padding:\n  "
          + string.Join("\n  ", offenders));

        static string Short(string entry)
            => entry.Length <= 90 ? entry : entry[..90] + "…";
    }

    [Fact]
    public void NoEntryCarriesAnInternalItemNumberOrABranchName()
    {
        // "item 186 F5" and "feature/..." are how this work is talked about internally and mean
        // nothing to a reader. They are also the giveaway that a line came straight from a commit.
        var internalReference = new Regex(@"\bitem \d+|feature/|develop\b|master\b",
                                          RegexOptions.IgnoreCase);
        foreach (var (day, entry) in EveryLine)
                Assert.False(internalReference.IsMatch(entry),
                    $"{day.Stream} {day.Version ?? "Unreleased"}: \"{entry}\" reads as an internal reference");
    }

    [Fact]
    public void EveryEntryIsASentenceRatherThanAFragment()
    {
        foreach (var (_, entry) in EveryLine)
        {
            Assert.True(entry.Length >= 15, $"\"{entry}\" is too short to tell anybody anything");
            Assert.True(char.IsUpper(entry[0]) || char.IsDigit(entry[0]),
                $"\"{entry}\" should start as a sentence does");
            Assert.EndsWith(".", entry);
        }
    }

    // ── The parser ───────────────────────────────────────────────────────────

    [Fact]
    public void OnlyVersionHeadingsAndDashedLinesAreRead()
    {
        const string markdown = """
            # A preamble nobody renders

            Some rules about what may go in here.

            ## 1.1.0 · 2026-09-12
            - The first thing.
            - The second thing.

            ## 1.0.0
            - An older thing.
            """;

        var releases = ChangelogService.Parse(ChangelogStream.WebApp, markdown).ToList();

        Assert.Equal(2, releases.Count);
        Assert.Equal("1.1.0", releases[0].Version);
        Assert.Equal(new DateOnly(2026, 9, 12), releases[0].Date);
        Assert.Equal(["The first thing.", "The second thing."], releases[0].Entries.ToArray());
        Assert.Equal("1.0.0", releases[1].Version);
        Assert.Null(releases[1].Date);
        Assert.Equal(["An older thing."], releases[1].Entries.ToArray());
    }

    [Theory]
    [InlineData("Coming soon")]
    [InlineData("2026-09-12 · 1.0.0")]   // the old shape, date first
    [InlineData("2026-09-12")]
    [InlineData("2.1")]                  // half a number
    [InlineData("2.1.0 · last Tuesday")] // a date that is not one
    public void A_heading_that_is_not_a_version_is_skipped_rather_than_guessed_at(string heading)
    {
        // A changelog that invents a version is worse than one missing one; the file tests above
        // make sure no such heading ships.
        var markdown = $"## {heading}\n- Something.\n\n## 1.0.0\n- Something real.\n";

        var releases = ChangelogService.Parse(ChangelogStream.Api, markdown).ToList();

        Assert.Single(releases);
        Assert.Equal(["Something real."], releases[0].Entries.ToArray());
    }

    [Fact]
    public void AWrappedEntryIsJoinedRatherThanTruncated()
    {
        // These files are written by hand and an editor wraps long lines. Reading only the first
        // line would publish half a sentence, and nobody would notice until a reader did.
        const string markdown = """
            ## 1.0.0
            - One upload carries five minutes of video,
              and five hundred megabytes in total.
            - A second, unwrapped one.
            """;

        var releases = ChangelogService.Parse(ChangelogStream.Api, markdown).ToList();

        Assert.Equal(
            ["One upload carries five minutes of video, and five hundred megabytes in total.",
             "A second, unwrapped one."],
            releases[0].Entries.ToArray());
    }

    [Fact]
    public void AVersionWithNoEntriesIsNotShown()
    {
        const string markdown = """
            ## 1.0.1

            ## 1.0.0
            - A real one.
            """;

        var releases = ChangelogService.Parse(ChangelogStream.Ios, markdown).ToList();

        Assert.Single(releases);
        Assert.Equal("1.0.0", releases[0].Version);
    }

    [Fact]
    public void LastChangedIsTheNewestDateForThatStream()
    {
        foreach (var stream in Enum.GetValues<ChangelogStream>())
        {
            var dated = _changelog.Releases(stream).FirstOrDefault(r => r.Date is not null);
            Assert.Equal(dated?.Date, _changelog.LastChanged(stream, includeAdministration: true));
        }

        Assert.Equal(_changelog.Releases().First(r => r.Date is not null).Date,
                     _changelog.LastChanged(includeAdministration: true));
    }

    [Fact]
    public void An_undated_version_sorts_with_the_one_it_came_after_on_the_combined_list()
    {
        // Within a product the file's order is the truth. On the combined list an undated version
        // must not fall below everything else just because it has no date.
        Assert.All(Enum.GetValues<ChangelogStream>(), stream =>
            Assert.Equal(_changelog.Releases(stream).Select(r => r.Version),
                         _changelog.Releases().Where(r => r.Stream == stream).Select(r => r.Version)));
    }

    // ── Versions ────────────────────────────────────────────────────────────────
    // Ben, 10/14/2026: "Major changes take a 1.0.0 to 2.0.0, not overhaul would take a 1.0.0 to
    // 1.1.0 and minor or everyday updates take a 1.0.0 to 1.0.1. The date is a bonus, but not
    // tracked like versioning anymore."

    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.1", "1.1.0", true)]
    [InlineData("1.9.3", "2.0.0", true)]
    [InlineData("1.0.0", "1.0.2", false)]   // a skipped number
    [InlineData("1.1.4", "1.2.4", false)]   // a minor bump resets the patch
    [InlineData("2.0.0", "1.9.9", false)]
    public void Each_release_is_one_step_on(string from, string to, bool oneStep)
    {
        Assert.True(ReleaseVersion.TryParse(from, out var a));
        Assert.True(ReleaseVersion.TryParse(to, out var b));
        Assert.Equal(oneStep, a.IsNextStep(b));
    }

    [Theory]
    [MemberData(nameof(Products))]
    public void Every_product_starts_at_one_and_each_version_is_one_step_after_the_last(ChangelogStream stream)
    {
        // Oldest first: the file lists them newest first.
        var released = _changelog.Releases(stream).Reverse().ToList();
        Assert.NotEmpty(released);
        Assert.Equal("1.0.0", released[0].Version);

        for (var n = 1; n < released.Count; n++)
        {
            ReleaseVersion.TryParse(released[n - 1].Version, out var before);
            ReleaseVersion.TryParse(released[n].Version, out var after);
            Assert.True(before.IsNextStep(after),
                $"{stream.Slug()}.md: {after} is not one step after {before} — everyday updates and fixes " +
                "are the next patch, something new the next minor, a major change the next major");
        }
    }

    // ── Administration: SuperAdmins only (Ben, 10/14/2026) ─────────────────────

    [Fact]
    public void Administration_lines_are_kept_apart_from_everybody_s()
    {
        const string md = """
            ## 1.1.0
            - Something for everybody.

            ### Administration
            - The place duplicates screen can merge three at once,
              not only two.

            ## 1.0.1

            ### Administration
            - Only an administration change.

            ## 1.0.0

            ### Notes
            - A sub-heading the page does not know is not read.
            - Nor is anything under it.
            """;

        var releases = ChangelogService.Parse(ChangelogStream.WebApp, md).ToList();

        Assert.Equal(["1.1.0", "1.0.1"], releases.Select(r => r.Version).ToArray());
        Assert.Equal(["Something for everybody."], releases[0].Entries.ToArray());
        Assert.Equal(["The place duplicates screen can merge three at once, not only two."], releases[0].AdminEntries.ToArray());
        Assert.True(releases[0].IsPublic);

        // Numbered like any other version, and invisible to everybody but a SuperAdmin.
        Assert.Empty(releases[1].Entries);
        Assert.False(releases[1].IsPublic);
    }

    [Theory]
    [MemberData(nameof(Products))]
    public void The_only_sub_heading_is_Administration(ChangelogStream stream)
    {
        // Lines under any other sub-heading are not read, so a misspelt one would hide them.
        var file = File.ReadAllText(Path.Combine(RepoRoot(), "Ben.Web.Services", "Changelog", "Content", $"{stream.Slug()}.md"));
        var others = file.Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("### ", StringComparison.Ordinal))
            .Where(l => !string.Equals(l[4..].Trim(), ChangelogService.AdministrationHeading, StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(others.Count == 0, $"{stream.Slug()}.md: {string.Join(", ", others)}");
    }

    [Fact]
    public void A_version_nobody_else_can_see_does_not_move_the_date_they_are_told()
    {
        Assert.All(Enum.GetValues<ChangelogStream>(), stream =>
            Assert.Equal(_changelog.Releases(stream).Where(r => r.IsPublic).Select(r => r.Date).FirstOrDefault(d => d is not null),
                         _changelog.LastChanged(stream)));
    }

    // ── Unreleased, and the version in the build (10/14/2026, as on benclark.net) ──

    [Fact]
    public void Unreleased_work_is_kept_apart_from_what_was_released()
    {
        const string md = "# x\n\n## Unreleased\n\n- Coming.\n\n## 2.20.0 · 2026-10-13\n\n- Out.\n";
        var releases = ChangelogService.Parse(ChangelogStream.WebApp, md).ToList();
        Assert.True(releases[0].Unreleased);
        Assert.Equal(["Coming."], releases[0].Entries.ToArray());
        Assert.Null(releases[0].Version);
        Assert.False(releases[1].Unreleased);
        Assert.Equal("2.20.0", releases[1].Version);
    }

    [Fact]
    public void The_public_list_never_carries_unreleased_work()
    {
        Assert.All(_changelog.Releases(), r => Assert.False(r.Unreleased));
        Assert.All(_changelog.Unreleased(), r => Assert.True(r.Unreleased));
    }

    [Theory]
    [MemberData(nameof(Products))]
    public void Unreleased_is_one_heading_and_it_is_the_first(ChangelogStream stream)
    {
        // Two would leave a release to rename the wrong one; one further down would hide under
        // releases that came after it.
        var headings = Headings(stream);
        var unreleased = headings.Count(h => string.Equals(h, ChangelogService.UnreleasedHeading, StringComparison.OrdinalIgnoreCase));
        Assert.True(unreleased <= 1, $"{stream.Slug()}.md has {unreleased} Unreleased headings");
        if (unreleased == 1)
            Assert.Equal(ChangelogService.UnreleasedHeading, headings[0]);
    }

    [Theory]
    [InlineData(ChangelogStream.WebApp, "Ben.Web.Website/Ben.Web.Website.csproj")]
    [InlineData(ChangelogStream.Api, "Ben.Data.WebApi/Ben.Data.WebApi.csproj")]
    [InlineData(ChangelogStream.Canvas, "Ben.Canvas.Editor/Ben.Canvas.Editor.csproj")]
    [InlineData(ChangelogStream.VideoEditor, "Ben.Video.Editor/Ben.Video.Editor.csproj")]
    [InlineData(ChangelogStream.Sidecar, "Ben.Video.Sidecar/Ben.Video.Sidecar.csproj")]
    public void The_build_carries_the_newest_released_version(ChangelogStream stream, string project)
    {
        // What the build reports is what What's new says was released last. A release changes both
        // in one commit; a project still on the old number, or one bumped without its heading,
        // fails here.
        var xml = System.Xml.Linq.XDocument.Load(Path.Combine(RepoRoot(), project));
        var version = xml.Descendants("Version").Select(v => v.Value.Trim()).FirstOrDefault();
        Assert.Equal(_changelog.CurrentVersion(stream), version);
    }

    [Fact]
    public void The_audio_editor_carries_the_newest_released_version()
    {
        // It lives in the website's library rather than a project of its own, so its number is a
        // constant beside its code.
        Assert.Equal(Ben.Web.Website.Library.Manage.Audio.AudioEditorRelease.Version,
                     _changelog.CurrentVersion(ChangelogStream.AudioEditor));
    }

    [Fact]
    public void The_CMS_carries_the_newest_released_version()
        => Assert.Equal(_changelog.CurrentVersion(ChangelogStream.Cms),
                        Ben.Web.Website.Library.Organization.Cms.CmsRelease.Version);

    [Fact]
    public void The_website_is_the_web_app_its_three_editors_and_the_CMS()
    {
        Assert.Equal([ChangelogStream.WebApp, ChangelogStream.Canvas, ChangelogStream.VideoEditor, ChangelogStream.AudioEditor, ChangelogStream.Cms],
                     ChangelogProject.Website.Streams().ToArray());
        Assert.Equal([ChangelogStream.Api], ChangelogProject.Api.Streams().ToArray());
        Assert.Equal([ChangelogStream.Ios], ChangelogProject.Ios.Streams().ToArray());
        Assert.Equal([ChangelogStream.Sidecar], ChangelogProject.Sidecar.Streams().ToArray());
        Assert.All(_changelog.Releases(ChangelogProject.Website),
                   r => Assert.Equal(ChangelogProject.Website, r.Stream.Project()));
    }

    [Fact]
    public void The_app_carries_the_newest_released_version()
    {
        // MARKETING_VERSION is what the App Store and Settings → About show. Every configuration
        // must agree, or a release build and a debug build would claim different versions.
        var project = File.ReadAllText(Path.Combine(RepoRoot(), "Ben.iOS", "IsHaunted.xcodeproj", "project.pbxproj"));
        var versions = Regex.Matches(project, @"MARKETING_VERSION = ([0-9.]+);")
                            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Equal([_changelog.CurrentVersion(ChangelogStream.Ios)!], versions.ToArray());
    }

    public static TheoryData<ChangelogStream> Products()
    {
        var data = new TheoryData<ChangelogStream>();
        foreach (var stream in Enum.GetValues<ChangelogStream>()) data.Add(stream);
        return data;
    }

    /// <summary>Every "## " heading in a product's file, as written, in order.</summary>
    private static List<string> Headings(ChangelogStream stream) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "Ben.Web.Services", "Changelog", "Content", $"{stream.Slug()}.md"))
            .Split('\n').Select(l => l.Trim())
            .Where(l => l.StartsWith("## ", StringComparison.Ordinal))
            .Select(l => l[3..].Trim()).ToList();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void An_entry_draws_its_bold_and_italics_and_never_markup_of_its_own()
    {
        Assert.Equal("Press <strong>Local time &#183; My time</strong> to see <em>your</em> clock in <code>/changes</code>.",
            ChangelogText.ToHtml("Press **Local time · My time** to see *your* clock in `/changes`."));
        Assert.Equal("&lt;script&gt;alert(1)&lt;/script&gt; <strong>still bold</strong>",
            ChangelogText.ToHtml("<script>alert(1)</script> **still bold**"));
        Assert.Equal("3 * 4 = 12", ChangelogText.ToHtml("3 * 4 = 12"));
    }
}
