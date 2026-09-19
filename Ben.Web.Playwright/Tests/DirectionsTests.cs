using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Driving directions from the map provider (item 228, phase 5), drawn on a <c>BenMap</c>.
/// </summary>
/// <remarks>
/// Exercised through the map module on a public place page rather than through the admin
/// Directions button, because no seeded address carries coordinates and that button does
/// nothing without them. The module's <c>route</c> is exactly what <c>DirectionsMapModal</c>
/// calls; what the modal adds is a form and a list, and those are Blazor markup. The route is
/// asked of Apple for real: one directions call against the daily quota.
/// </remarks>
[TestFixture]
[Category("Directions")]
public class DirectionsTests : BenTestBase
{
    private const string MapModule = "/_content/Ben.Web.Website.Library/Kit/Maps/BenMap.razor.js";
    private const string BellWitchCave = "40000001-0000-0000-0000-000000000001";

    private sealed class RouteResult
    {
        public double DistanceMeters { get; set; }
        public double DurationSeconds { get; set; }
        public int Steps { get; set; }
        public string? Error { get; set; }
        public int OverlaysAfter { get; set; }
        public int OverlaysCleared { get; set; }
    }

    [Test]
    public async Task A_route_is_drawn_and_read_out_from_an_address_in_words()
    {
        await Page.GotoAsync($"{BaseUrl}/places/{BellWitchCave}");
        await WaitUntilLoadedAsync();
        var map = Page.Locator(".ben-map").First;
        try { await Expect(map).ToBeVisibleAsync(new() { Timeout = 15_000 }); }
        catch { Assert.Ignore("the seeded place with a map is not on this deployment"); }

        var result = await Page.EvaluateAsync<RouteResult>(@"async (path) => {
            const mod = await import(path);
            const id = document.querySelector('.ben-map').id;
            for (let i = 0; i < 40 && !mod.describe(id); i++) await new Promise(r => setTimeout(r, 250));
            const r = await mod.route(id, { originAddress: 'Nashville, TN', originLatitude: null, originLongitude: null,
                                            destinationLatitude: 36.5893, destinationLongitude: -87.0625 });
            return { DistanceMeters: r.distanceMeters, DurationSeconds: r.durationSeconds, Steps: r.steps.length,
                     Error: r.error, OverlaysAfter: mod.describe(id).overlays, OverlaysCleared: -1 };
        }", MapModule);

        // Opt-in proof for a reviewer: BEN_MAP_SHOT=/some/dir writes the drawn route as a PNG.
        if (Environment.GetEnvironmentVariable("BEN_MAP_SHOT") is { Length: > 0 } dir)
        {
            await Page.WaitForTimeoutAsync(4_000);   // tiles and the framing animation
            Directory.CreateDirectory(dir);
            await map.ScreenshotAsync(new() { Path = Path.Combine(dir, "directions-route.png") });
        }

        result.OverlaysCleared = await Page.EvaluateAsync<int>(@"async (path) => {
            const mod = await import(path);
            const id = document.querySelector('.ben-map').id;
            mod.clearRoute(id);
            return mod.describe(id).overlays;
        }", MapModule);

        Assert.That(result.Error, Is.Null, "the provider should find a route from Nashville to the cave");
        // Nashville to Adams, TN is about 40 miles by road.
        Assert.That(result.DistanceMeters, Is.InRange(40_000, 120_000), "a plausible driving distance");
        Assert.That(result.DurationSeconds, Is.GreaterThan(600));
        Assert.That(result.Steps, Is.GreaterThan(2), "turn-by-turn steps come with the route");
        Assert.That(result.OverlaysAfter, Is.EqualTo(1), "the route is drawn on the map");
        Assert.That(result.OverlaysCleared, Is.EqualTo(0), "and taken off again");
    }
}
