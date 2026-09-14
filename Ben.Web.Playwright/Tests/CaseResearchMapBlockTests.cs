using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A map block on a research page: places the author chose, numbered, with the route between them the author picked.
/// </summary>
/// <remarks>
/// Ben, 2026-09-14: a map of somewhere that is not the case's address — "a cemetery where the previous owners are buried" —
/// and then "the map block should allow drawing route between pins", chosen per map, with distance and time per leg for
/// the reader. Places are added by clicking the map, which needs MapKit on this host; without it these tests are ignored
/// rather than failed, as DirectionsTests are. Walking routes ask Apple for real (one directions call per leg).
/// </remarks>
[TestFixture]
[Category("CaseResearch")]
[Category("Maps")]
public class CaseResearchMapBlockTests : ResearchPageTestBase
{
    private const string MapModule = "/_content/Ben.Web.Website.Library/Kit/Maps/BenMap.razor.js";

    private ILocator MapBlock => Page.Locator("[data-testid=map-block]").First;
    private ILocator Stops => MapBlock.Locator("li.ben-map-block__stop");
    private ILocator Legs => MapBlock.Locator("[data-testid=map-block-leg]");
    private ILocator RouteChoice => MapBlock.Locator("select[id$='-route']");

    private sealed class MapState
    {
        public int Annotations { get; set; }
        public int Overlays { get; set; }
    }

    /// <summary>The map block's map as MapKit sees it, once MapKit has drawn it; null when it never does.</summary>
    private async Task<MapState?> MapStateAsync(int waitMs = 15_000) =>
        await Page.EvaluateAsync<MapState?>(@"async ([path, waitMs]) => {
            const mod = await import(path);
            const el = document.querySelector('[data-testid=map-block] .ben-map');
            if (!el) return null;
            for (let waited = 0; waited < waitMs && !mod.describe(el.id); waited += 250) await new Promise(r => setTimeout(r, 250));
            const d = mod.describe(el.id);
            return d ? { Annotations: d.annotations, Overlays: d.overlays } : null;
        }", new object[] { MapModule, waitMs });

    private async Task AddMapBlockAsync()
    {
        await ClickUntilAsync(Page.Locator("[data-testid=block-add-map]"), MapBlock.Locator(".ben-map"));
        if (await MapStateAsync() is null) Assert.Ignore("MapKit is not configured on this host, so there is no map to click.");
    }

    /// <summary>Clicks empty map at a fraction of its width and height, and waits for the place list to grow to <paramref name="expected"/>.</summary>
    private async Task TapMapAsync(float x, float y, int expected)
    {
        var map = MapBlock.Locator(".ben-map");
        await map.ScrollIntoViewIfNeededAsync();
        var box = (await map.BoundingBoxAsync())!;
        await Page.Mouse.ClickAsync(box.X + box.Width * x, box.Y + box.Height * y);
        await Expect(Stops).ToHaveCountAsync(expected, new() { Timeout = 10_000 });
    }

    private Task<string[]> StopLinksAsync() =>
        Stops.EvaluateAllAsync<string[]>("items => items.map(li => li.querySelector('a[href*=\"maps.apple.com\"]').href)");

