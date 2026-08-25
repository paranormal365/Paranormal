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
        // Through the sidebar filter: the link lives inside the collapsed Media group now, so a
        // bare role lookup finds nothing. See FindSidebarLinkAsync for why the filter is the path.
        await OpenSidebarLinkAsync("Media Library", "/media-library");
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

    /// <summary>
    /// The vote buttons on a library card fit the card.
    /// </summary>
    /// <remarks>
    /// They did not. The word beside each icon was hidden on a <c>d-md-inline</c> breakpoint,
    /// which asks how wide the VIEWPORT is — and says nothing about the card, which is a quarter
    /// of a row. On any desktop the words switched on and were then clipped mid-syllable:
    /// "Confi", "Disp", "Inconcl". Measured rather than eyeballed, because a screenshot review
    /// is exactly what let it through the first time.
    /// </remarks>
    [Test]
    public async Task VoteButtonsOnACard_FitTheCard()
    {
        await NavigateToMediaLibraryAsync();

        var anyCard = Page.Locator(".card .evidence-vote-widget").First;
        if (await anyCard.CountAsync() == 0)
        { Assert.Ignore("no media in this library to vote on"); return; }
        await Expect(anyCard).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var clipped = await Page.EvaluateAsync<int>(
            "() => [...document.querySelectorAll('.card .evidence-vote-widget button')]" +
            ".filter(b => b.scrollWidth > b.clientWidth + 1).length");
        Assert.That(clipped, Is.Zero, "a vote button's label is cut off inside the card");

        // And every one of them still says what it does, for a tooltip and a screen reader.
        var unlabelled = await Page.EvaluateAsync<int>(
            "() => [...document.querySelectorAll('.card .evidence-vote-widget button')]" +
            ".filter(b => !b.getAttribute('aria-label') || !b.title).length");
        Assert.That(unlabelled, Is.Zero,
            "an icon-only vote button must carry its meaning in title and aria-label");
    }

    /// <summary>
    /// Every card gets the same thumbnail tile, whatever it holds.
    /// </summary>
    /// <remarks>
    /// Without a tile of its own height, a card holding a tiny image collapsed to what looked
    /// like blank space — and "this file has no preview" was indistinguishable from "this page is
    /// broken". The seeded library is full of 1×1 test PNGs, so this was the normal view.
    /// </remarks>
    [Test]
    public async Task EveryCard_HasAThumbnailTileOfTheSameHeight()
    {
        await NavigateToMediaLibraryAsync();

        var card = Page.Locator(".card").First;
        if (await card.CountAsync() == 0) { Assert.Ignore("no media in this library"); return; }
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var heights = await Page.EvaluateAsync<int[]>(
            "() => [...document.querySelectorAll('.card .card-body > div')]" +
            ".filter(d => d.style && d.style.height)" +
            ".map(d => Math.round(d.getBoundingClientRect().height))");

        Assert.That(heights, Is.Not.Empty, "cards should have a fixed-height thumbnail tile");
        Assert.That(heights.Distinct().Count(), Is.EqualTo(1),
            "thumbnail tiles should all be the same height — a ragged grid reads as a broken page");
        Assert.That(heights[0], Is.GreaterThan(40), "the tile should be a visible frame");
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
        // The entry sits inside the collapsed Media group; the filter is how it is reached, and
        // this test doubles as the filter's own e2e coverage.
        var link = await FindSidebarLinkAsync("Media Library");
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
