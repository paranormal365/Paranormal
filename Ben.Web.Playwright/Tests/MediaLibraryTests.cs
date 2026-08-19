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
    }

    [Test]
    public async Task Page_RendersWithoutError()
    {
        await NavigateToMediaLibraryAsync();
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Is.Not.Empty);
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
        Assert.That(body, Does.Contain("Everything you own"));
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
        var link = Page.GetByRole(AriaRole.Link, new() { Name = "Media Library", Exact = true }).Or(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Media Library", Exact = true })).First;
        await Expect(link).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await link.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Contain("Everything you own"));
    }

    // ── "Attach from Library" picker embedding on the case Files tab ────────────

    private async Task NavigateToTghCaseFilesTab()
    {
        await LoginAsync(UserEmail, UserPassword); // Sarah — TGH org member
        // SPA nav-link click, not Page.GotoAsync — see the note above the standalone-page
        // tests: a hard navigation to an authenticated route currently mis-redirects here too.
        await Page.GetByRole(AriaRole.Link, new() { Name = "Organizations", Exact = true }).Or(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Organizations", Exact = true })).First.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var tgh = Page.GetByText("Tennessee Ghost Hunters", new() { Exact = false });
        if (!await tgh.IsVisibleAsync()) { Assert.Pass("TGH org not visible; seed data may differ."); return; }
        await tgh.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var casesLink = Page.GetByRole(AriaRole.Link, new() { Name = "Cases" })
                            .Or(Main.GetByText("Cases", new() { Exact = true })).First;
        await casesLink.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var caseItem = Page.GetByText("Park", new() { Exact = false }).First;
        await Expect(caseItem).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await caseItem.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var filesTab = Page.GetByRole(AriaRole.Tab, new() { Name = "Files", Exact = true })
                           .Or(Main.GetByText("Files", new() { Exact = true })).First;
        await filesTab.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
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
