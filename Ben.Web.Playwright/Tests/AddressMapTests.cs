using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The single-address map (item 228, phase 4): the pin, the region circle and the click that
/// sets a region's edge, on Apple Maps through <c>BenMap</c>.
/// </summary>
/// <remarks>
/// Reached through the profile's Contact tab: pressing <b>Add</b> opens the address editor,
/// which shows the map before any address is typed (a world view with one pin). Nothing is saved;
/// the form is cancelled at the end. The circle and the tap are exercised through the map
/// module's own exports, because the config panel that would drive them is not offered by any
/// page today — the component keeps it for the day one does.
/// </remarks>
[TestFixture]
[Category("AddressMap")]
public class AddressMapTests : BenTestBase
{
    private const string MapModule = "/_content/Ben.Web.Website.Library/Kit/Maps/BenMap.razor.js";

    private sealed class MapState
    {
        public double[] Size { get; set; } = [];
        public double[] Span { get; set; } = [];
        public int Annotations { get; set; }
        public int Overlays { get; set; }
        public double[]? LastTap { get; set; }
    }

    /// <summary>What the map is showing, once it has finished showing it.</summary>
    /// <remarks>
    /// It waited for the map to DESCRIBE itself and then read immediately, so a pin placed a
    /// moment after the map first answered was not there yet and the reading came back with none.
    /// It waits for the pin too now — bounded, and it still returns whatever it finds, so a map
    /// that genuinely draws nothing fails the assertion rather than hanging.
    /// </remarks>
    private Task<MapState> StateAsync() => Page.EvaluateAsync<MapState>(@"async (path) => {
        const mod = await import(path);
        const el = document.querySelector('.ben-map');
        for (let i = 0; i < 40 && !mod.describe(el.id); i++) await new Promise(r => setTimeout(r, 250));
        for (let i = 0; i < 40 && (mod.describe(el.id)?.annotations ?? 0) === 0; i++)
            await new Promise(r => setTimeout(r, 250));
        const d = mod.describe(el.id);
        return { Size: d.size, Span: d.span, Annotations: d.annotations, Overlays: d.overlays, LastTap: d.lastTap };
    }", MapModule);

    [Test]
    public async Task The_address_editor_draws_a_pin_a_region_and_hears_a_click()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/profile");
        await WaitUntilLoadedAsync();

        // The TAB, not any text saying "Contact".
        //
        // GetByText("Contact").First matched the footer link that every page carries, so the test
        // navigated to the public Contact Us page and then waited for an Add button that page has
        // never had. It only bit once the profile's tabs started rendering a moment later than the
        // footer, at which point the first match flipped from the tab to the link (2026-09-21).
        var contactTab = Main.GetByRole(AriaRole.Tab, new() { Name = "Contact", Exact = true });
        await Expect(contactTab).ToBeVisibleAsync(new() { Timeout = 20_000 });

        await ClickUntilAsync(contactTab, Main.GetByRole(AriaRole.Button, new() { Name = "Add" }).First);
        await ClickUntilAsync(Main.GetByRole(AriaRole.Button, new() { Name = "Add" }).First,
            Page.Locator(".ben-map").First);

        var map = Page.Locator(".ben-map").First;
        await Expect(map).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // One pin, on a world view (zoom 2: 360 / (256 · 4) degrees of longitude per pixel).
        var state = await StateAsync();
        Assert.That(state.Annotations, Is.EqualTo(1), "the address editor shows one pin");
        var expectedLon = 360.0 / (256 * 4) * state.Size[0];
        Assert.That(state.Span[1], Is.EqualTo(expectedLon).Within(expectedLon * 0.35),
            $"longitude span {state.Span[1]:F1}° for a {state.Size[0]}px map is not zoom 2");

        // A region circle can be drawn and taken away again.
        var overlays = await Page.EvaluateAsync<int[]>(@"async (path) => {
            const mod = await import(path);
            const id = document.querySelector('.ben-map').id;
            mod.setCircle(id, { latitude: 20, longitude: 0, radiusMeters: 500000, fillColor: '#3388ff', fillOpacity: .2, strokeColor: '#1155cc', strokeOpacity: .8, strokeWidth: 2 });
            const withCircle = mod.describe(id).overlays;
            mod.setCircle(id, null);
            return [withCircle, mod.describe(id).overlays];
        }", MapModule);
        Assert.That(overlays, Is.EqualTo(new[] { 1, 0 }), "the circle is drawn, then removed");

        // A click on empty map reaches the module's tap handler with a ground coordinate.
        await map.ScrollIntoViewIfNeededAsync();
        var box = (await map.BoundingBoxAsync())!;
        await Page.Mouse.ClickAsync(box.X + box.Width * 0.3f, box.Y + box.Height * 0.3f);
        await Page.WaitForTimeoutAsync(500);
        state = await StateAsync();
        Assert.That(state.LastTap, Is.Not.Null, "a click on the map is reported as a coordinate");
        Assert.That(state.LastTap![0], Is.InRange(-90, 90));

        await Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).First.ClickAsync();
        Assert.That(await Page.InnerTextAsync("body"), Does.Not.Contain("An unhandled error has occurred"));
    }
}
