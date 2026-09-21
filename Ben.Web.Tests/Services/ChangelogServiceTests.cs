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

    // ── The files themselves ─────────────────────────────────────────────────

    [Fact]
    public void EveryStreamHasContent()
    {
        foreach (var stream in Enum.GetValues<ChangelogStream>())
        {
            var days = _changelog.Days(stream);
            Assert.True(days.Length > 0, $"{stream} has no entries — its file is missing or unreadable");
            Assert.All(days, day => Assert.NotEmpty(day.Entries));
        }
    }

    [Fact]
    public void TheWholeListIsNewestFirst()
    {
        // The page draws them in the order they arrive, so the ordering is this class's promise
        // rather than the page's.
        var dates = _changelog.Days().Select(d => d.Date).ToList();
        Assert.Equal(dates.OrderByDescending(d => d).ToList(), dates);
    }

    [Fact]
    public void FilteringByStreamReturnsOnlyThatStream()
    {
        foreach (var stream in Enum.GetValues<ChangelogStream>())
            Assert.All(_changelog.Days(stream), day => Assert.Equal(stream, day.Stream));
    }

    [Fact]
    public void NothingIsDatedInTheFuture()
    {
        // A changelog that announces tomorrow is a typo somebody will believe.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        Assert.All(_changelog.Days(), day => Assert.True(day.Date <= today, $"{day.Date} is in the future"));
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
        foreach (var day in _changelog.Days())
        {
            foreach (var entry in day.Entries)
            {
                foreach (var word in Forbidden)
                {
                    Assert.False(entry.Contains(word, StringComparison.OrdinalIgnoreCase),
                        $"{day.Stream} {day.Date}: \"{entry}\" mentions \"{word}\", which does not "
                        + "belong on a page a stranger reads");
                }
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
        var offenders = _changelog.Days()
            .SelectMany(day => day.Entries.Select(entry => (day, entry)))
            .Where(pair => AdministrationWords.Any(
                w => pair.entry.Contains(w, StringComparison.OrdinalIgnoreCase)))
            .Select(pair => $"{pair.day.Stream} {pair.day.Date}: \"{Short(pair.entry)}\"")
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
        foreach (var day in _changelog.Days())
            foreach (var entry in day.Entries)
                Assert.False(internalReference.IsMatch(entry),
                    $"{day.Stream} {day.Date}: \"{entry}\" reads as an internal reference");
    }

    [Fact]
    public void EveryEntryIsASentenceRatherThanAFragment()
    {
        foreach (var day in _changelog.Days())
        {
            foreach (var entry in day.Entries)
            {
                Assert.True(entry.Length >= 15, $"\"{entry}\" is too short to tell anybody anything");
                Assert.True(char.IsUpper(entry[0]) || char.IsDigit(entry[0]),
                    $"\"{entry}\" should start as a sentence does");
                Assert.EndsWith(".", entry);
            }
        }
    }

    // ── The parser ───────────────────────────────────────────────────────────

    [Fact]
    public void OnlyDatedHeadingsAndDashedLinesAreRead()
    {
        const string markdown = """
            # A preamble nobody renders

            Some rules about what may go in here.

            ## 2026-09-12
            - The first thing.
            - The second thing.

            ## 2026-09-01
            - An older thing.
            """;

        var days = ChangelogService.Parse(ChangelogStream.Website, markdown).ToList();

        Assert.Equal(2, days.Count);
        Assert.Equal(new DateOnly(2026, 9, 12), days[0].Date);
        Assert.Equal(["The first thing.", "The second thing."], days[0].Entries.ToArray());
        Assert.Equal(["An older thing."], days[1].Entries.ToArray());
    }

    [Fact]
    public void AHeadingThatIsNotADateIsSkippedRatherThanGuessedAt()
    {
        // A changelog that invents a date is worse than one missing a day.
        const string markdown = """
            ## Coming soon
            - Something unreleased.

            ## 2026-09-12
            - Something real.
            """;

        var days = ChangelogService.Parse(ChangelogStream.Api, markdown).ToList();

        Assert.Single(days);
        Assert.Equal(["Something real."], days[0].Entries.ToArray());
    }

    [Fact]
    public void AWrappedEntryIsJoinedRatherThanTruncated()
    {
        // These files are written by hand and an editor wraps long lines. Reading only the first
        // line would publish half a sentence, and nobody would notice until a reader did.
        const string markdown = """
            ## 2026-09-12
            - One upload carries five minutes of video,
              and five hundred megabytes in total.
            - A second, unwrapped one.
            """;

        var days = ChangelogService.Parse(ChangelogStream.Api, markdown).ToList();

        Assert.Equal(
            ["One upload carries five minutes of video, and five hundred megabytes in total.",
             "A second, unwrapped one."],
            days[0].Entries.ToArray());
    }

    [Fact]
    public void ADayWithNoEntriesIsNotShown()
    {
        const string markdown = """
            ## 2026-09-12

            ## 2026-09-11
            - A real one.
            """;

        var days = ChangelogService.Parse(ChangelogStream.Apps, markdown).ToList();

        Assert.Single(days);
        Assert.Equal(new DateOnly(2026, 9, 11), days[0].Date);
    }

    [Fact]
    public void LastChangedIsTheNewestDateForThatStream()
    {
        foreach (var stream in Enum.GetValues<ChangelogStream>())
        {
            var days = _changelog.Days(stream);
            Assert.Equal(days[0].Date, _changelog.LastChanged(stream));
        }

        Assert.Equal(_changelog.Days()[0].Date, _changelog.LastChanged());
    }
}
