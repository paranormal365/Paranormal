using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// Every <c>for=</c> on a label has to name a control that exists.
/// </summary>
/// <remarks>
/// A label pointing at nothing is invisible: it looks right, reads right to a sighted user, and
/// simply does not work — clicking it does not focus or toggle anything, and a screen reader
/// announces the control unlabelled. Four checkboxes in the CMS section editor were in exactly
/// that state, which is why this check exists rather than being assumed.
/// </remarks>
public class LabelAssociationTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    private static IEnumerable<string> Components()
    {
        var root = RepoRoot().FullName;
        foreach (var dir in new[] { "Ben.Web.Website", "Ben.Web.Website.Library" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(root, dir), "*.razor", SearchOption.AllDirectories))
                yield return file;
    }

    [Fact]
    public void Every_label_for_target_names_a_control_that_exists()
    {
        var root = RepoRoot().FullName;
        var scanned = 0;
        var orphans = new List<string>();

        foreach (var file in Components())
        {
            var text = File.ReadAllText(file);
            scanned++;

            var ids = new HashSet<string>(
                Regex.Matches(text, @"\bid=""([^""]+)""").Select(m => m.Groups[1].Value),
                StringComparer.Ordinal);

            foreach (Match m in Regex.Matches(text, @"\bfor=""([^""]+)"""))
            {
                var target = m.Groups[1].Value;
                // Skip ids built from a Razor expression — those cannot be resolved statically.
                if (target.Contains('@')) continue;
                if (!ids.Contains(target))
                    orphans.Add($"{Path.GetRelativePath(root, file)} :: for=\"{target}\"");
            }
        }

        Assert.True(scanned > 100, $"only {scanned} components were scanned — has the layout moved?");
        Assert.True(orphans.Count == 0,
            "These labels point at controls that do not exist, so clicking them does nothing and a "
            + "screen reader announces the control unlabelled:\n  " + string.Join("\n  ", orphans));
    }
}
