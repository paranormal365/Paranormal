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
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    /// <summary>Every symbol id in the shipped sprite.</summary>
    private static HashSet<string> SpriteSymbols()
    {
        var sprite = Path.Combine(
            RepoRoot().FullName, "Ben.Web.Website", "wwwroot", "icons", "sprite.svg");

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
        var named = new Regex("<BenIcon\\b[^>]*\\bName=\"([^\"@][^\"]*)\"");

        foreach (var file in RepoRoot().EnumerateFiles("*.razor", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
             || file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var text = File.ReadAllText(file.FullName);

            foreach (Match match in named.Matches(text))
            {
                var name = match.Groups[1].Value;
                if (!symbols.Contains(name))
                    missing.Add($"{file.Name}: \"{name}\"");
            }
        }

        Assert.True(missing.Count == 0,
            "these icons are not in /icons/sprite.svg, and a missing symbol renders as an empty "
          + "300×150 box rather than as nothing:\n  " + string.Join("\n  ", missing));
    }
}
