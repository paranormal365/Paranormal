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
        => new[] { "Ben.Web.Website.Library", "Ben.Web.Website" }
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

/// <summary>
/// Building the readable part of a URL (backlog item #89).
/// </summary>
/// <remarks>
/// The street-address check is the part with teeth. A slug is public text that ends up in browser
/// histories, referrer headers and pasted links, and a case is somebody's home — so
/// <c>/cases/42-elm-street-hauntings</c> would hand back everything redacting the coordinates was
/// built to protect.
/// </remarks>
public sealed class UrlSlugTests
{
    private static string? Slug(string? text)
        => (string?)typeof(Ben.Data.WebApi.Controllers.Public.PublicCaseController).Assembly
            .GetType("Ben.Data.WebApi.Services.UrlSlug")!
            .GetMethod("From")!.Invoke(null, [text]);

    private static bool LooksLikeAddress(string? text)
        => (bool)typeof(Ben.Data.WebApi.Controllers.Public.PublicCaseController).Assembly
            .GetType("Ben.Data.WebApi.Services.UrlSlug")!
            .GetMethod("LooksLikeAStreetAddress")!.Invoke(null, [text])!;

    [Theory]
    [InlineData("The Mill House Investigation", "the-mill-house-investigation")]
    [InlineData("  Spaced   Out  ", "spaced-out")]
    [InlineData("Café Noir", "cafe-noir")]
    [InlineData("What's Happening?!", "what-s-happening")]
    public void A_title_becomes_a_readable_slug(string title, string expected)
        => Assert.Equal(expected, Slug(title));

    /// <summary>
    /// Accents fold to their base letters rather than being dropped — a URL that silently loses
    /// letters reads as a typo.
    /// </summary>
    [Fact]
    public void Nothing_usable_gives_nothing()
    {
        Assert.Null(Slug(null));
        Assert.Null(Slug("   "));
        Assert.Null(Slug("!!!"));
    }

    [Theory]
    [InlineData("42 Elm Street Hauntings")]
    [InlineData("Investigation at 1600 Pennsylvania Ave")]
    [InlineData("14 Oak Road")]
    [InlineData("7 St Mary's Lane")]
    public void A_street_address_is_recognised(string title)
        => Assert.True(LooksLikeAddress(title), $"'{title}' should have been caught.");

    /// <summary>
    /// Deliberately narrow. This refuses a title an organization typed, and a rule that fired on
    /// ordinary names would teach people to work around it rather than to name things carefully.
    /// </summary>
    [Theory]
    [InlineData("The Mill House")]
    [InlineData("The 1892 Foundry")]
    [InlineData("Case of the 13 Bells")]
    [InlineData("Streetlight Manor")]
    public void An_ordinary_title_is_not_mistaken_for_an_address(string title)
        => Assert.False(LooksLikeAddress(title), $"'{title}' should not have been caught.");
}
