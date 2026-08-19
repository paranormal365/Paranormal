using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Keeps Bootstrap's own JavaScript from taking ownership of markup Blazor renders.
/// </summary>
/// <remarks>
/// <para>The template this site is skinned with drives its overlays through Bootstrap's plugins:
/// <c>data-bs-toggle="modal"</c>, <c>="tab"</c>, <c>="collapse"</c> and friends. Those plugins
/// move, re-parent and remove nodes on their own. Blazor then patches a tree that no longer
/// matches what it rendered, and edits inside the affected subtree are silently lost.</para>
///
/// <para>This is the same failure this codebase already banned <c>TelerikDialog</c> for — see
/// <see cref="NoTelerikDialogTests"/> — reached by a different route. The kit components
/// (<c>BenModal</c>, <c>BenTabs</c>, <c>BenPanel</c>, <c>BenDropdown</c>) therefore render
/// Bootstrap's <i>markup</i> while keeping open/active state in C#, and none of them opt into the
/// plugins. This test is what stops a future call site from reaching for the attribute because it
/// looks like the obvious way to make a tab strip work.</para>
///
/// <para>Attributes that only style or annotate — <c>data-bs-theme</c>, <c>data-bs-target</c> on
/// its own — are unaffected; it is <c>data-bs-toggle</c> that activates a plugin.</para>
/// </remarks>
public sealed class BenKitOwnsItsDomTests
{
    // The interactive plugins. "dropdown" is included because BenDropdown owns its own open state;
    // letting Bootstrap also manage it produced menus that closed themselves on the first click.
    private static readonly string[] BannedToggles =
        ["modal", "tab", "pill", "collapse", "dropdown", "offcanvas"];

    /// <summary>
    /// Strips Razor (<c>@* *@</c>) and HTML comments so prose that names the attribute on purpose
    /// — every kit component explains why it does not use one — is not mistaken for a call site.
    /// </summary>
    private static string StripComments(string text)
    {
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"@\*.*?\*@", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);

        return System.Text.RegularExpressions.Regex.Replace(
            text, @"<!--.*?-->", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);
    }

    [Fact]
    public void No_razor_file_in_the_new_site_hands_a_subtree_to_a_bootstrap_plugin()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Ben.slnx")))
            root = root.Parent;
        Assert.NotNull(root);

        var offenders = new List<string>();
        var scanned = 0;

        var files = new[] { "Ben.Web.Website", "Ben.Web.Website.Library" }
            .Select(p => Path.Combine(root!.FullName, p))
            .Where(Directory.Exists)
            .SelectMany(p => Directory.EnumerateFiles(p, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

        foreach (var file in files)
        {
            scanned++;
            var text = StripComments(File.ReadAllText(file));

            foreach (var toggle in BannedToggles)
            {
                // Attribute with its value, in either quote style. Comments are already gone.
                if (text.Contains($"data-bs-toggle=\"{toggle}\"", StringComparison.Ordinal)
                 || text.Contains($"data-bs-toggle='{toggle}'", StringComparison.Ordinal))
                {
                    offenders.Add($"{Path.GetFileName(file)} (data-bs-toggle=\"{toggle}\")");
                }
            }
        }

        Assert.True(scanned > 0,
            "No .razor files were scanned in the new site — have the projects been renamed?");

        Assert.True(offenders.Count == 0,
            "Bootstrap's JS plugins move and remove nodes that Blazor rendered, which corrupts its "
            + "render tree and silently drops input inside the affected subtree — the same failure "
            + "that got TelerikDialog banned. Use the kit components (BenModal, BenTabs, BenPanel, "
            + "BenDropdown), which keep this state in C#. Found in:\n  "
            + string.Join("\n  ", offenders));
    }
}
