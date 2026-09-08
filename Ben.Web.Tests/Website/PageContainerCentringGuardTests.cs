using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Website;

/// <summary>
/// A page container must be centred horizontally, not floated in the middle of the window.
/// </summary>
/// <remarks>
/// <para>W-V1 of the 2026-09-06 evaluation, and it was never really about <c>/events</c>. The
/// site's shell puts every page inside <c>.content-wrapper</c>, which is
/// <c>display:flex</c>. Twenty-three pages opened with
/// <c>style="max-width:900px; margin:auto;"</c> — the idiom everybody knows for "centre this
/// column" — and on a flex item <c>margin:auto</c> centres on BOTH axes. So a page shorter than
/// the viewport was pushed down by half the space left over.</para>
///
/// <para>On <c>/events</c> that was 150px of nothing above the heading, with the two event cards
/// hanging in the middle of the window. It went unnoticed for so long because a page with enough
/// content to fill the screen has no leftover space to distribute, and most pages do.</para>
///
/// <para>Source-scanned, because the failure is in a hand-typed style attribute that no rendering
/// test would reach without a browser. The regex looks for the shorthand only; an explicit
/// <c>margin-top</c> or a deliberate <c>margin:auto</c> on something that is not a page container
/// is not this bug and is not flagged.</para>
/// </remarks>
public sealed class PageContainerCentringGuardTests
{
    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;
        return dir ?? throw new InvalidOperationException("repo root not found");
    }

    [Fact]
    public void No_razor_file_centres_a_container_on_both_axes()
    {
        var root = RepoRoot();
        var offences = new List<string>();

        // The two website projects. Ben.Video.Editor draws inside its own shell and is not laid
        // out by .content-wrapper, so its rules are its own.
        foreach (var project in new[] { "Ben.Web.Website", "Ben.Web.Website.Library" })
        {
            var dir = new DirectoryInfo(Path.Combine(root.FullName, project));
            if (!dir.Exists) continue;

            foreach (var file in dir.EnumerateFiles("*.razor", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file.FullName);
                foreach (Match m in Regex.Matches(text, @"margin:\s*auto\s*;"))
                {
                    var line = text.Take(m.Index).Count(c => c == '\n') + 1;
                    offences.Add($"{project}/{file.Name}:{line}");
                }
            }
        }

        Assert.True(offences.Count == 0,
            $"""
             {offences.Count} page container(s) use `margin:auto`, which centres VERTICALLY too
             inside the site's flex content wrapper — the page then floats in the middle of the
             window under a band of empty space (W-V1).

             Write `margin:0 auto;`.

               {string.Join("\n  ", offences.Take(40))}
             """);
    }
}
