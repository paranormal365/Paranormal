using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// An icon-only grid command button names itself in its own content, not only in its title.
/// </summary>
/// <remarks>
/// <para>The site-wide TelerikTooltip (MainLayout) moves a target's <c>title</c> aside while its tip
/// is showing — so a GridCommandButton whose only name WAS its title had no name at all for as long
/// as the pointer rested on it. The new-group journey accepted one applicant, the next row slid up
/// under the pointer, and its Accept button was a nameless <c>button</c> to the test and to a screen
/// reader alike (2026-10-01). It had failed that way since before the redesign.</para>
/// <para>BenGridAction carries an aria-label and is fine. A GridCommandButton cannot take one (an
/// attribute Telerik does not expect takes the circuit down, item 112), so its words go inside it,
/// visually hidden, beside the icon.</para>
/// </remarks>
public class GridCommandButtonsKeepTheirNamesTests
{
    [Fact]
    public void Every_icon_grid_command_button_carries_its_name_inside_it()
    {
        var root = RepoRoot();
        var nameless = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.razor", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains("worktrees")) continue;
            if (!file.Contains("Ben.Web.Website")) continue;

            var source = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(source, @"<GridCommandButton\b(?:(?!</GridCommandButton>).)*?</GridCommandButton>", RegexOptions.Singleline))
            {
                var button = m.Value;
                if (button.Contains("ben-grid-action") && !button.Contains("visually-hidden"))
                    nameless.Add($"{Path.GetFileName(file)}: {Regex.Match(button, "Title=\"[^\"]*\"").Value}");
            }
        }

        Assert.True(nameless.Count == 0,
            "These icon-only grid buttons are named only by their title, which the tooltip takes away while it shows. "
            + "Add <span class=\"visually-hidden\">the same words</span> beside the icon: " + string.Join("; ", nameless));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }
}
