using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A product page's extras (store sellers, backlog 251, P14): the item's own returns and warranty
/// words, videos in the gallery, and the store's reviews switch.
/// </summary>
/// <remarks>Seeded: Hazel's REM pod carries return and warranty words. The tests put back what they change.</remarks>
[TestFixture]
[Category("Store")]
public class StorePageExtrasTests : BenTestBase
{
    private const string RemPod = "a1000000-0000-0000-0000-000000000028";
    private const string ThermometerV2 = "a1000000-0000-0000-0000-000000000031";

    private static string VideoFixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Media", "hallway-camera.mp4");

    [Test]
    [Description("The REM pod's page shows its own returns words and its warranty.")]
    public async Task The_page_shows_the_items_own_returns_and_warranty()
    {
        await Page.GotoAsync($"{BaseUrl}/store/p/hand-built-rem-pod");
        await WaitForTheCircuitAsync();
        var warranty = Page.Locator("[data-testid=product-warranty]");
        await Expect(warranty).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await warranty.Locator("summary").ClickAsync();
        await Expect(warranty).ToContainTextAsync("Repaired or replaced free for a year");
        await Expect(Page.Locator("[data-testid=product-return-words]")).ToHaveTextAsync("Unused and in its box, please — each one is built to order.");
    }

    [Test]
    [Description("Hazel adds a video on the Page tab; it plays in her item's gallery; she removes it again.")]
    public async Task A_seller_adds_a_video_to_the_gallery()
    {
        await LoginAsync(SellerEmail, SellerPassword);
        await Page.GotoAsync($"{BaseUrl}/store/selling/items/{RemPod}?tab=page");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("#extras-returns")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#extras-reviews")).ToHaveCountAsync(0);   // the store's switch, not hers
        await Expect(Page.Locator("[data-testid=extras-reviews-state]")).ToContainTextAsync("Reviews are on");

        await FillAndConfirmAsync("#video-title", "Setting it up");
        await Page.Locator("#video-input").SetInputFilesAsync(VideoFixture);
        await Expect(Page.Locator("#video-upload")).ToBeEnabledAsync(new() { Timeout = 15_000 });
        await Page.Locator("#video-upload").ClickAsync();
        var added = Page.Locator("[data-testid=extras-video]", new() { HasText = "Setting it up" });
        await Expect(added).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("#extras-refusal")).ToHaveCountAsync(0);

        await Page.GotoAsync($"{BaseUrl}/store/p/hand-built-rem-pod");
        await WaitForTheCircuitAsync();
        await ClickUntilAsync(Page.Locator("[data-testid=gallery-video-thumb]").Last, Page.Locator("[data-testid=gallery-video]"));
        var status = await Page.EvaluateAsync<int>(
            "async () => (await fetch(document.querySelector('[data-testid=gallery-video] source').src, { headers: { Range: 'bytes=0-15' } })).status");
        Assert.That(status, Is.EqualTo(206), "the video should serve in ranges");

        // Tidy up.
        await Page.GotoAsync($"{BaseUrl}/store/selling/items/{RemPod}?tab=page");
        await WaitForTheCircuitAsync();
        await Page.Locator("[data-testid=extras-video]", new() { HasText = "Setting it up" }).Locator("[data-testid=extras-video-remove]").ClickAsync();
        await Expect(Page.Locator("[data-testid=extras-video]", new() { HasText = "Setting it up" })).ToHaveCountAsync(0, new() { Timeout = 15_000 });
    }

    [Test]
    [Description("The store switches the Field Thermometer v2's reviews off: its page has no reviews; then back on.")]
    public async Task The_store_switches_an_items_reviews_off()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await SetReviewsAsync(on: false);
        try
        {
            await Page.GotoAsync($"{BaseUrl}/store/p/field-thermometer-v2");
            await WaitForTheCircuitAsync();
            await Expect(Page.Locator("[data-testid=product-price]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Page.Locator("#reviews")).ToHaveCountAsync(0);
        }
        finally
        {
            await SetReviewsAsync(on: true);
        }
        await Page.GotoAsync($"{BaseUrl}/store/p/field-thermometer-v2");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("#reviews")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    private async Task SetReviewsAsync(bool on)
    {
        await Page.GotoAsync($"{BaseUrl}/admin/store/products/{ThermometerV2}/edit?tab=page");
        await WaitForTheCircuitAsync();
        var toggle = Page.Locator("#extras-reviews");
        await Expect(toggle).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await toggle.IsCheckedAsync() != on) await toggle.ClickAsync();
        await Expect(toggle).ToBeCheckedAsync(new() { Checked = on });
        await Page.Locator("#extras-save").ClickAsync();
        await Expect(Page.GetByText("Saved.").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
    }
}
