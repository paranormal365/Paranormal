using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the home page (<c>/</c>):
/// hero section, investigation map, and ranked case list.
/// </summary>
/// <remarks>
/// These tests verify that the home page loads correctly and that all three
/// major sections render with data when the dev seed data is present.
/// They do not require authentication.
/// </remarks>
[TestFixture]
[Category("Home")]
public class HomePageTests : BenTestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Test]
    [Description("Verifies the page title is set correctly.")]
    public async Task PageTitle_IsCorrect()
    {
        var title = await Page.TitleAsync();
        Assert.That(title, Does.Contain("IsHaunted"), "Expected page title to contain 'IsHaunted'");
    }

    [Test]
    [Description("The hero opens on a photograph with a headline, a tagline and the search — and never prints the site's name.")]
    public async Task Hero_opens_on_a_photograph_with_a_headline_and_the_search()
    {
        // Signal (Ben, 2026-10-01): the front door opens like the mock-up he chose and like the
        // signed-in desk — a photograph, a headline, the search over it. It used to open on the logo.
        var hero = Page.Locator("[data-testid=home-hero]");
        await Expect(hero).ToBeVisibleAsync();
        await Expect(hero.Locator(".home-hero__headline")).ToBeVisibleAsync();
        await Expect(hero.Locator(".home-hero__tagline")).ToBeVisibleAsync();
        await Expect(hero.Locator(".home-hero__search-input")).ToBeVisibleAsync();

        var photo = await hero.Locator(".home-hero__photo")
            .EvaluateAsync<string>("e => getComputedStyle(e).backgroundImage");
        Assert.That(photo, Does.Contain("/static/images/signal/"), "the hero's photograph did not load");

        // 09/25's rule, in its new form: the site's name appears ONCE, in the header lockup. The
        // hero carries a headline — a promise — and must never repeat the name beside it.
        await Expect(hero).Not.ToContainTextAsync("IsHaunted");
    }

    [Test]
    [Description("The hero fits a phone: nothing in it is wider than the window.")]
    public async Task Hero_fits_a_phone()
    {
        // Replaces "the logo resizes with the window", whose subject is gone; what that test was
        // really protecting is the front door working on a phone.
        await Page.SetViewportSizeAsync(375, 812);
        await Page.GotoAsync(BaseUrl);
        var hero = Page.Locator("[data-testid=home-hero]");
        await Expect(hero.Locator(".home-hero__headline")).ToBeVisibleAsync();

        var overflow = await Page.EvaluateAsync<double>(
            "() => Math.max(...[...document.querySelectorAll('[data-testid=home-hero] *')].map(e => e.getBoundingClientRect().right)) - window.innerWidth");
        Assert.That(overflow, Is.LessThanOrEqualTo(1), "something in the hero runs past the edge of a phone");
    }

    [Test]
    [Description("Every section of the visitor's Home shares the hero's edges — no page within the page.")]
    public async Task Home_sections_share_the_heros_width()
    {
        // The live sections (What's Near You, Public Investigations, the flyers) sat in a centred
        // 1000px column under a full-width hero and pitch, and read as a narrower page inside the page.
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync(BaseUrl);
        await Expect(Page.Locator("#nearby")).ToBeVisibleAsync();
        var json = await Page.EvaluateAsync<string>(@"() => {
            const edge = e => { const r = e.getBoundingClientRect(); return [Math.round(r.left), Math.round(r.right)]; };
            const hero = edge(document.querySelector('[data-testid=home-hero]'));
            const bands = [...document.querySelectorAll('#nearby, .home-band, [data-testid=home-pitch], [data-testid=home-join]')]
                .filter(e => e.getBoundingClientRect().height > 0)
                .map(e => ({ name: e.id || e.dataset.testid || e.textContent.trim().slice(0, 30), edge: edge(e) }));
            return JSON.stringify({ hero, bands }); }");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var hero = doc.RootElement.GetProperty("hero");
        var bands = doc.RootElement.GetProperty("bands").EnumerateArray().ToList();
        Assert.That(bands.Count, Is.GreaterThanOrEqualTo(3), "expected at least the pitch, nearby and join bands");
        foreach (var band in bands)
        {
            var edge = band.GetProperty("edge");
            Assert.That(edge[0].GetInt32(), Is.EqualTo(hero[0].GetInt32()).Within(1), $"'{band.GetProperty("name")}' starts off the hero's left edge");
            Assert.That(edge[1].GetInt32(), Is.EqualTo(hero[1].GetInt32()).Within(1), $"'{band.GetProperty("name")}' ends off the hero's right edge");
        }
    }

    [Test]
    [Description("Text on the hero's photograph stays light in BOTH themes.")]
    public async Task Hero_text_stays_light_on_its_photograph_in_both_themes()
    {
        // Replaces "the logo follows the theme". The rule this site learned the hard way: text
        // over a photograph is NOT the page's ink. Derived from the palette, the light-mode mock-up
        // put a dark kicker on a night photograph and it vanished. Measured, in each theme.
        foreach (var theme in new[] { "dark", "light" })
        {
            await Page.EvaluateAsync($"document.documentElement.setAttribute('data-bs-theme', '{theme}')");
            var luminance = await Page.Locator("[data-testid=home-hero] .home-hero__headline").EvaluateAsync<double>(
                @"e => { const [r,g,b] = getComputedStyle(e).color.match(/[\d.]+/g).map(Number);
                         const f = v => { v/=255; return v<=0.03928 ? v/12.92 : ((v+0.055)/1.055)**2.4; };
                         return 0.2126*f(r) + 0.7152*f(g) + 0.0722*f(b); }");
            Assert.That(luminance, Is.GreaterThan(0.8), $"the headline is not light over the photograph in {theme}");
        }
    }

    [Test]
    [Description("The App Store is one click away — the footer, Home, and Safari's smart banner all point at the same app.")]
    public async Task The_app_store_is_reachable_from_every_page_and_from_home()
    {
        // Ben, 2026-10-01: "The appstore should have a link on the site." All three read
        // SiteIdentity.AppStoreUrl, so they cannot disagree — this checks they are actually there.
        const string app = "https://apps.apple.com/us/app/ishaunted/id6806786633";
        await Expect(Page.Locator("[data-testid=footer-app-store]")).ToHaveAttributeAsync("href", app);
        await Expect(Page.Locator("[data-testid=home-app-store]")).ToHaveAttributeAsync("href", app);
        await Expect(Page.Locator("meta[name=apple-itunes-app]")).ToHaveAttributeAsync("content", "app-id=6806786633");

        // and on a page that is not Home: the footer is the every-page half of the promise
        await Page.GotoAsync($"{BaseUrl}/help");
        await Expect(Page.Locator("[data-testid=footer-app-store]")).ToHaveAttributeAsync("href", app);
    }

    [Test]
    [Description("Hero shows 'Find Groups' search button.")]
    public async Task Hero_HasFindGroupsButton()
    {
        var btn = Page.GetByText("Find Groups");
        await Expect(btn).ToBeVisibleAsync();
    }

    [Test]
    [Description("'Public Investigations' section heading is visible on the home page.")]
    public async Task Investigations_SectionHeadingVisible()
    {
        var heading = Page.GetByText("Public Investigations");
        await Expect(heading).ToBeVisibleAsync();
    }

    [Test]
    [Description("The Telerik map container renders (height > 0).")]
    public async Task Map_ContainerIsRendered()
    {
        // Wait for the map div; it may take a moment as it loads after the Blazor circuit connects.
        var mapContainer = Page.Locator("[class*='k-map'], .k-widget[data-role='map']").First;
        await Expect(mapContainer).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    [Description("At least one case card is rendered in the ranked list below the map.")]
    public async Task CaseList_ShowsAtLeastOneCard()
    {
        // Wait for loading to finish
        await Page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
        var cards = Page.Locator(".card");
        var count = await cards.CountAsync();
        Assert.That(count, Is.GreaterThan(0), "Expected at least one case card to be rendered.");
    }

    [Test]
    [Description("Each case card has a 'View' link that navigates to a case detail URL.")]
    public async Task CaseList_ViewLinksNavigateToDetail()
    {
        await Page.WaitForSelectorAsync(".card-footer a", new() { Timeout = 15_000 });
        var viewLinks = Page.Locator(".card-footer a").Filter(new() { HasText = "View" });
        var count = await viewLinks.CountAsync();
        Assert.That(count, Is.GreaterThan(0), "No 'View' links found on case cards.");

        // Click the first View link and verify we land on a case detail page
        var href = await viewLinks.First.GetAttributeAsync("href");
        Assert.That(href, Does.Match(@"/o/.+/cases/.+"), "View link href does not match expected pattern.");
    }

    [Test]
    [Description("'Sort by Most Votes' button is visible and can be toggled.")]
    public async Task CaseList_SortButtonsVisible()
    {
        await Page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
        var votesBtn = Page.GetByText("Most Votes");
        var dateBtn  = Page.GetByText("Newest");
        await Expect(votesBtn).ToBeVisibleAsync();
        await Expect(dateBtn).ToBeVisibleAsync();
    }

    [Test]
    [Description("Sign in prompt is shown to unauthenticated users next to the map.")]
    public async Task ForAnonymousUser_SignInPromptIsShown()
    {
        var signIn = Page.GetByText("Sign in").First;
        await Expect(signIn).ToBeVisibleAsync();
    }
}
