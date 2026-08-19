using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the universal media library (<c>/media-library</c>) — the cross-scope
/// browse/picker grid introduced in backlog item #6 Phase 1 — and its "Attach from
/// Library" embedding on the org case Files tab.
/// </summary>
[TestFixture]
[Category("MediaLibrary")]
public class MediaLibraryTests : BenTestBase
{
    // ── Standalone page ───────────────────────────────────────────────────────
    //
    // These go through the sidebar link rather than a direct GotoAsync, which is what the page
    // is actually reached by. The link click is retried: it is an ordinary Blazor navigation and
    // a click that lands before the circuit connects is silently dropped, which left the browser
    // sitting on the page it was already on. That read as "the media library rendered the home
    // page", and the earlier note here blamed an app-wide auth-timing bug on hard navigation —
    // it was a dropped click, and hard navigation to this page works (see the parity tests).

    private async Task NavigateToMediaLibraryAsync()
    {
        await LoginAsync(UserEmail, UserPassword);
        var link = Page.GetByRole(AriaRole.Link, new() { Name = "Media Library", Exact = true })
                       .Or(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Media Library", Exact = true }))
                       .First;
        await ClickUntilUrlAsync(link, "/media-library");
        await WaitUntilLoadedAsync();
    }

    [Test]
    public async Task Page_RendersWithoutError()
    {
        await NavigateToMediaLibraryAsync();
        await Expect(Main.GetByText("Everything you own", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Is.Not.Empty);
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task Page_AnonymousRedirectsToLogin()
    {
        await LogoutAsync();
        await Page.GotoAsync($"{BaseUrl}/media-library");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var url = Page.Url;
        var body = await Page.InnerTextAsync("body");
        Assert.That(url.Contains("/login") || body.Contains("signed in", StringComparison.OrdinalIgnoreCase),
            Is.True, "Expected redirect to login for unauthenticated media library access.");
    }

    [Test]
    public async Task Page_HasGridListToggle()
    {
        await NavigateToMediaLibraryAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Grid", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 8_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "List", Exact = true }))
            .ToBeVisibleAsync();
    }

    [Test]
    public async Task Page_HasScopeFilterChips()
    {
        await NavigateToMediaLibraryAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "All", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 8_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Mine", Exact = true }))
            .ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Public", Exact = true }))
            .ToBeVisibleAsync();
    }

    [Test]
    public async Task ScopeFilter_ClickMine_DoesNotCrash()
    {
        await NavigateToMediaLibraryAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Mine", Exact = true }).ClickAsync();
        await Page.WaitForTimeoutAsync(300);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task ListView_TogglesWithoutError()
    {
        await NavigateToMediaLibraryAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "List", Exact = true }).ClickAsync();
        await Page.WaitForTimeoutAsync(300);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    // ── Nav drawer entry ──────────────────────────────────────────────────────

    [Test]
    public async Task NavDrawer_MediaLibraryLinkNavigates()
    {
        await LoginAsync(UserEmail, UserPassword);
        var link = Page.GetByRole(AriaRole.Link, new() { Name = "Media Library", Exact = true })
                       .Or(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Media Library", Exact = true }))
                       .First;
        await Expect(link).ToBeVisibleAsync(new() { Timeout = 8_000 });
        // Retried: an unretried click here left the browser on the page it started from, which
        // read as the media library rendering the home page's content.
        await ClickUntilUrlAsync(link, "/media-library");
        // Expect, not a single InnerText read: Blazor changes the address before the new page has
        // rendered, so reading once caught the home page's text and reported the media library as
        // missing its own copy. Verified against the running app — both soft and hard navigation
        // to /media-library render correctly; only the test was early.
        await Expect(Main.GetByText("Everything you own", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    // ── "Attach from Library" picker embedding on the case Files tab ────────────

    private async Task NavigateToTghCaseFilesTab()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah — TGH org member

        // The shared walk. This had its own copy, written against the original site: it clicked
        // the organisation's name (a grid cell here, not a link) after an unretried nav click, so
        // it was really operating on whatever page it had failed to leave.
        if (!await OpenOrgCaseAsync("Tennessee Ghost Hunters", "Park"))
            Assert.Ignore("TGH case not in the seed data.");

        await OpenTabAsync("Files", Main.GetByRole(AriaRole.Button, new() { Name = "Attach from Library", Exact = false })
                                        .Or(Main.GetByText("No files", new() { Exact = false })));
    }

    [Test]
    public async Task CaseFiles_HasAttachFromLibraryButton()
    {
        await NavigateToTghCaseFilesTab();
        var attachBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Attach from Library", Exact = false });
        await Expect(attachBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    public async Task CaseFiles_AttachFromLibrary_OpensPickerWithGridToggle()
    {
        await NavigateToTghCaseFilesTab();
        var attachBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Attach from Library", Exact = false });
        await attachBtn.ClickAsync();
        await Page.WaitForTimeoutAsync(400);

        // The embedded MediaLibraryGrid (PickerMode) should render its Grid/List toggle inside the window
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Grid", Exact = true }))
            .ToBeVisibleAsync(new() { Timeout = 8_000 });

        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }
}
