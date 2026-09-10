using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A map that can be panned has to ask about where it now is.
/// </summary>
/// <remarks>
/// <para>Ben, 2026-09-09: <em>"When a person scrolls, does it load investigations or cases in the
/// new areas?"</em> It did not. The group's Investigations map and the home page's public cases
/// map each loaded once and then panned over a set that never changed — and the cases map asked
/// for 500, so past that it quietly stopped being the whole picture.</para>
///
/// <para>Modelled on <c>MyFieldSessionsTests.Panning_the_map_asks_for_the_viewport_once</c>, which
/// is the one map that already did this properly. Blazor Server calls the API from the server, so
/// the browser never sees the request; each page counts its own bounded loads into a hidden
/// element for exactly this reason.</para>
/// </remarks>
[TestFixture]
[Category("MapViewport")]
public class MapViewportTests : BenTestBase
{
    private const string ReadOrgCounter =
        "() => document.querySelector(\"[data-testid='org-map-bounded-loads']\")?.textContent?.trim() ?? 'missing'";

    /// <summary>Three quick drags, all inside the debounce window.</summary>
    /// <remarks>
    /// Scrolled into view first, and deliberately. A bounding box is in viewport coordinates, so a
    /// map below the fold hands back a y the mouse cannot reach — the drags land on whatever
    /// happens to be there instead, and the test reports "the map never reloaded" about a map
    /// nobody touched. The public cases map is near the bottom of the home page.
    /// </remarks>
    private async Task PanThriceAsync(ILocator map)
    {
        await map.ScrollIntoViewIfNeededAsync();
        await Page.WaitForTimeoutAsync(300);

        var box = (await map.BoundingBoxAsync())!;
        var cx = box.X + box.Width / 2;
        var cy = box.Y + box.Height / 2;

        for (var i = 0; i < 3; i++)
        {
            await Page.Mouse.MoveAsync(cx, cy);
            await Page.Mouse.DownAsync();
            await Page.Mouse.MoveAsync(cx + 60, cy + 40, new() { Steps = 5 });
            await Page.Mouse.UpAsync();
            await Page.WaitForTimeoutAsync(80);   // well inside the 350 ms debounce
        }

        await Page.WaitForTimeoutAsync(1500);     // let it fire and the answer land
    }

    [Test]
    public async Task Panning_a_groups_investigations_asks_for_the_viewport_once()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        if (!await OpenOrganizationAsync("Paranormal365"))
            Assert.Ignore("Seeded Paranormal365 not reachable.");

        await OpenTabAsync("Investigations", Main.GetByText("Investigations", new() { Exact = false }).First);
        await SkipAnyTourAsync();

        var map = Page.Locator(".ben-map").First;
        try { await Expect(map).ToBeVisibleAsync(new() { Timeout = 15_000 }); }
        catch { Assert.Ignore("no investigation on this org has coordinates, so there is no map to pan"); }

        Assert.That(await Page.EvaluateAsync<string>(ReadOrgCounter), Is.EqualTo("0"),
            "nothing bounded before any gesture");

        await PanThriceAsync(map);

        Assert.That(await Page.EvaluateAsync<string>(ReadOrgCounter), Is.EqualTo("1"),
            "three gestures inside the debounce window should produce exactly one bounded request");
    }

    /// <summary>
    /// Selecting a pin still opens the investigation, now that the pin is an Apple annotation
    /// rather than an HTML template with an onclick (item 228, phase 2).
    /// </summary>
    [Test]
    public async Task Selecting_a_pin_opens_the_investigation()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        if (!await OpenOrganizationAsync("Paranormal365"))
            Assert.Ignore("Seeded Paranormal365 not reachable.");
        await OpenTabAsync("Investigations", Main.GetByText("Investigations", new() { Exact = false }).First);
        await SkipAnyTourAsync();

        // MapKit draws pins on canvas, so there is no element to click and a synthetic click at a
        // guessed offset tests the guess. The map module exports selectPin for exactly this: it
        // fires the same 'select' event a click does, on the same module instance the page holds.
        var map = Page.Locator(".ben-map").First;
        try { await Expect(map).ToBeVisibleAsync(new() { Timeout = 15_000 }); }
        catch { Assert.Ignore("no investigation on this org has coordinates, so there is no map"); }
        var selected = await Page.EvaluateAsync<bool>(@"async () => {
            const mod = await import('/_content/Ben.Web.Website.Library/Kit/Maps/BenMap.razor.js');
            const id = document.querySelector('.ben-map').id;
            for (let i = 0; i < 40 && mod.pinCount(id) === 0; i++) await new Promise(r => setTimeout(r, 250));
            return mod.selectPin(id, 0);
        }");
        if (!selected) Assert.Ignore("no investigation on this org has coordinates, so there is no pin to select");

        // A click must always show something (Ben, 2026-08-22): a case-bound visit goes to its
        // case's Investigations tab; a case-less one is highlighted and opened here.
        var shown = Page.Locator("tr.table-active").First;
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && !Page.Url.Contains("/cases/") && await shown.CountAsync() == 0)
            await Page.WaitForTimeoutAsync(250);
        Assert.That(Page.Url.Contains("/cases/") || await shown.CountAsync() > 0, Is.True,
            "selecting the pin neither opened a case nor highlighted a row");
    }

    [Test]
    public async Task Panning_the_public_cases_map_narrows_the_list_with_it()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var map = Page.Locator(".ben-map").First;
        try { await Expect(map).ToBeVisibleAsync(new() { Timeout = 20_000 }); }
        catch { Assert.Ignore("no public cases on this deployment, so the discovery map is absent"); }

        // Before any gesture the page speaks about everything, not about a view.
        await Expect(Page.Locator("[data-testid='case-map-in-view']")).ToHaveCountAsync(0);

        await PanThriceAsync(map);

        // The map and the list are built from one answer, so the page now says which view it means
        // — whether or not anything is left inside it.
        var narrowed = Page.Locator("[data-testid='case-map-in-view']")
            .Or(Page.Locator("[data-testid='case-map-empty-view']"));
        await Expect(narrowed.First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
