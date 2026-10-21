using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// What each plan includes, and hosted events, below the bands on the pricing page (Ben, 10/13/2026).
/// </summary>
/// <remarks>
/// Ben: every band gets a small button to show what it includes, built from whatever is switched on
/// ("I just turned on the Video Editor, so it would now display it for each plan"); the plans after
/// the first say how they differ from it; and the page sells events with what one includes. So
/// these read the switches and settings from the API and hold the page to them, rather than to a
/// list typed into the test.
/// </remarks>
[TestFixture]
[Category("PublicSurface")]
public class PricingWhatsIncludedTests : BenTestBase
{
    private ILocator Bands => Page.Locator("[data-testid='pricing-band']");

    private async Task<JsonElement> ApiAsync(string path)
    {
        using var http = new HttpClient { BaseAddress = new Uri(ApiUrl) };
        return JsonDocument.Parse(await http.GetStringAsync(path)).RootElement.Clone();
    }

    private async Task OpenAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/pricing");
        await WaitForTheCircuitAsync();
        await Expect(Bands.First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }

    [Test]
    [Description("Every band's What's included link lands on that plan in the comparison below.")]
    public async Task Each_band_jumps_to_its_own_plan_below()
    {
        await OpenAsync();

        var links = Page.Locator("[data-testid='pricing-whats-included']");
        var count = await Bands.CountAsync();
        Assert.That(await links.CountAsync(), Is.EqualTo(count), "One What's included link per band.");

        var cards = Page.Locator("[data-testid='plan-inclusions']");
        Assert.That(await cards.CountAsync(), Is.EqualTo(count), "One plan in the comparison per band.");

        // The last band's: the one furthest down, so a link that went nowhere would leave it off screen.
        var last = links.Nth(count - 1);
        var href = (await last.GetAttributeAsync("href"))!;
        Assert.That(href, Does.Match(@"^pricing#included-[0-9a-f]{32}$"));
        var target = href[href.IndexOf('#')..];

        await last.ClickAsync();
        var card = Page.Locator(target);
        await Expect(card).ToBeInViewportAsync(new() { Timeout = 10_000 });
        Assert.That(Page.Url, Does.EndWith(target), "The link changes the address, so it can be shared.");

        var name = (await Bands.Nth(count - 1).Locator("h3").InnerTextAsync()).Trim();
        await Expect(card.Locator("h3")).ToHaveTextAsync(name);
    }

    [Test]
    [Description("The comparison lists the parts of the site that are switched on, and nothing switched off.")]
    public async Task The_list_follows_the_feature_switches()
    {
        var features = (await ApiAsync("/api/public/site-features")).GetProperty("features");
        await OpenAsync();

        var platform = Page.Locator("[data-testid='plan-platform']");
        await Expect(platform).ToBeVisibleAsync(new() { Timeout = 15_000 });

        foreach (var (key, words) in new[]
                 {
                     ("features.video-editor", "The video editor"),
                     ("features.equipment", "Equipment: your own kit"),
                     ("features.media-library", "A media library"),
                     ("features.public-feed", "Posts on the public feed"),
                 })
        {
            var on = features.GetProperty(key).GetBoolean();
            await Expect(platform.GetByText(words)).ToHaveCountAsync(on ? 1 : 0);
        }

        // The plans after the first are compared with it, not with the one below them.
        var first = (await Page.Locator("[data-testid='plan-inclusions'] h3").First.InnerTextAsync()).Trim();
        var others = await Page.Locator("[data-testid='plan-inclusions']").CountAsync();
        if (others > 1)
            await Expect(Page.Locator("[data-testid='plan-inclusions']").Nth(1))
                .ToContainTextAsync($"Everything in {first}, with these differences", new() { IgnoreCase = true });
    }

    [Test]
    [Description("Hosted events: what one includes, and the price the site charges, or that plans include them.")]
    public async Task Events_say_what_they_include_and_what_they_cost()
    {
        var features = (await ApiAsync("/api/public/site-features")).GetProperty("features");
        if (!features.GetProperty("features.events").GetBoolean())
            Assert.Ignore("Events are switched off on this site, so the pricing page has no events section.");

        var offer = (await ApiAsync("/api/public/pricing/extras")).GetProperty("events");

        await OpenAsync();

        var section = Page.Locator("[data-testid='pricing-events']");
        await Expect(section).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Expect(section.Locator("[data-testid='pricing-event-features']")).ToContainTextAsync("Menus for each night");
        await Expect(section.Locator("[data-testid='pricing-event-features']")).ToContainTextAsync("dining tables");

        // No plan includes events (Ben, 10/21/2026): the card always sells them one at a time, at the
        // price in the settings, and never says a plan covers them.
        var card = section.Locator("[data-testid='pricing-event-offer']");
        var price = offer.GetProperty("unitPrice").GetDecimal();
        await Expect(card).ToContainTextAsync("per event");
        await Expect(card).ToContainTextAsync($"${price:0}");
        await Expect(card).Not.ToContainTextAsync("Every plan includes hosted events");
        await Expect(card.GetByRole(AriaRole.Link, new() { Name = "Sign in to buy events" })).ToBeVisibleAsync();
    }

    [Test]
    [Description("The free band no longer says a bare \"No limits\".")]
    public async Task The_free_band_says_its_limits_are_about_public_work()
    {
        await OpenAsync();
        var free = Bands.Filter(new() { HasText = "Free" }).Filter(new() { HasText = "forever" });
        if (await free.CountAsync() == 0)
            Assert.Ignore("This site's price list has no free band.");

        await Expect(free.First.Locator(".pricing-band__limits")).Not.ToHaveTextAsync("No limits");
    }
}
