using Ben.Web.Tests.Support;
using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every icon named in the markup is a symbol the sprite actually has.
/// </summary>
/// <remarks>
/// <para><b>What went wrong.</b> A pass button asked for <c>qrcode</c>, which is a Bootstrap icon
/// name and not a Feather one, so the sprite had no such symbol. Nothing failed: <c>&lt;use&gt;</c>
/// with an href that resolves to nothing renders an empty <c>&lt;svg&gt;</c>, and an
/// <c>&lt;svg&gt;</c> with no intrinsic size and no CSS size falls back to the SVG default of
/// 300×150 pixels. A list of seven bookings came out twenty-two thousand pixels tall, and the
/// only reason anybody noticed was that a screenshot looked absurd (item 235 phase 6).</para>
///
/// <para><b>Why a source scan.</b> The fault is silent by construction — no exception, no console
/// error, no failing render test, because the markup is perfectly valid and so is the SVG. What is
/// being held is "this name exists over there", and only something that reads both can hold
/// it.</para>
///
/// <para><b>Only literal names.</b> <c>Name="@Badge.Icon"</c> is computed and cannot be checked
/// here; the switch that produces it is ordinary code with ordinary tests. Every literal in the
/// codebase can be, and literals are where this mistake gets made.</para>
/// </remarks>
public sealed class IconNameGuardTests
{
    /// <summary>Every symbol id in the shipped sprite.</summary>
    private static HashSet<string> SpriteSymbols()
    {
        var sprite = Path.Combine(
            RepoFiles.Root().FullName, "Ben.Web.Website", "wwwroot", "icons", "sprite.svg");

        Assert.True(File.Exists(sprite), $"the icon sprite is missing: {sprite}");

        return Regex.Matches(File.ReadAllText(sprite), @"<symbol[^>]*\bid=""([^""]+)""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void Every_named_icon_exists_in_the_sprite()
    {
        var symbols = SpriteSymbols();
        var missing = new List<string>();

        // A literal name only: Name="@something" is computed, and the switch that produces it is
        // ordinary code with ordinary tests.
        // BenGridAction takes the same sprite name as Icon (grid row actions, 2026-09-14).
        var named = new Regex("<BenIcon\\b[^>]*\\bName=\"([^\"@][^\"]*)\"|<BenGridAction\\b[^>]*\\bIcon=\"([^\"@][^\"]*)\"");

        // RepoFiles, not a walk of its own: the walk this used to do also read the other branches
        // checked out under .claude/worktrees, and failed this branch for icons a stale copy of
        // another one still named.
        var files = RepoFiles.Paths("*.razor");
        Assert.True(files.Length > 100, $"only {files.Length} .razor files were found — a guard that reads nothing proves nothing");

        foreach (var path in files)
        {
            foreach (Match match in named.Matches(File.ReadAllText(path)))
            {
                var name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                if (!symbols.Contains(name))
                    missing.Add($"{Path.GetFileName(path)}: \"{name}\"");
            }
        }

        Assert.True(missing.Count == 0,
            "these icons are not in /icons/sprite.svg, and a missing symbol renders as an empty "
          + "300×150 box rather than as nothing:\n  " + string.Join("\n  ", missing));
    }
    /// <summary>
    /// And the board-template icons, which the scan above cannot see: the picker renders
    /// <c>Name="@template.IconName"</c>, so the literals live in a list rather than in markup.
    /// </summary>
    [Fact]
    public void Every_board_template_icon_exists_in_the_sprite()
    {
        var symbols = SpriteSymbols();

        var missing = Ben.Web.Website.Library.Manage.Messenger.CanvasBoardTemplates.All
            .Where(t => !symbols.Contains(t.IconName))
            .Select(t => $"{t.Id}: {t.IconName}")
            .ToList();

        Assert.True(missing.Count == 0,
            "these board-template icons are not in /icons/sprite.svg: " + string.Join(", ", missing));
    }
}
