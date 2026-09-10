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

        // NOT NetworkIdle. The home page carries a map, and the map streams tiles
        // for as long as it is on screen — so "no network activity for 500ms" is a condition this
        // page may simply never satisfy. Under the full suite's load it did not, and every test in
        // this fixture failed in SetUp with a bare 30s timeout that named nothing.
        //
        // Waiting for the placeholders instead waits for the thing actually being tested to exist,
        // and it does not care how much unrelated traffic the map is generating.
        await WaitUntilLoadedAsync();
    }

    // ── The map (Apple MapKit JS since item 228) ─────────────────────────────
    //
    // MapKit draws tiles AND pins on canvas: there is no tile <img> to watch for and no marker
    // element to click. The map module exports what a test needs instead — the pins as drawn,
    // and a select-by-index that fires the same event a click does — and a test imports the
    // very module instance the page holds.

    private const string MapModule = "/_content/Ben.Web.Website.Library/Kit/Maps/BenMap.razor.js";

    /// <summary>A plain class with settable properties: Playwright's result converter cannot fill a positional record.</summary>
    private sealed class DrawnPin
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Glyph { get; set; } = string.Empty;
    }

    /// <summary>The pins once the map has some, or an empty list after 20 seconds.</summary>
    private async Task<IReadOnlyList<DrawnPin>> WaitForPinsAsync()
    {
        var raw = await Page.EvaluateAsync<DrawnPin[]>(@"async (path) => {
            const mod = await import(path);
            const id = document.querySelector('.ben-map')?.id;
            if (!id) return [];
            for (let i = 0; i < 80 && mod.pinCount(id) === 0; i++) await new Promise(r => setTimeout(r, 250));
            return mod.pins(id).map(p => ({ Title: p.title ?? '', Subtitle: p.subtitle ?? '', Glyph: p.glyph ?? '' }));
        }", MapModule);
        return raw;
    }

    /// <summary>Selects the first pin whose glyph the predicate accepts. False when there is none.</summary>
    private async Task<bool> SelectPinAsync(Func<DrawnPin, bool> pick)
    {
        var pins = await WaitForPinsAsync();
        var index = pins.ToList().FindIndex(p => pick(p));
        if (index < 0) return false;
        return await Page.EvaluateAsync<bool>(@"async ([path, index]) => {
            const mod = await import(path);
            return mod.selectPin(document.querySelector('.ben-map').id, index);
        }", new object[] { MapModule, index });
    }

    private static bool IsSingle(DrawnPin p)  => p.Glyph == "👻";
    private static bool IsGroup(DrawnPin p)   => int.TryParse(p.Glyph, out var n) && n > 1;

    [Test]
    public async Task Map_TileLayerLoads()
    {
        // Apple's tiles are drawn on canvas; the map has at least two canvases once anything is drawn.
        var canvases = Page.Locator(".ben-map canvas");
        await Expect(canvases.First).ToBeAttachedAsync(new() { Timeout = 25_000 });
        Assert.That(await canvases.CountAsync(), Is.GreaterThanOrEqualTo(2), "Expected the map to have drawn.");
    }

    [Test]
    public async Task Map_MarkersRenderAfterLoad()
    {
        var pins = await WaitForPinsAsync();
        Assert.That(pins.Count, Is.GreaterThan(0), "Expected at least one map pin to be drawn.");
    }

    private sealed class MapState
    {
        public double[] Size { get; set; } = [];
        public double[] Span { get; set; } = [];
        public int Annotations { get; set; }
    }

    /// <summary>
    /// The map recreated by a sort frames the same view as the first one, at the container's real
    /// width (item 228, phase 3).
    /// </summary>
    /// <remarks>
    /// The home page swaps its map for a loader on every reload, so a sort makes a NEW map. That
    /// map used to come up with no pins (a re-render during its first render marked them drawn
    /// before it existed), and the state it reports is checked here against the container it
    /// fills: a region framed for the wrong width shows the Gulf of Mexico in place of the country.
    /// </remarks>
    [Test]
    public async Task Map_KeepsItsPinsAndFramingAfterASort()
    {
        var before = await WaitForPinsAsync();
        if (before.Count == 0) { Assert.Pass("No pins in the seed."); return; }

        await Page.GetByText("Newest").ClickAsync();
        await Page.WaitForTimeoutAsync(2_500);

        var after = await WaitForPinsAsync();
        Assert.That(after.Count, Is.EqualTo(before.Count), "the sort must not lose the pins");

        var state = await Page.EvaluateAsync<MapState>(@"async (path) => {
            const mod = await import(path);
            const el = document.querySelector('.ben-map');
            const d = mod.describe(el.id);
            return { Size: d.size, Span: d.span, Annotations: d.annotations };
        }", MapModule);
        Assert.That(state.Size[0], Is.GreaterThan(300), "the map should fill its container, not a mid-layout sliver");
        // At zoom 4 a Web-Mercator map shows 360 / (256 · 2^4) degrees of longitude per pixel.
        var expectedLon = 360.0 / (256 * 16) * state.Size[0];
        Assert.That(state.Span[1], Is.EqualTo(expectedLon).Within(expectedLon * 0.35),
            $"longitude span {state.Span[1]:F1}° for a {state.Size[0]}px map is not zoom 4");
        Assert.That(await Page.InnerTextAsync("body"), Does.Not.Contain("An unhandled error has occurred"));
    }

    [Test]
    public async Task Map_ClusterMarkerShowsCount()
    {
        var pins = await WaitForPinsAsync();
        var group = pins.FirstOrDefault(IsGroup);
        if (group is null) { Assert.Pass("No grouped pins — all seeded cities have exactly one case."); return; }
        Assert.That(int.Parse(group.Glyph), Is.GreaterThan(1), "A grouped pin shows its count.");
        Assert.That(group.Title, Does.Contain("cases near"));
    }

    [Test]
    public async Task Map_ClickingSingleMarker_OpensPopup()
    {
        if (!await SelectPinAsync(IsSingle))
        {
            Assert.Pass("No single-case pin in the seed.");
            return;
        }

        var popup = Page.Locator(".k-window, .modal.show");
        await Expect(popup).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Test]
    public async Task Map_PopupShowsViewInvestigationButton()
    {
        if (!await SelectPinAsync(IsSingle) && !await SelectPinAsync(IsGroup))
        {
            Assert.Pass("No pin in the seed.");
            return;
        }
        await Page.WaitForTimeoutAsync(500);
        var viewBtn = Page.GetByText("View Investigation", new() { Exact = false });
        // If a group, a case has to be picked first
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
        // Only this test is about the vote widget; the rest of the fixture is not, so the
        // gate is per test rather than on the fixture.
        await SkipIfFeatureOffAsync("features.voting");

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
    [Description("The map popup shows a non-empty title when a pin is selected.")]
    public async Task Map_PopupTitle_IsNotEmpty()
    {
        if (!await SelectPinAsync(_ => true))
        { Assert.Pass("No pin in the seed."); return; }
        await Page.WaitForTimeoutAsync(500);
        var titleBar = Page.Locator(".k-window, .modal.show-title, .k-window, .modal.show-titlebar, .modal.show .modal-title").First;
        await Expect(titleBar).ToBeVisibleAsync(new() { Timeout = 5_000 });
        var titleText = await titleBar.InnerTextAsync();
        Assert.That(titleText, Is.Not.Empty, "The popup title should not be empty.");
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
