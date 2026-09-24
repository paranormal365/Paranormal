using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// One product's page (storefront S2.9): choices change the SKU, price and stock; low stock says so;
/// pictures serve to anybody; the description is styled; a hidden product opens only for a
/// SuperAdmin's preview. Reads the demo store by slug.
/// </summary>
[TestFixture]
[Category("Store")]
public class StoreProductTests : BenTestBase
{
    [Test]
    [Description("Choosing Camo shows its SKU and price, 'Sold out', the disabled button and a muted ring — not a line-through.")]
    public async Task Product_OptionsChangeSkuPriceAndStock()
    {
        await Page.GotoAsync($"{BaseUrl}/store/p/p-sb7-spirit-box");
        await Expect(Page.Locator("[data-testid=product-sku]")).ToHaveTextAsync("PSB7-BLACK", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=product-price]")).ToContainTextAsync("$79.99");
        await Expect(Page.Locator("[data-testid=product-stock]")).ToContainTextAsync("In stock");
        await Expect(Page.Locator("[data-testid=sold-out-button]")).ToHaveCountAsync(0);

        await WaitForTheCircuitAsync();
        var camo = Page.Locator("[data-testid=option-value][title^=Camo]");
        await Expect(camo).ToHaveClassAsync(new Regex("ben-selector--soldout"));
        await ClickUntilAsync(camo, Page.Locator("[data-testid=product-sku]", new() { HasTextString = "PSB7-CAMO" }));

        await Expect(Page.Locator("[data-testid=product-price]")).ToContainTextAsync("$84.99");
        await Expect(Page.Locator("[data-testid=product-stock]")).ToContainTextAsync("Sold out");
        await Expect(Page.Locator("[data-testid=sold-out-button]")).ToBeDisabledAsync();
        var decoration = await camo.EvaluateAsync<string>("e => getComputedStyle(e).textDecorationLine");
        Assert.That(decoration, Does.Not.Contain("line-through"));
    }

    [Test]
    [Description("Two left reads 'Only 2 left'.")]
    public async Task Product_ShowsLowStockNote()
    {
        await Page.GotoAsync($"{BaseUrl}/store/p/rem-pod");
        await Expect(Page.Locator("[data-testid=product-stock]")).ToContainTextAsync("Only 2 left", new() { Timeout = 30_000 });
    }

    [Test]
    [Description("A product picture serves to somebody with no account, as a JPEG.")]
    public async Task Product_image_is_served_anonymously()
    {
        var response = await Page.APIRequest.GetAsync($"{BaseUrl}/media/store-image/a1000000-0000-0000-0021-000000000200");
        Assert.That(response.Status, Is.EqualTo(200));
        Assert.That(response.Headers["content-type"], Does.StartWith("image/jpeg"));
    }

    [Test]
    [Description("The description's paragraphs get the store's prose styling, not the browser's defaults.")]
    public async Task Product_description_is_styled()
    {
        await Page.GotoAsync($"{BaseUrl}/store/p/k-ii-emf-meter");
        var paragraph = Page.Locator("[data-testid=product-description] p").First;
        await Expect(paragraph).ToBeVisibleAsync(new() { Timeout = 30_000 });
        // The live page replaces the server's copy of the page; a paragraph read in that moment is
        // detached and has no style at all (NaN). Measure the live one.
        await WaitForTheCircuitAsync();

        var ratio = await paragraph.EvaluateAsync<double>(
            "e => { const s = getComputedStyle(e); return parseFloat(s.lineHeight) / parseFloat(s.fontSize); }");
        Assert.That(ratio, Is.EqualTo(1.65).Within(0.02), "ben-store-description's line height did not reach the paragraph.");
    }

    [Test]
    [Description("A member asking for a preview of a hidden product gets 'Page not found'; a SuperAdmin gets the preview banner.")]
    public async Task SuperAdmin_previews_an_inactive_product()
    {
        await LoginAsync(UserEmail, UserPassword);
        await Page.GotoAsync($"{BaseUrl}/store/p/boo-buddy?preview=1");
        await Expect(Page.Locator("[data-testid=store-not-found]")).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/store/p/boo-buddy?preview=1");
        await Expect(Page.Locator("[data-testid=store-preview]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("h1")).ToContainTextAsync("Boo Buddy");
        await Expect(Page.Locator("[data-testid=sold-out-button]")).ToHaveTextAsync("Not on sale");
    }
}
