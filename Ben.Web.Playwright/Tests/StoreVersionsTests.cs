using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Versions (store sellers, backlog 251, P13): a replaced item's page says it's no longer made and
/// links forward; the new one links back. A seller starts a new version of an item from its Versions
/// tab and lands in the new draft's editor.
/// </summary>
/// <remarks>
/// Seeded (StoreDemoSeeder): the Field Thermometer v1 is off sale, replaced by v2, which is on sale.
/// The seller test deletes the draft it starts, so the REM pod has no newer version on the next run.
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreVersionsTests : BenTestBase
{
    [Test]
    [Description("The old Field Thermometer says it's no longer made and links to v2, which links back.")]
    public async Task A_replaced_item_points_to_its_newer_version()
    {
        await Page.GotoAsync($"{BaseUrl}/store/p/field-thermometer");
        await WaitForTheCircuitAsync();
        var gone = Page.Locator("[data-testid=product-discontinued]");
        await Expect(gone).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(gone).ToContainTextAsync("No longer made");
        await Expect(Page.Locator("[data-testid=sold-out-button]")).ToHaveTextAsync("No longer made");
        await Expect(Page.Locator("[data-testid=add-to-cart]")).ToHaveCountAsync(0);

        await Page.Locator("[data-testid=product-replaced-by]").ClickAsync();
        await Page.WaitForURLAsync(new Regex("/store/p/field-thermometer-v2$"), new() { Timeout = 30_000 });
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid=product-version]")).ToHaveTextAsync("v2", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=product-older-version]")).ToContainTextAsync("Field Thermometer (v1)");
        await Expect(Page.Locator("[data-testid=add-to-cart]")).ToBeVisibleAsync();
    }

    [Test]
    [Description("Hazel starts a new version of her REM pod: a draft that links back, with her choice for the old one.")]
    public async Task A_seller_starts_a_new_version()
    {
        await LoginAsync(SellerEmail, SellerPassword);
        await Page.GotoAsync($"{BaseUrl}/store/selling/items/a1000000-0000-0000-0000-000000000028?tab=versions");
        await WaitForTheCircuitAsync();

        await ClickUntilAsync(Page.Locator("#version-start"), Page.Locator("#new-version-label"));
        await FillAndConfirmAsync("#new-version-label", "v2");
        await Page.Locator("#new-version-policy-KeepOffering").CheckAsync();
        await Page.Locator("#new-version-confirm").ClickAsync();

        await Page.WaitForURLAsync(new Regex(@"/store/selling/items/(?!a1000000-0000-0000-0000-000000000028)[0-9a-f-]{36}$"), new() { Timeout = 30_000 });
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("#seller-item-status")).ToHaveTextAsync("Draft", new() { Timeout = 30_000 });
        await Page.GotoAsync($"{Page.Url}?tab=versions");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid=version-previous]")).ToContainTextAsync("Hand-Built REM Pod", new() { Timeout = 30_000 });
        await Expect(Page.Locator("#version-policy")).ToHaveValueAsync("KeepOffering");

        // Tidy up: a draft never on sale can go, and the REM pod is free for the next run.
        await ClickUntilAsync(Page.Locator("#seller-delete"), Page.GetByRole(AriaRole.Dialog));
        await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true }).ClickAsync();
        await Page.WaitForURLAsync(new Regex("/store/selling$"), new() { Timeout = 30_000 });
    }
}
