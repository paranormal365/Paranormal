using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// Paying a seller (store sellers, backlog 251, P10): the store records a payment made outside the
/// site, the seller sees it on their earnings, and a payment recorded in error is voided.
/// </summary>
/// <remarks>
/// Hazel's earnings come from the seeded delivered order and from packages other tests ship, so the
/// test pays "everything owed" — whatever that is — rather than a figure of its own.
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreSellerPayoutTests : BenTestBase
{
    [Test]
    [Description("The store records a payment of everything Hazel is owed; she sees it on My Earnings; it is voided and owed again.")]
    public async Task Record_a_seller_payment()
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/store/sellers");
        await WaitForTheCircuitAsync();
        var row = Page.Locator("[data-testid=seller-row]").Filter(new() { HasText = "Hazel Marsh" });
        await Expect(row).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await row.GetByRole(AriaRole.Link, new() { Name = "Hazel Marsh" }).ClickAsync();
        await Page.WaitForURLAsync(new Regex("/admin/store/sellers/[0-9a-f-]{36}$"), new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=earnings-summary]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        var payoutsBefore = await Page.Locator("[data-testid=earnings-payout]").CountAsync();

        await ClickUntilAsync(Page.Locator("#seller-record-payment"), Page.Locator("#pay-all"));
        await Page.Locator("#pay-all").CheckAsync();
        await FillAndConfirmAsync("#pay-reference", "E2E transfer");
        await Page.Locator("#pay-save").ClickAsync();
        await Expect(Page.Locator("[data-testid=earnings-payout]")).ToHaveCountAsync(payoutsBefore + 1, new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=earnings-payout]").First).ToContainTextAsync("E2E transfer");

        // Hazel sees it.
        await LoginAsync(SellerEmail, SellerPassword);
        await Page.GotoAsync($"{BaseUrl}/store/selling/earnings");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid=earnings-payout]").First).ToContainTextAsync("E2E transfer", new() { Timeout = 30_000 });

        // Recorded in error: voided, and owed again.
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/store/sellers");
        await WaitForTheCircuitAsync();
        await Page.Locator("[data-testid=seller-row]").Filter(new() { HasText = "Hazel Marsh" }).GetByRole(AriaRole.Link, new() { Name = "Hazel Marsh" }).ClickAsync();
        await Expect(Page.Locator("[data-testid=payout-void]").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await ClickUntilAsync(Page.Locator("[data-testid=payout-void]").First, Page.Locator("#void-reason"));
        await FillAndConfirmAsync("#void-reason", "Recorded by the e2e run");
        await Page.Locator("#void-save").ClickAsync();
        await Expect(Page.Locator("[data-testid=earnings-payout]").First).ToContainTextAsync("voided", new() { Timeout = 30_000 });
    }
}