    [Test]
    public async Task ANewMap_HasNoPlaces_AndTapsAddNumberedPlacesJoinedByAStraightLine()
    {
        await OpenResearchTabAsync();
        await CreatePageAsync(UniqueTitle("Cemetery map"));
        await AddMapBlockAsync();

        // Nothing from the case: no place, no pin, and no route to choose until there are two places.
        Assert.That((await MapStateAsync())!.Annotations, Is.EqualTo(0), "a new map block starts with a pin on it");
        await Expect(Stops).ToHaveCountAsync(0);
        await Expect(RouteChoice).ToHaveCountAsync(0);

        await TapMapAsync(0.35f, 0.4f, expected: 1);
        await TapMapAsync(0.65f, 0.6f, expected: 2);
        await Expect(MapBlock.Locator(".ben-map-block__number")).ToHaveTextAsync(new[] { "1", "2" });
        Assert.That((await MapStateAsync())!.Annotations, Is.EqualTo(2), "each place is a pin");

        await RouteChoice.SelectOptionAsync(new SelectOptionValue { Label = "Straight lines" });
        await Expect(Legs).ToHaveCountAsync(1, new() { Timeout = 10_000 });
        await Expect(Legs.First).ToContainTextAsync("straight line");
        await Page.WaitForFunctionAsync(@"async (path) => {
            const mod = await import(path);
            return (mod.describe(document.querySelector('[data-testid=map-block] .ben-map').id)?.overlays ?? 0) >= 1;
        }", MapModule, new() { Timeout = 10_000 });
    }

    [Test]
    public async Task MovingAndRemovingPlaces_RenumbersThem_AndOnePlaceHasNoRoute()
    {
        await OpenResearchTabAsync();
        await CreatePageAsync(UniqueTitle("Move places"));
        await AddMapBlockAsync();
        await TapMapAsync(0.3f, 0.3f, expected: 1);
        await TapMapAsync(0.7f, 0.7f, expected: 2);

        var before = await StopLinksAsync();
        await MapBlock.GetByRole(AriaRole.Button, new() { Name = "Move place 1 down" }).ClickAsync();
        await Page.WaitForFunctionAsync(@"expected => [...document.querySelectorAll('[data-testid=map-block] li.ben-map-block__stop')]
            .map(li => li.querySelector('a[href*=""maps.apple.com""]').href).join('|') === expected",
            string.Join("|", before.Reverse()), new() { Timeout = 10_000 });

        await RouteChoice.SelectOptionAsync(new SelectOptionValue { Label = "Straight lines" });
        await Expect(Legs).ToHaveCountAsync(1, new() { Timeout = 10_000 });

        await MapBlock.GetByRole(AriaRole.Button, new() { Name = "Remove place 2" }).ClickAsync();
        await Expect(Stops).ToHaveCountAsync(1);
        await Expect(Legs).ToHaveCountAsync(0);
        await Expect(RouteChoice).ToHaveCountAsync(0);
        Assert.That(await StopLinksAsync(), Is.EqualTo(new[] { before[1] }), "the place left is the one that was moved up");
    }

    [Test]
    public async Task APublishedWalkingMap_ShowsTheReaderEachLeg_AndDirectionsInMaps()
    {
        await OpenResearchTabAsync();
        var url = await CreatePageAsync(UniqueTitle("Walking map"));
        await AddMapBlockAsync();
        await TapMapAsync(0.4f, 0.45f, expected: 1);
        await TapMapAsync(0.6f, 0.55f, expected: 2);
        await RouteChoice.SelectOptionAsync(new SelectOptionValue { Label = "Walking" });
        await PublishAsync();

        await LoginAsync(MemberEmail, MemberPassword);   // James reads the published page
        await ReloadPageAsync(url);

        var reader = Page.Locator("[data-testid=block-reader]");
        await Expect(reader.Locator("[data-testid=map-block]")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(Stops).ToHaveCountAsync(2);

        // A leg Apple can walk says how far and how long; one it cannot is a straight line that says so. Either is a
        // leg the reader can read — what must not happen is no figure at all.
        await Expect(Legs).ToHaveCountAsync(1, new() { Timeout = 30_000 });
        await Expect(Legs.First).ToContainTextAsync(new System.Text.RegularExpressions.Regex(@"\d.*(walk|straight line)"), new() { Timeout = 30_000 });

        var directions = MapBlock.Locator("[data-testid=map-block-open-in-maps]");
        await Expect(directions).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex(@"^https://maps\.apple\.com/\?saddr=.+&daddr=.+&dirflg=w$"));
        await Expect(Page.Locator("[data-block-handle]")).ToHaveCountAsync(0);
    }
}
