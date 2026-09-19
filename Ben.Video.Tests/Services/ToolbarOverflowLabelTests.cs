using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Video.Tests.Services;

/// <summary>
/// The toolbar's "…" menu has to be both readable and reachable.
/// </summary>
/// <remarks>
/// <para>Backlog item 93. A toolbar button that collapses into the overflow menu loses its icon's
/// context: in the bar an icon sits among familiar neighbours and answers to a tooltip, while in
/// the menu it is one row of a list, read rather than hovered. Undo, Redo and Save to server
/// appeared there as three bare glyphs — the last of them a cloud, which could as easily have
/// meant upload, sync or download.</para>
///
/// <para>The second test guards the reason nobody noticed sooner: the menu was covered. Kendo
/// places popups at z-index 10002 and windows at 11500, and the editor's Media &amp; Properties
/// window docks to the right — directly beneath the "…" button. Hit-testing the menu's own items
/// returned the window's title bar and tab strip, so the menu was not just half-hidden but
/// unclickable, and at narrow widths it is the only route to Preview, Export, Undo and Redo.</para>
/// </remarks>
public sealed class ToolbarOverflowLabelTests
{
    private static DirectoryInfo EditorRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Ben.Video.Editor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return new DirectoryInfo(Path.Combine(dir!.FullName, "Ben.Video.Editor"));
    }

    [Fact]
    public void Buttons_That_Can_Reach_The_Overflow_Menu_Carry_A_Label()
    {
        var markup = File.ReadAllText(
            Path.Combine(EditorRoot().FullName, "Components", "Toolbar.razor"));

        // A self-closing tag has no child content, and child content is what the overflow menu
        // shows as the row's text. ToolBarTemplateItem is not considered: Telerik treats it as
        // Overflow=Never regardless, so it never reaches the menu.
        var selfClosing = Regex.Matches(markup, @"<ToolBarButton\b[^>]*?/>", RegexOptions.Singleline);

        var unlabelled = selfClosing
            .Where(m => !m.Value.Contains("ToolBarItemOverflow.Never", StringComparison.Ordinal))
            .Select(m =>
            {
                var title = Regex.Match(m.Value, @"Title=""([^""]*)""").Groups[1].Value;
                var icon = Regex.Match(m.Value, @"Icon=""@\(?SvgIcon\.(\w+)").Groups[1].Value;
                return $"the {(icon.Length > 0 ? icon : "unnamed")} button ({title})";
            })
            .ToList();

        Assert.True(unlabelled.Count == 0,
            "These toolbar buttons can collapse into the overflow menu, where they would render as "
            + "an icon and nothing else. Give each one child content to use as its label, or set "
            + "Overflow=\"@ToolBarItemOverflow.Never\" if it should always stay in the bar:\n  "
            + string.Join("\n  ", unlabelled));

        // The scan is only meaningful while buttons are still written this way.
        Assert.Contains("<ToolBarButton", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// The actions a person came to the editor for stay in the bar.
    /// </summary>
    /// <remarks>
    /// <para>Initialize, Open, Preview, Export, Undo and Redo carried no Overflow attribute, so
    /// they took Telerik's default of Auto and were the only things that could collapse. As soon
    /// as the status chip grew a progress bar, the whole primary row moved into "…" — including
    /// Initialize, which the help calls the first thing to press (2026-09-05 audit, F1).</para>
    ///
    /// <para>Matching on the handler name rather than the label so a reworded button still
    /// counts.</para>
    /// </remarks>
    [Fact]
    public void The_Primary_Actions_Never_Collapse_Into_The_Overflow_Menu()
    {
        var markup = File.ReadAllText(
            Path.Combine(EditorRoot().FullName, "Components", "Toolbar.razor"));

        string[] primary =
        [
            "OnInitializeClicked", "OnOpenClicked", "OnPreviewClicked",
            "OnExportClicked", "OnUndoClicked", "OnRedoClicked",
        ];

        var collapsible = new List<string>();
        foreach (var handler in primary)
        {
            var button = Regex.Match(
                markup,
                @"<ToolBarButton\b(?:(?!</ToolBarButton>|/>).)*?OnClick=""" + handler + @"""(?:(?!</ToolBarButton>|/>).)*?(?:</ToolBarButton>|/>)",
                RegexOptions.Singleline);

            if (!button.Success)
            {
                collapsible.Add($"{handler} (no button found — was it renamed?)");
                continue;
            }

            if (!button.Value.Contains("ToolBarItemOverflow.Never", StringComparison.Ordinal))
                collapsible.Add(handler);
        }

        Assert.True(collapsible.Count == 0,
            "These are the editor's primary actions and must stay visible in the toolbar. Set "
            + "Overflow=\"@ToolBarItemOverflow.Never\" on each:\n  " + string.Join("\n  ", collapsible));
    }

    [Fact]
    public void The_Overflow_Menu_Sits_Above_Kendo_Windows()
    {
        var css = File.ReadAllText(
            Path.Combine(EditorRoot().FullName, "wwwroot", "css", "ben-video-theme.css"));

        var rule = Regex.Match(
            css,
            @"\.k-animation-container:has\(\.k-toolbar-popup\)\s*\{[^}]*z-index:\s*(\d+)\s*!important",
            RegexOptions.Singleline);

        Assert.True(rule.Success,
            "ben-video-theme.css no longer raises the toolbar's overflow popup. Without it the "
            + "Media & Properties window (Kendo z-index 11500) paints over the menu opened by the "
            + "button directly beneath it, and its items cannot be clicked.");

        var z = int.Parse(rule.Groups[1].Value);
        Assert.True(z > 11500,
            $"The overflow popup is raised to {z}, which is still under Kendo's window layer at "
            + "11500 — the value that has to be beaten. 10050 looks plausible and fixes nothing.");
    }
}
