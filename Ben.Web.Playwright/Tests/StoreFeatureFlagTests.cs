using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The store switch hides the shop and nothing else (storefront S2.10): with it off the shop's
/// pages are "Page not found", while the back office still works and the pictures still serve.
/// </summary>
[TestFixture]
[Category("Store")]
public class StoreFeatureFlagTests : BenTestBase
{
    [Test]
    [Description("Store off: shop pages are 'Page not found'; the admin store screens and pictures still work; the switch is on Site Settings.")]
    public async Task The_switch_hides_the_shop_not_the_back_office()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        var wasOn = await SetTheStoreAsync(on: false);
        Assert.That(wasOn, Is.Not.Null, "Could not switch the store off.");
        try
        {
            foreach (var path in new[] { "/store", "/store/products", "/store/p/k-ii-emf-meter" })
            {
                await Page.GotoAsync($"{BaseUrl}{path}");
                await Expect(Page.GetByText("There is nothing at this address.")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            }

            await Page.GotoAsync($"{BaseUrl}/admin/store/products");
            await Expect(Page.Locator("[data-testid=product-row]").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

            var picture = await Page.APIRequest.GetAsync($"{BaseUrl}/media/store-image/a1000000-0000-0000-0021-000000000200");
            Assert.That(picture.Status, Is.EqualTo(200), "Store pictures must serve while the shop is dark.");

            await Page.GotoAsync($"{BaseUrl}/admin/site-settings?setting=features.store");
            await Expect(Page.Locator("#sw-features\\.store")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Page.Locator("#sw-features\\.store")).Not.ToBeCheckedAsync();
        }
        finally { await PutTheStoreBackAsync(wasOn); }
    }
}
