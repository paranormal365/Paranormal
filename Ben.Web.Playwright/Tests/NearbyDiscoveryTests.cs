using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Tests for the home page's "What's Near You" panel (backlog item #88).
/// </summary>
/// <remarks>
/// <para>Requires the dev seed data: <c>SeedLocalDiscoveryAsync</c> marks Tennessee Ghost Hunters
/// findable and creates two public events, one of them at the group's Nashville address and one at
/// Bell Witch Cave, 33.4 miles away. Without that data the panel is correct and empty, and every
/// assertion below would be testing an empty state.</para>
///
/// <para><b>Geolocation is granted explicitly</b> and pinned to the seeded Nashville point.
/// A Playwright context has no location by default, which sends the component down its
/// declined-permission fallback — worth testing, and done separately in
/// <see cref="NearbyDiscoveryFallbackTests"/>, but useless for asserting that results render.</para>
/// </remarks>
[TestFixture]
[Category("Nearby")]
public class NearbyDiscoveryTests : BenTestBase
{
    /// <summary>The seeded Nashville org address — the same point the seed data is built around.</summary>
    private const float NashvilleLat = 36.1627f;
    private const float NashvilleLon = -86.7816f;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        Permissions = ["geolocation"],
        Geolocation = new Geolocation { Latitude = NashvilleLat, Longitude = NashvilleLon },
    };

    /// <remarks>
    /// Waits for the panel itself, not just <c>NetworkIdle</c>. A Blazor Server circuit does its
    /// data load after the network goes quiet, so NetworkIdle alone is not a settled page — the
    /// first run against a freshly restarted app failed on exactly that race while three
    /// consecutive warm runs passed.
    /// </remarks>
    [SetUp]
    public async Task GoHome()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.GetByText("What's Near You", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    [Description("The panel renders its heading and distance control.")]
    public async Task Panel_RendersHeadingAndDistanceControl()
    {
        var heading = Page.GetByText("What's Near You", new() { Exact = false });
        await Expect(heading).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var radius = Page.Locator("#nearby-radius");
        await Expect(radius).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Test]
    [Description("With location granted, the seeded findable group appears.")]
    public async Task Granted_ShowsSeededGroup()
    {
        // The panel queries after geolocation resolves, so wait for the section rather than the
        // page load — NetworkIdle fires before the Blazor circuit has made its call.
        var groups = Page.GetByText("Groups Nearby", new() { Exact = false });
        await Expect(groups).ToBeVisibleAsync(new() { Timeout = 20_000 });

        var tgh = Page.GetByText("Tennessee Ghost Hunters", new() { Exact = false }).First;
        await Expect(tgh).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Test]
    [Description("The nearer seeded event shows at the default 25-mile radius.")]
    public async Task Granted_ShowsNearEventAtDefaultRadius()
    {
        var events = Page.GetByText("Upcoming Events", new() { Exact = false });
        await Expect(events).ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Seeded at the NPS Nashville address, so within 25 miles of the caller.
        var meeting = Page.GetByText("Open Meeting", new() { Exact = false }).First;
        await Expect(meeting).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Test]
    [Description("Event locations are labelled approximate, never as an address.")]
    public async Task Events_AreLabelledApproximate()
    {
        await Expect(Page.GetByText("Upcoming Events", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        var caveat = Page.GetByText("approximate", new() { Exact = false }).First;
        await Expect(caveat).ToBeVisibleAsync(new() { Timeout = 5_000 });
    }

    [Test]
    [Description("Widening the radius to 50 miles brings in the far seeded event.")]
    public async Task WideningRadius_BringsInTheFarEvent()
    {
        await Expect(Page.GetByText("Upcoming Events", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 20_000 });

        // Bell Witch Cave is 33.4 mi away — deliberately outside the default 25.
        var walk = Page.GetByText("Public Night Walk", new() { Exact = false });
        var visibleBefore = await walk.IsVisibleAsync();

        await Page.ClickAsync("#nearby-radius");
        await Page.ClickAsync(".k-list-item:has-text('50'), li:has-text('50')");
        await Page.WaitForTimeoutAsync(3_000); // re-query round trip

        await Expect(walk.First).ToBeVisibleAsync(new() { Timeout = 10_000 });

        Assert.That(visibleBefore, Is.False,
            "The far event was already visible at 25 miles — the seeded distances no longer "
            + "demonstrate that the radius control does anything.");
    }
}

/// <summary>
/// The panel when the browser will not say where the visitor is.
/// </summary>
/// <remarks>
/// A separate fixture because the permission is a context-level setting, and this one deliberately
/// withholds it. Declining is not an edge case — a corporate browser, an older device, or simply
/// somebody who says no. Falling silent there would look like the feature is broken.
/// </remarks>
[TestFixture]
[Category("Nearby")]
public class NearbyDiscoveryFallbackTests : BenTestBase
{
    [SetUp]
    public async Task GoHome()
    {
        await Page.GotoAsync(BaseUrl);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Expect(Page.GetByText("What's Near You", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    [Description("Without location permission, the panel offers a place-name search instead.")]
    public async Task Denied_OffersManualLocationEntry()
    {
        await Expect(Page.GetByText("What's Near You", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        var input = Page.Locator("#nearby-location");
        await Expect(input).ToBeVisibleAsync(new() { Timeout = 20_000 });
    }

    [Test]
    [Description("A typed location returns the seeded nearby results.")]
    public async Task Denied_TypedLocationReturnsResults()
    {
        var input = Page.Locator("#nearby-location");
        await Expect(input).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await input.FillAsync("Nashville, TN");
        await Page.ClickAsync("button:has-text(\"Show what's near there\")");

        // Geocoding is an outbound call, so allow for it before asserting on results.
        var results = Page.GetByText("Groups Nearby", new() { Exact = false })
            .Or(Page.GetByText("Upcoming Events", new() { Exact = false }))
            .Or(Page.GetByText("Nothing found", new() { Exact = false }));

        await Expect(results.First).ToBeVisibleAsync(new() { Timeout = 25_000 });
    }
}
