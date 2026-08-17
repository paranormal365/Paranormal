using Ben.Data.Common;
using System.Text.RegularExpressions;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The words an organization cannot use as a page address (backlog item #89).
/// </summary>
/// <remarks>
/// <para>CMS pages live at the root of <c>/o/{org}/</c>, which is the address somebody would guess —
/// <c>/o/ghost-squad/about</c> rather than <c>/o/ghost-squad/pages/about</c>. The cost is that every
/// route the site adds under that namespace steals a word, and a page saved at a stolen word saves
/// happily and is then unreachable for ever.</para>
///
/// <para>The scan below is the part that matters. Refusing the words we know about today is easy;
/// the failure mode is somebody adding <c>/o/{org}/team</c> in six months and nobody remembering
/// this list exists. Then an organization with a page called "team" loses it silently, and the
/// person who broke it has already moved on.</para>
/// </remarks>
public sealed class ReservedSlugTests
{
    /// <summary>Matches the first fixed segment after the organization, e.g. "events" in /o/{UrlName}/events.</summary>
    private static readonly Regex OrgRoute = new(
        """@page\s+"/o/\{[^}]+\}/(?<segment>[a-zA-Z0-9\-]+)""",
        RegexOptions.Compiled);

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ben.slnx")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return dir!;
    }

    private static IEnumerable<string> RazorSources()
        => new[] { "Ben.Web.Library", "Ben.Web.WebApp" }
            .Select(p => Path.Combine(RepoRoot().FullName, p))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.razor", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>
    /// Every fixed segment routed under <c>/o/{org}/</c> is reserved. Adding a route without adding
    /// the word here is the mistake this catches — and it is the one nobody would notice until an
    /// organization's page stopped opening.
    /// </summary>
    [Fact]
    public void Every_route_under_an_organization_reserves_its_own_word()
    {
        var routed = RazorSources()
            .SelectMany(f => OrgRoute.Matches(File.ReadAllText(f))
                .Select(m => (File: Path.GetFileName(f), Segment: m.Groups["segment"].Value)))
            .ToList();

        // If this is empty the regex has stopped matching and the test is proving nothing.
        Assert.NotEmpty(routed);

        var unreserved = routed
            .Where(r => !CmsReservedSlugs.IsReserved(r.Segment))
            .Select(r => $"{r.Segment} (in {r.File})")
            .Distinct()
            .ToList();

        Assert.True(unreserved.Count == 0,
            "These are routed under /o/{org}/ but not reserved, so an organization could create a "
            + "page there that saves and then never opens: " + string.Join(", ", unreserved)
            + ". Add them to CmsReservedSlugs.");
    }

    [Fact]
    public void The_words_the_site_routes_today_are_refused()
    {
        foreach (var slug in new[] { "cases", "events", "Cases", "  events  " })
            Assert.NotNull(CmsReservedSlugs.RefusalFor(slug));
    }

    /// <summary>
    /// The refusal names the word and suggests a way out. Somebody told only "invalid" tries the
    /// same thing again.
    /// </summary>
    [Fact]
    public void The_refusal_explains_itself()
    {
        var refusal = CmsReservedSlugs.RefusalFor("cases");

        Assert.NotNull(refusal);
        Assert.Contains("cases", refusal!);
        Assert.Contains("our-cases", refusal);
    }

    [Fact]
    public void An_ordinary_page_name_is_fine()
    {
        foreach (var slug in new[] { "about", "our-team", "contact-us", "case-studies", "" })
            Assert.Null(CmsReservedSlugs.RefusalFor(slug));
    }

    /// <summary>
    /// Reserved comparison ignores case, because slugs are lowercased on save and somebody typing
    /// "Cases" must still be stopped rather than saved as an unreachable page.
    /// </summary>
    [Fact]
    public void Reservation_ignores_case_and_surrounding_space()
    {
        Assert.True(CmsReservedSlugs.IsReserved("EVENTS"));
        Assert.True(CmsReservedSlugs.IsReserved(" events "));
        Assert.False(CmsReservedSlugs.IsReserved("events-2026"));
    }
}
