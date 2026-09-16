using System.Text.RegularExpressions;
using Ben.Web.Tests.Support;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A button in a grid is an icon with its words in a tooltip, never a word.
/// </summary>
/// <remarks>
/// <para>
/// Ben, 2026-09-14: "When able, don't use text in buttons in a grid. Use icon buttons with a tooltip." Text
/// verbs side by side made every Actions column a paragraph — wide when there was room, wrapped when there was
/// not (item 139). The sweep that day turned ~90 of them into <c>Kit/BenGridAction</c> or a
/// <c>GridCommandButton</c> with a <c>BenIcon</c> child and a <c>Title</c>, and one <c>TelerikTooltip</c> in the
/// layout shows the words. This guard is what keeps the next grid from bringing a text button back.
/// </para>
/// <para>
/// "When able" is the allowlist: a few buttons in grids carry words that ARE the content — a list of screens to
/// open, a count of plan areas, the export buttons above a grid, a two-way choice inside an inline prompt. Each
/// entry says why. Anything else fails with the file and the button.
/// </para>
/// </remarks>
public sealed class GridRowActionsAreIconsTests
{
    /// <summary>(file name, a substring of the button) → why its words stay.</summary>
    private static readonly (string File, string Marker, string Why)[] KeepsItsWords =
    [
        ("AdminEvents.razor", "@screen.Label", "a detail row listing every screen of the event by name"),
        ("AdminEvents.razor", "PhotoWall(", "part of the same list of screens"),
        ("AdminEvents.razor", "PublicPage(", "part of the same list of screens"),
        ("AdminSubscriptionTiers.razor", "includes-", "the button's words are the count of areas the band includes"),
        ("AdminAuditLog.razor", "ExportToExcel", "a toolbar button above the grid, not a row action"),
        ("AdminAuditLog.razor", "ExportToCsv", "a toolbar button above the grid, not a row action"),
        ("OrganizationMembers.razor", "AcceptOfferAsync", "one of two answers to a question asked inline in the row"),
        ("OrganizationMembers.razor", "_offer = null", "one of two answers to a question asked inline in the row"),
    ];

    private static readonly Regex Grid = new(@"<TelerikGrid\b.*?</TelerikGrid>", RegexOptions.Singleline);

    private static readonly Regex CommandButton = new(
        @"<GridCommandButton\b((?:[^>""]|""[^""]*"")*?)(?:/>|>(.*?)</GridCommandButton>)", RegexOptions.Singleline);

    private static readonly Regex PlainButton = new(
        @"<(button|a|TelerikButton)\b((?:[^>""]|""[^""]*"")*)>(.*?)</\1>", RegexOptions.Singleline);

    private static string StripComments(string text) =>
        Regex.Replace(Regex.Replace(text, @"@\*.*?\*@", "", RegexOptions.Singleline), @"<!--.*?-->", "",
            RegexOptions.Singleline);

    /// <summary>What a person would read on the button: tags and Razor expressions removed.</summary>
    private static string VisibleText(string inner) =>
        Regex.Replace(Regex.Replace(inner, @"<[^>]+>", " "), @"\s+", " ").Trim();

    [Fact]
    public void Buttons_in_grids_are_icons_with_a_title()
    {
        // The site's own screens. Ben.Video.Editor is a vendored product with its own design language and no
        // reference to the Kit, so its project list keeps Telerik's buttons.
        var files = RepoFiles.Paths("*.razor")
            .Where(p => !p.Replace('\\', '/').Contains("/Ben.Video.Editor/", StringComparison.Ordinal))
            .ToArray();
        Assert.True(files.Length > 100, $"only {files.Length} .razor files were found — a guard that reads nothing proves nothing");

        var problems = new List<string>();
        var checkedButtons = 0;

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            var source = StripComments(File.ReadAllText(path));

            foreach (Match grid in Grid.Matches(source))
            {
                foreach (Match cmd in CommandButton.Matches(grid.Value))
                {
                    checkedButtons++;
                    var attrs = cmd.Groups[1].Value;
                    var text = VisibleText(cmd.Groups[2].Value);
                    if (!Regex.IsMatch(attrs, @"\bTitle="""))
                        problems.Add($"{name}: a GridCommandButton has no Title, so it has no tooltip and no accessible name");
                    if (text.Length > 0)
                        problems.Add($"{name}: a GridCommandButton says \"{text}\" — use a BenIcon child and put the words in Title");
                }

                foreach (Match button in PlainButton.Matches(grid.Value))
                {
                    var tag = button.Groups[1].Value;
                    var attrs = button.Groups[2].Value;
                    if (tag == "a" && !Regex.IsMatch(attrs, @"class=""[^""]*\bbtn\b")) continue;   // an ordinary link in a cell
                    if (Regex.IsMatch(attrs, @"class=""[^""]*\bdropdown-item\b")) continue;       // a line in a More-actions menu

                    checkedButtons++;
                    if (KeepsItsWords.Any(k => k.File == name && button.Value.Contains(k.Marker, StringComparison.Ordinal)))
                        continue;

                    var shown = Regex.Replace(button.Value, @"\s+", " ");
                    problems.Add($"{name}: <{tag}> in a grid — use <BenGridAction Icon=… Label=… /> ({shown[..Math.Min(120, shown.Length)]})");
                }
            }
        }

        Assert.True(checkedButtons > 20, $"only {checkedButtons} grid buttons were read — the patterns no longer match the markup");
        Assert.True(problems.Count == 0, "Grid buttons with words instead of an icon:\n  " + string.Join("\n  ", problems));
    }

    [Fact]
    public void Every_allowlisted_button_still_exists()
    {
        var files = RepoFiles.Paths("*.razor");
        var stale = KeepsItsWords
            .Where(k => !files.Any(p => Path.GetFileName(p) == k.File
                                        && File.ReadAllText(p).Contains(k.Marker, StringComparison.Ordinal)))
            .Select(k => $"{k.File}: \"{k.Marker}\"")
            .ToList();

        Assert.True(stale.Count == 0, "allowlist entries that no longer match anything — remove them:\n  " + string.Join("\n  ", stale));
    }
}
