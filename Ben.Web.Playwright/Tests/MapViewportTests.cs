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

        var map = Page.Locator(".k-map").First;
        try { await Expect(map).ToBeVisibleAsync(new() { Timeout = 15_000 }); }
        catch { Assert.Ignore("no investigation on this org has coordinates, so there is no map to pan"); }

        Assert.That(await Page.EvaluateAsync<string>(ReadOrgCounter), Is.EqualTo("0"),
            "nothing bounded before any gesture");

        await PanThriceAsync(map);

        Assert.That(await Page.EvaluateAsync<string>(ReadOrgCounter), Is.EqualTo("1"),
            "three gestures inside the debounce window should produce exactly one bounded request");
    }

    [Test]
    public async Task Panning_the_public_cases_map_narrows_the_list_with_it()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        var map = Page.Locator(".k-map").First;
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
