using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A browser test that cannot fail is worse than no test: it reports coverage that is not there.
/// </summary>
/// <remarks>
/// <para><c>Assert.Pass</c> ends a test as passed. Used as a soft skip — "the element is not there
/// yet, pass" — it turns every future regression in that area into a green tick. The editor's
/// ffmpeg-status test looked for a class no element in the editor has and then passed
/// unconditionally, so it had never once exercised the thing it was named for; two audio tests
/// passed when an upload silently failed to produce a player (2026-09-05 audit, F19).</para>
///
/// <para>The distinction this guard enforces: a missing <i>precondition</i> (seed data absent, a
/// host not running, a feature switched off) is <c>Assert.Ignore</c>, which the run reports as
/// skipped and nobody mistakes for coverage. A missing <i>behaviour</i> is <c>Assert.Fail</c> or an
/// ordinary assertion.</para>
///
/// <para>Comments are stripped first, because several of the tests explain in prose why they no
/// longer call the thing they are named after — including this one's own subject.</para>
/// </remarks>
public sealed class PlaywrightTestsCanFailTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static string StripComments(string source)
    {
        var s = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join('\n', s.Split('\n').Select(line =>
        {
            var slashes = line.IndexOf("//", StringComparison.Ordinal);
            return slashes >= 0 ? line[..slashes] : line;
        }));
    }

    /// <summary>
    /// The soft passes that predate this rule, with the count each file still has.
    /// </summary>
    /// <remarks>
    /// <para>A ratchet rather than a clean sweep. Seventy-five of these were already in the suite
    /// when the rule was written, spread over sixteen files, and each one needs a judgement — is
    /// the thing it gives up on a precondition or the behaviour under test? — that belongs with
    /// whoever owns that area. What this list does is stop the number growing while they are
    /// worked through: a new file fails immediately, and an existing file fails as soon as it
    /// gains one.</para>
    ///
    /// <para>Numbers may only be lowered. When a file reaches zero, delete its entry.</para>
    /// </remarks>
    private static readonly Dictionary<string, int> LegacySoftPasses = new(StringComparer.Ordinal)
    {
        ["RequestStatusProgressionTests.cs"] = 12,
        ["CaseTransferTests.cs"]             = 11,
        ["MyCaseDashboardTests.cs"]          = 10,
        ["ClientRequestNavTests.cs"]         = 6,
        ["InvestigationPanelTests.cs"]       = 5,
        ["NavigationTests.cs"]               = 4,
        ["InvestigationReportTests.cs"]      = 4,
        ["HomeMapTests.cs"]                  = 4,
        ["PublishLeakWarningTests.cs"]       = 3,
        ["OrganizationTests.cs"]             = 3,
        ["CaseMessageBoardTests.cs"]         = 3,
        ["CaseNotesTests.cs"]                = 2,
        ["CaseManagerAssignmentTests.cs"]    = 2,
        ["OrdinaryMemberBaselineTests.cs"]   = 1,
        ["ClientRequestTests.cs"]            = 1,
        ["CaseManagementTests.cs"]           = 1,
    };

    private static Dictionary<string, int> CountSoftPassesPerFile()
    {
        var testsDir = new DirectoryInfo(Path.Combine(RepoRoot().FullName, "Ben.Web.Playwright"));
        Assert.True(testsDir.Exists, $"Expected the Playwright project at {testsDir.FullName}");

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(testsDir.FullName, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
             || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            var count = Regex.Matches(StripComments(File.ReadAllText(file)), @"\bAssert\.Pass\s*\(").Count;
            if (count > 0) counts[Path.GetFileName(file)] = count;
        }

        return counts;
    }

    [Fact]
    public void No_new_browser_test_ends_itself_as_passed()
    {
        var counts = CountSoftPassesPerFile();

        var newOffenders = counts.Keys
            .Where(file => !LegacySoftPasses.ContainsKey(file))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(newOffenders.Count == 0,
            "These browser tests end themselves as passed, so a regression in what they cover "
            + "reports green. Use Assert.Ignore for a missing precondition and Assert.Fail (or a "
            + "real assertion) for missing behaviour:\n  " + string.Join("\n  ", newOffenders));
    }

    [Fact]
    public void The_number_of_soft_passes_only_goes_down()
    {
        var counts = CountSoftPassesPerFile();

        var grown = LegacySoftPasses
            .Where(entry => counts.GetValueOrDefault(entry.Key) > entry.Value)
            .Select(entry => $"{entry.Key}: {counts[entry.Key]} now, {entry.Value} allowed")
            .ToList();

        Assert.True(grown.Count == 0,
            "These files gained soft passes. The allowance may only be lowered:\n  "
            + string.Join("\n  ", grown));

        var improved = LegacySoftPasses
            .Where(entry => counts.GetValueOrDefault(entry.Key) < entry.Value)
            .Select(entry => $"{entry.Key}: {counts.GetValueOrDefault(entry.Key)} now, {entry.Value} allowed")
            .ToList();

        Assert.True(improved.Count == 0,
            "Soft passes were removed — thank you. Lower (or delete) these entries in "
            + $"{nameof(LegacySoftPasses)} so the ratchet holds the new level:\n  "
            + string.Join("\n  ", improved));
    }

    /// <summary>
    /// The editor's own browser tests are held at zero: they are the ones this rule was written
    /// for, and they were fixed when it was.
    /// </summary>
    [Theory]
    [InlineData("VideoEditorTests.cs")]
    [InlineData("WasmEditorTests.cs")]
    [InlineData("AudioScrubModeTests.cs")]
    [InlineData("EditorHostFreshnessTests.cs")]
    public void The_editor_browser_tests_have_none(string fileName)
    {
        var counts = CountSoftPassesPerFile();

        Assert.False(counts.ContainsKey(fileName),
            $"{fileName} has {counts.GetValueOrDefault(fileName)} soft pass(es). The editor's "
            + "browser tests are the coverage the 2026-09-05 audit found missing; they must fail "
            + "when the editor breaks.");
    }
}
