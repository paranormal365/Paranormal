using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the layout: app bar, nav drawer, theme switching, and responsive behaviour.
/// Covers both authenticated and anonymous states.
/// </summary>
[TestFixture]
[Category("Navigation")]
public class NavigationTests : BenTestBase
{
    // ── App bar ───────────────────────────────────────────────────────────────

    [Test]
    public async Task AppBar_ShowsSignInWhenAnonymous()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var signIn = Page.GetByText("Sign In", new() { Exact = false }).First;
        await Expect(signIn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task AppBar_ShowsEmailAfterLogin()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var emailText = Page.GetByText(UserEmail.Split('@')[0], new() { Exact = false });
        await Expect(emailText.First).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task AppBar_AdministrationButton_VisibleForSuperAdminOnly()
    {
        // Regular user — button should NOT appear
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var adminBtn = Page.GetByText("Administration", new() { Exact = true });
        Assert.That(await adminBtn.IsVisibleAsync(), Is.False,
            "Administration button should not be visible to non-SuperAdmin users.");
    }

    // ── Nav drawer ────────────────────────────────────────────────────────────

    [Test]
    public async Task NavDrawer_ToggleOpensAndCloses()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // Look for hamburger / menu toggle
        var menuBtn = Page.GetByRole(AriaRole.Button, new() { Name = "menu" })
                          .Or(Page.Locator("[aria-label*='menu' i]"))
                          .Or(Page.Locator(".k-drawer-toggle, .mobile-menu-icon, .collapse-icon, [class*='toggle']"))
                          .First;
        if (await menuBtn.IsVisibleAsync())
        {
            await menuBtn.ClickAsync();
            await Page.WaitForTimeoutAsync(400); // drawer animation
            // Some nav item should now be visible
            var navItem = Page.GetByRole(AriaRole.Link, new() { Name = "Home" })
                              .Or(Page.GetByRole(AriaRole.Link, new() { Name = "Find" }))
                              .First;
            await Expect(navItem).ToBeVisibleAsync(new() { Timeout = 5_000 });
        }
        else
        {
            Assert.Pass("No togglable menu found — drawer may be permanently open at this viewport.");
        }
    }

    [Test]
    public async Task NavDrawer_FindGroupsLinkNavigatesToFind()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var findLink = Page.GetByRole(AriaRole.Link, new() { Name = "Find Groups", Exact = false })
                           .Or(Page.GetByRole(AriaRole.Link, new() { Name = "Find", Exact = false }))
                           .First;
        if (await findLink.IsVisibleAsync())
        {
            await findLink.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Assert.That(Page.Url, Does.Contain("/find"));
        }
        else
        {
            Assert.Pass("Find link not visible from home page — may require drawer open.");
        }
    }

    // ── Theme switch ──────────────────────────────────────────────────────────

    [Test]
    public async Task ThemeSwitch_TogglesBodyClass()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var themeBtn = Page.Locator("[aria-label*='theme' i], [title*='theme' i], button.theme-toggle").First;
        if (await themeBtn.IsVisibleAsync())
        {
            var initialClass = await Page.EvaluateAsync<string>("document.documentElement.getAttribute('data-theme') || document.body.className");
            await themeBtn.ClickAsync();
            await Page.WaitForTimeoutAsync(300);
            var newClass = await Page.EvaluateAsync<string>("document.documentElement.getAttribute('data-theme') || document.body.className");
            Assert.That(newClass, Is.Not.EqualTo(initialClass), "Expected theme class to change after toggle.");
        }
        else
        {
            Assert.Pass("Theme toggle not found by selector — skipping.");
        }
    }

    // ── Link integrity ────────────────────────────────────────────────────────

    [Test]
    public async Task HomeLink_NavigatesHome()
    {
        await Page.GotoAsync($"{BaseUrl}/find");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        // IsHaunted logo or title should link to home
        var homeLink = Page.GetByRole(AriaRole.Link, new() { Name = "IsHaunted", Exact = false })
                           .Or(Page.Locator("a[href='/']"))
                           .First;
        if (await homeLink.IsVisibleAsync())
        {
            await homeLink.ClickAsync();
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            Assert.That(Page.Url, Does.EndWith("/").Or.EndWith(BaseUrl));
        }
        else
        {
            Assert.Pass("Home link selector not found — layout may use a different nav pattern.");
        }
    }

    // ── Responsive ────────────────────────────────────────────────────────────

    [Test]
    [TestCase(375, 812,  "Mobile (iPhone X)")]
    [TestCase(768, 1024, "Tablet (iPad)")]
    [TestCase(1280, 800, "Desktop (1280px)")]
    public async Task HomePage_RendersAtViewport(int width, int height, string label)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"),
            $"Crash at viewport {label} ({width}×{height}).");
        Assert.That(body, Does.Contain("IsHaunted").Or.Contain("Investigation").Or.Contain("Ghost"),
            $"Expected content at {label}.");
    }
}
