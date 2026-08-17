using Ben.Web.Library.Organization.Cms;
using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The ready-made blocks the CMS editor offers (backlog item #80, part 2).
/// </summary>
/// <remarks>
/// Most of a snippet is markup, and testing markup against itself proves nothing. The one thing
/// that genuinely breaks is <b>id collision</b>: Bootstrap's collapsibles and carousels find each
/// other through <c>id</c> and <c>data-bs-target</c>, so two carousels built from one snippet would
/// share ids and drive each other. That reads as a browser bug rather than a content mistake, which
/// is what makes it worth a test.
/// </remarks>
public sealed class CmsSnippetTests
{
    [Fact]
    public void Every_snippet_produces_something()
    {
        Assert.NotEmpty(CmsSnippets.All);

        foreach (var snippet in CmsSnippets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(snippet.Name));
            Assert.False(string.IsNullOrWhiteSpace(snippet.Description));
            Assert.False(string.IsNullOrWhiteSpace(CmsSnippets.Render(snippet)));
        }
    }

    /// <summary>
    /// Two insertions of the same snippet must not share a single id.
    /// </summary>
    [Fact]
    public void Two_insertions_of_one_snippet_never_share_an_id()
    {
        foreach (var snippet in CmsSnippets.All)
        {
            var first  = IdsIn(CmsSnippets.Render(snippet));
            var second = IdsIn(CmsSnippets.Render(snippet));

            Assert.False(first.Intersect(second).Any(),
                $"'{snippet.Name}' produced the same id twice: "
                + string.Join(", ", first.Intersect(second)));
        }
    }

    /// <summary>
    /// And every <c>data-bs-target</c> must point at an id that insertion actually created —
    /// otherwise the block renders and simply does nothing when clicked, which is worse than
    /// visibly broken.
    /// </summary>
    [Fact]
    public void Every_target_points_at_an_id_in_the_same_insertion()
    {
        foreach (var snippet in CmsSnippets.All)
        {
            var markup = CmsSnippets.Render(snippet);
            var ids    = IdsIn(markup);

            var targets = Regex.Matches(markup, """data-bs-(?:target|parent)="#([^"]+)""")
                .Select(m => m.Groups[1].Value)
                .Distinct();

            foreach (var target in targets)
                Assert.True(ids.Contains(target),
                    $"'{snippet.Name}' targets #{target}, which it never creates.");
        }
    }

    /// <summary>
    /// Snippets use the Bootstrap classes the public pages already load. One that needed its own
    /// stylesheet would look right in the editor and wrong on the live site.
    /// </summary>
    [Fact]
    public void No_snippet_carries_its_own_styles_or_scripts()
    {
        foreach (var snippet in CmsSnippets.All)
        {
            var markup = CmsSnippets.Render(snippet);
            Assert.DoesNotContain("<script", markup, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<style", markup, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static HashSet<string> IdsIn(string markup)
        => [.. Regex.Matches(markup, """\sid="([^"]+)""").Select(m => m.Groups[1].Value)];
}
