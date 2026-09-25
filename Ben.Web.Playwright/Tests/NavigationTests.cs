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
        // Identity moved into the profile menu on the new site; a no-op where it is already shown.
        await OpenProfileMenuAsync();

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

    /// <summary>
    /// The mobile hamburger opens the navigation.
    /// </summary>
    /// <remarks>
    /// <para><b>This test could not fail until 2026-09-17.</b> Its <c>Or</c>-union resolved in DOM
    /// order to the hamburger, which carries <c>d-lg-none</c> — and nothing in the fixture or the
    /// runsettings sets a viewport, so Playwright's 1280×720 default applied and the button was
    /// hidden. <c>IsVisibleAsync()</c> was false on every run and the <c>Assert.Pass</c> fired.
    /// <c>.First</c> also meant the one toggle that IS visible at desktop was unreachable.</para>
    ///
    /// <para>So the viewport is set to a phone, which is the only width at which this control
    /// exists. Reloaded after resizing, because the gate is a CSS breakpoint evaluated on
    /// layout.</para>
    /// </remarks>
    [Test]
    public async Task NavDrawer_ToggleOpensAndCloses()
    {
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GotoAsync(BaseUrl);
        await WaitUntilLoadedAsync();

        var menuBtn = Page.Locator("[data-action='toggle-swap'].mobile-menu-icon").First;
        await Expect(menuBtn).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // ben-boot's toggle-swap with no data-target puts the class on the DOCUMENT ELEMENT, so
        // that is where it is asserted. Checking visibility of ".app-mobile-menu-open" would have
        // worked by accident and said less.
        await menuBtn.ClickAsync();

        await Expect(Page.Locator("html")).ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("app-mobile-menu-open"),
            new() { Timeout = 5_000 });

        // And a navigation item is genuinely reachable once it is open, which is the point of
        // opening it. Signed out, "Join a Group" is the /find entry.
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Join a Group", Exact = false })
                         .Or(Page.Locator("a[href='/find']"))
                         .First)
            .ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Closes again — "OpensAndCloses" is what the name promises.
        await menuBtn.ClickAsync();
        await Expect(Page.Locator("html")).Not.ToHaveClassAsync(
            new System.Text.RegularExpressions.Regex("app-mobile-menu-open"),
            new() { Timeout = 5_000 });
    }

    /// <summary>
    /// The home page offers a way to browse every group, and it arrives somewhere real.
    /// </summary>
    /// <remarks>
    /// <para><b>This test could not fail until 2026-09-17.</b> It asked for a LINK whose
    /// accessible name contained "Find". The only "Find Groups" text on the site is a
    /// <c>&lt;button&gt;</c> in the hero — role button, not link — and the actual links to
    /// <c>/find</c> are named "Browse All Groups" and "browse every group". So no link on the home
    /// page matched and every run took the <c>Assert.Pass</c> branch.</para>
    ///
    /// <para>Targeted by <c>href</c>, which is the thing under test, and it asserts the
    /// destination RENDERED rather than only that the URL changed — a URL assertion passes on an
    /// error panel.</para>
    /// </remarks>
    [Test]
    public async Task NavDrawer_FindGroupsLinkNavigatesToFind()
    {
        await Page.GotoAsync(BaseUrl);
        await WaitUntilLoadedAsync();

        var findLink = Page.Locator("a[href='/find']").First;
        await Expect(findLink).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await ClickUntilUrlAsync(findLink, "/find");
        await WaitUntilLoadedAsync();

        // Arrived, and the page is the discovery page rather than a refusal or an empty shell.
        await Expect(Main.GetByRole(AriaRole.Heading, new() { Name = "Find", Exact = false })
                         .Or(Main.GetByTestId("org-discovery"))
                         .Or(Main.GetByPlaceholder("Search", new() { Exact = false }))
                         .First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    // ── Theme switch ──────────────────────────────────────────────────────────

    /// <summary>
    /// The header's light/dark button actually changes the theme.
    /// </summary>
    /// <remarks>
    /// <para><b>This test could not fail until 2026-09-17.</b> It looked for
    /// <c>[aria-label*='theme']</c>, <c>[title*='theme']</c> or <c>.theme-toggle</c>, and the real
    /// control is <c>aria-label="Toggle Dark Mode"</c> with class <c>btn btn-system</c> — no
    /// title, no matching class, and "Toggle Dark Mode" does not contain the substring "theme". So
    /// every run took the <c>Assert.Pass</c> branch, and deleting the theme switcher outright
    /// would have left this green. Nothing else in the suite clicks it: the two tests that care
    /// about dark mode set <c>localStorage</c> and reload, saying so explicitly.</para>
    ///
    /// <para>Targeted on <c>data-action</c>, which is the contract the JS actually binds, rather
    /// than on a label somebody may reword.</para>
    /// </remarks>
    [Test]
    public async Task ThemeSwitch_TogglesBodyClass()
    {
        await Page.GotoAsync(BaseUrl);
        await WaitUntilLoadedAsync();

        var themeBtn = Page.Locator("[data-action='toggle-theme']").First;
        await Expect(themeBtn).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // The attribute the site actually themes on. Read before, so the assertion is about the
        // change rather than about a particular starting theme — the viewer's own preference
        // decides where this begins.
        var before = await Page.EvaluateAsync<string?>(
            "document.documentElement.getAttribute('data-bs-theme')");

        await themeBtn.ClickAsync();

        // Polled rather than slept: the toggle is JS on the document element and lands fast, but a
        // fixed wait is the flake this suite has been bitten by before.
        await Expect(Page.Locator("html")).Not.ToHaveAttributeAsync(
            "data-bs-theme", before ?? "", new() { Timeout = 5_000 });

        var after = await Page.EvaluateAsync<string?>(
            "document.documentElement.getAttribute('data-bs-theme')");

        Assert.That(after, Is.Not.EqualTo(before),
            "Clicking the header's theme button should change data-bs-theme on <html>.");
        Assert.That(after, Is.AnyOf("light", "dark"),
            $"The theme should be light or dark, not \"{after}\".");
    }

    // ── Link integrity ────────────────────────────────────────────────────────

    [Test]
    public async Task HomeLink_NavigatesHome()
    {
        await Page.GotoAsync($"{BaseUrl}/find");
        await WaitUntilLoadedAsync();

        // The brand link, which exists unconditionally in BenHeader. Asserted rather than
        // guarded: a soft pass here meant the header losing its way home reported green
        // (2026-09-17).
        var homeLink = Page.Locator("a[href='/']").First;
        await Expect(homeLink).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await homeLink.ClickAsync();
        await WaitUntilLoadedAsync();

        Assert.That(Page.Url.TrimEnd('/'), Is.EqualTo(BaseUrl.TrimEnd('/')),
            "The brand link should go home.");

        // And home RENDERED. A URL assertion alone passes on an error panel or an empty shell,
        // which is the other half of what made this test weak.
        await Expect(Main.GetByRole(AriaRole.Heading, new() { Level = 1 })
                         .Or(Main.GetByTestId("home-hero"))
                         .Or(Main.GetByText("near you", new() { Exact = false }))
                         .First)
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
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
