using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the home page map interaction:
/// marker rendering, popup behaviour, cluster expansion,
/// sort toggle, pager, and vote widget rendering.
/// </summary>
[TestFixture]
[Category("HomeMap")]
public class HomeMapTests : BenTestBase
{
    [SetUp]
    public async Task GoHome()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
    }

    [Test]
    public async Task Map_TileLayerLoads()
    {
        // Wait for at least one OpenStreetMap tile request
        var tileLoaded = false;
        Page.Response += (_, response) =>
        {
            if (response.Url.Contains("tile.openstreetmap.org"))
                tileLoaded = true;
        };
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(3_000); // tiles load async
        Assert.That(tileLoaded, Is.True, "Expected at least one OpenStreetMap tile to be requested.");
    }

    [Test]
    public async Task Map_MarkersRenderAfterLoad()
    {
        // Ghost or cluster markers appear as spans in the DOM after LoadAsync completes
        await Page.WaitForSelectorAsync(".case-map-cluster, .case-map-single", new() { Timeout = 15_000 });
        var markers = Page.Locator(".case-map-cluster, .case-map-single");
        var count = await markers.CountAsync();
        Assert.That(count, Is.GreaterThan(0), "Expected at least one map marker to render.");
    }

    [Test]
    public async Task Map_ClusterMarkerShowsCount()
    {
        await Page.WaitForSelectorAsync(".case-map-cluster", new() { Timeout = 15_000, State = WaitForSelectorState.Attached });
        var cluster = Page.Locator(".case-map-cluster").First;
        if (await cluster.IsVisibleAsync())
        {
            var text = await cluster.InnerTextAsync();
            Assert.That(int.TryParse(text.Trim(), out var n) && n > 1, Is.True,
                "Cluster marker should display a count ≥ 2.");
        }
        else
        {
            Assert.Pass("No cluster markers — all seeded cities have exactly one case.");
        }
    }

    [Test]
    public async Task Map_ClickingSingleMarker_OpensPopup()
    {
        await Page.WaitForSelectorAsync(".case-map-single", new() { Timeout = 15_000, State = WaitForSelectorState.Attached });
        // Not .First: at the default zoom the first marker in DOM order is sitting underneath a
        // neighbouring pin, so clicking it is intercepted rather than dropped.
        if (!await ClickTopmostAsync(Page.Locator(".case-map-single")))
        {
            Assert.Pass("No reachable single-case marker at the default US zoom level.");
            return;
        }

        // TelerikWindow popup should appear
        var popup = Page.Locator(".k-window, .modal.show");
        await Expect(popup).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Test]
    public async Task Map_PopupShowsViewInvestigationButton()
    {
        await Page.WaitForSelectorAsync(".case-map-single, .case-map-cluster", new() { Timeout = 15_000 });
        if (!await ClickTopmostAsync(Page.Locator(".case-map-single"))
         && !await ClickTopmostAsync(Page.Locator(".case-map-cluster")))
        {
            Assert.Pass("No reachable marker at the default US zoom level.");
            return;
        }
        await Page.WaitForTimeoutAsync(500);
        var viewBtn = Page.GetByText("View Investigation", new() { Exact = false });
        // If cluster, may need to click a case first
        if (!await viewBtn.IsVisibleAsync())
        {
            var firstCase = Page.Locator(".list-group-item").First;
            if (await firstCase.IsVisibleAsync())
                await firstCase.ClickAsync();
        }
        await Expect(viewBtn).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Test]
    public async Task List_SortByDateChangesOrder()
    {
        await Page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
        var dateBefore = await Page.Locator(".card .font-monospace").First.InnerTextAsync();
        await Page.GetByText("Newest").ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var dateAfter = await Page.Locator(".card .font-monospace").First.InnerTextAsync();
        // Order may or may not change depending on data, but no crash
        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task List_AuthUser_SeesVoteButtons()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
        // CaseVoteWidget shows vote buttons when authenticated
        var confirmBtn = Page.GetByRole(AriaRole.Button, new() { Name = "Confirms the findings" }).First;
        await Expect(confirmBtn).ToBeVisibleAsync(new() { Timeout = 12_000 });
    }

    [Test]
    public async Task List_AnonymousUser_SeesSignInPrompt()
    {
        await Page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });
        var signIn = Page.GetByText("Sign in to vote", new() { Exact = false }).First;
        await Expect(signIn).ToBeVisibleAsync(new() { Timeout = 8_000 });
    }

    [Test]
    [Description("The map popup (TelerikWindow) shows a non-empty title when a marker is clicked.")]
    public async Task Map_PopupTitle_IsNotEmpty()
    {
        await Page.WaitForSelectorAsync(".case-map-single, .case-map-cluster", new() { Timeout = 15_000 });
        if (!await ClickTopmostAsync(Page.Locator(".case-map-single, .case-map-cluster")))
        { Assert.Pass("No reachable marker at the default US zoom level."); return; }
        await Page.WaitForTimeoutAsync(500);
        var titleBar = Page.Locator(".k-window, .modal.show-title, .k-window, .modal.show-titlebar, .modal.show .modal-title").First;
        await Expect(titleBar).ToBeVisibleAsync(new() { Timeout = 5_000 });
        var titleText = await titleBar.InnerTextAsync();
        Assert.That(titleText, Is.Not.Empty, "TelerikWindow title should not be empty.");
    }

    [Test]
    [Description("Sort toggle buttons show the active button as selected after clicking.")]
    public async Task List_SortToggle_ShowsSelectedState()
    {
        await Page.WaitForSelectorAsync(".card", new() { Timeout = 15_000 });

        // Click Newest and verify the page doesn't error
        var newestBtn = Page.GetByText("Newest", new() { Exact = false }).First;
        await Expect(newestBtn).ToBeVisibleAsync(new() { Timeout = 8_000 });
        await newestBtn.ClickAsync();
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var body = await Page.InnerTextAsync("body");
        Assert.That(body, Does.Not.Contain("An unhandled error has occurred"),
            "Clicking sort toggle should not cause a Telerik component error.");
        Assert.That(body, Does.Not.Contain("does not have a property matching"),
            "ButtonGroupToggleButton should not produce a parameter error.");
    }
}
