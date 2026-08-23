using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Item 166: every tour step's selector must resolve in the razor that hosts the tour — a tour
/// pointing at a renamed element is a silently broken walkthrough, the same failure class the
/// HelpLink anchor guard exists for. Reads the SOURCE, so a rename fails the build's tests
/// before anyone runs the tour.
/// </summary>
public sealed class TourSelectorGuardTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!;
    }

    private static IEnumerable<(string File, string Selector)> AllTourSelectors()
    {
        var root = Path.Combine(RepoRoot().FullName, "Ben.Web.Website.Library");
        foreach (var file in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(source, @"TourStep\(\s*""([^""]+)"""))
                yield return (file, m.Groups[1].Value);
        }
    }

    [Fact]
    public void Every_tour_selector_resolves_in_the_razor_that_declares_it()
    {
        var found = AllTourSelectors().ToList();
        Assert.NotEmpty(found);   // the guard itself must be guarding something

        var broken = new List<string>();
        foreach (var (file, selector) in found)
        {
            var source = File.ReadAllText(file);
            var ok = selector.StartsWith("#tab-", StringComparison.Ordinal)
                // Tab ids come from BenTab Id="…" via BenTabs rendering id="tab-…".
                ? Regex.IsMatch(source, $@"Id=""{Regex.Escape(selector["#tab-".Length..])}""")
                : selector.StartsWith('#')
                    // A plain id must be a literal id="…" in the same file.
                    ? source.Contains($"id=\"{selector[1..]}\"", StringComparison.Ordinal)
                    // Non-id selectors are allowed but must at least name a class present here.
                    : selector.StartsWith('.')
                        && source.Contains(selector[1..], StringComparison.Ordinal);

            if (!ok) broken.Add($"{Path.GetFileName(file)}: {selector}");
        }

        Assert.True(broken.Count == 0,
            "These tour steps point at elements their razor does not contain — the walkthrough "
            + "is silently broken: " + string.Join("; ", broken));
    }
}
