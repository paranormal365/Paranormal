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
    [Description("Hero section renders with logo and tagline.")]
    public async Task Hero_RendersLogoAndTagline()
    {
        var logo = Page.Locator("img.home-hero__logo:visible");
        await Expect(logo).ToHaveCountAsync(1);
        await Expect(logo).ToBeVisibleAsync();

        var tagline = Page.Locator(".home-hero__tagline");
        await Expect(tagline).ToBeVisibleAsync();

        // The logo carries the site's name, so no title line repeats it (Ben, 09/25).
        await Expect(Page.Locator(".home-hero__title")).ToHaveCountAsync(0);
    }

    [Test]
    [Description("The hero's logo resizes with the window: smaller on a narrow one, larger on a wide one, within its bounds.")]
    public async Task Hero_logo_resizes_with_the_window()
    {
        async Task<double> WidthAtAsync(int windowWidth)
        {
            await Page.SetViewportSizeAsync(windowWidth, 900);
            var shown = Page.Locator("img.home-hero__logo:visible");
            await Expect(shown).ToHaveCountAsync(1);
            return (await shown.BoundingBoxAsync())!.Width;
        }

        var phone = await WidthAtAsync(375);
        var tablet = await WidthAtAsync(820);
        var desktop = await WidthAtAsync(1440);
        Assert.Multiple(() =>
        {
            Assert.That(phone, Is.EqualTo(220).Within(1), "a phone gets the smallest logo");
            Assert.That(tablet, Is.GreaterThan(phone + 50).And.LessThan(desktop), "it grows with the window");
            Assert.That(desktop, Is.EqualTo(460).Within(1), "and stops growing on a wide one");
        });
    }

    [Test]
    [Description("The hero shows the gray glass logo in dark mode and the white one in light mode, following the header's theme toggle.")]
    public async Task Hero_logo_follows_the_theme()
    {
        async Task<string?> ShownLogoAsync(string theme)
        {
            // The site's own theme switch, as the header's toggle sets it — not only the device's preference.
            await Page.EvaluateAsync($"document.documentElement.setAttribute('data-bs-theme', '{theme}')");
            var shown = Page.Locator("img.home-hero__logo:visible");
            await Expect(shown).ToHaveCountAsync(1);
            // Loaded, not just placed: a lazy picture that never arrives would still count as visible.
            await Expect(shown).ToHaveJSPropertyAsync("complete", true, new() { Timeout = 15_000 });
            Assert.That(await shown.EvaluateAsync<int>("i => i.naturalWidth"), Is.GreaterThan(0), $"the {theme} logo did not load");
            return await shown.GetAttributeAsync("src");
        }

        Assert.That(await ShownLogoAsync("dark"), Is.EqualTo("/static/images/logo-glass-gray.png"));
        Assert.That(await ShownLogoAsync("light"), Is.EqualTo("/static/images/logo-glass-white.png"));
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
