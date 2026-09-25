using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The order desk (storefront S5.5), as a SuperAdmin: ship with tracking or without, refunds and their
/// refusals, the letters, the filters and the CSV. Each test places its own paid guest order through
/// the store's API in test checkout.
/// </summary>
[TestFixture]
[Category("Store")]
public class StoreAdminOrderTests : BenTestBase
{
    private static string Unique(string name) => $"{name} {Guid.NewGuid().ToString("N")[..6]}";
    private static string Buyer() => $"desk-{Guid.NewGuid().ToString("N")[..8]}@example.com";

    private async Task<(Guid OrderId, string Email, JsonElement Product)> PaidOrderAsync(decimal price = 30m)
    {
        using var api = await StoreTestApi.OpenAsync();
        var product = await api.BuyableAsync(Unique("Desk meter"), price);
        var email = Buyer();
        return (await StoreTestApi.PaidGuestOrderAsync(product, email), email, product);
    }

    private async Task OpenOrderAsync(Guid id)
    {
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/store/orders/{id}");
        await Expect(Page.Locator("[data-testid=order-actions]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    [Description("Ship with a carrier and tracking number: the link is built from the two, the order is Shipped, and the buyer is emailed.")]
    public async Task Orders_ship_emails_the_buyer_with_the_tracking_link()
    {
        var (id, email, _) = await PaidOrderAsync();
        await OpenOrderAsync(id);

        await ClickUntilAsync(Page.Locator("#order-ship"), Page.Locator("#ship-carrier"));
        await Page.SelectOptionAsync("#ship-carrier", "USPS");
        await FillAndConfirmAsync("#ship-tracking", "9400111122223333");
        await Expect(Page.Locator("[data-testid=ship-link-preview]")).ToContainTextAsync("tools.usps.com");
        await Page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "store-desk-ship-dialog.png") });
        await Page.Locator("#ship-confirm").ClickAsync();

        await Expect(Page.Locator("[data-testid=order-status]")).ToHaveTextAsync("Shipped", new() { Timeout = 30_000 });
        await Page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "store-desk-order.png"), FullPage = true });
        await Page.SetViewportSizeAsync(375, 812);
        await Page.ReloadAsync();   // the layout decides phone or desktop as it loads
        await Expect(Page.Locator("[data-testid=order-actions]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Page.ScreenshotAsync(new() { Path = Path.Combine(Path.GetTempPath(), "store-desk-order-phone.png"), FullPage = true });
        var sideways = await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(sideways, Is.False, "the order desk scrolls sideways on a phone");
        await Page.SetViewportSizeAsync(1280, 800);
        await Expect(Page.Locator("[data-testid=order-tracking]")).ToHaveAttributeAsync("href", new Regex("tools\\.usps\\.com.*9400111122223333"));
        var letter = await TryLetterFromTheOutboxAsync(email, body => body.Contains("9400111122223333"));
        Assert.That(letter, Is.Not.Null, "the buyer's shipped letter did not reach the outbox");
    }

    [Test]
    [Description("An order with a seller's item is two packages: the store ships its own, the order reads Partially shipped; the seller ships theirs from My Packages, and it reads Shipped.")]
    public async Task Each_package_ships_on_its_own()
    {
        using var api = await StoreTestApi.OpenAsync();
        var ours = await api.BuyableAsync(Unique("Desk torch"), 14m);
        var hers = await api.GiveToSellerAsync(await api.BuyableAsync(Unique("Hand-wound spirit box"), 22m), SellerEmail);
        var email = Buyer();
        var id = await StoreTestApi.PaidGuestOrderAsync([ours, hers], email);
        var number = (await api.SendAsync(HttpMethod.Get, $"/api/admin/store/orders/{id}")).GetProperty("orderNumber").GetInt32();

        // The store ships package 1.
        await OpenOrderAsync(id);
        await Expect(Page.Locator("[data-testid=order-package]")).ToHaveCountAsync(2);
        await Expect(Page.Locator("[data-testid=order-package][data-number='2']")).ToContainTextAsync("Ships from Hazel Marsh");
        await ClickUntilAsync(Page.Locator("#parcel-1-ship"), Page.Locator("#ship-carrier"));
        await Page.SelectOptionAsync("#ship-carrier", "USPS");
        await FillAndConfirmAsync("#ship-tracking", "9400111122224444");
        await Page.Locator("#ship-confirm").ClickAsync();
        await Expect(Page.Locator("[data-testid=order-status]")).ToHaveTextAsync("Partially shipped", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=order-package][data-number='1'] [data-testid=order-package-status]")).ToHaveTextAsync("Shipped");
        await Expect(Page.Locator("[data-testid=order-package][data-number='2'] [data-testid=order-package-status]")).ToHaveTextAsync("Being prepared");
        var first = await TryLetterFromTheOutboxAsync(email, body => body.Contains("9400111122224444") && body.Contains("package 1 of 2"));
        Assert.That(first, Is.Not.Null, "the buyer's letter for package 1 did not reach the outbox");

        // Hazel ships package 2 from her own page.
        await LoginAsync(SellerEmail, SellerPassword);
        await Page.GotoAsync($"{BaseUrl}/store/selling/packages");
        await WaitForTheCircuitAsync();
        var card = Page.Locator($"[data-testid=seller-package][data-order='{number}']");
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(card.Locator("[data-testid=seller-package-ship-to]")).ToContainTextAsync("3 Birch Rd");
        await ClickUntilAsync(card.Locator("[data-testid=seller-package-ship]"), Page.Locator("#ship-carrier"));
        await Page.SelectOptionAsync("#ship-carrier", "UPS");
        await Page.Locator("#ship-no-tracking").CheckAsync();
        await Page.Locator("#ship-confirm").ClickAsync();
        await Expect(Page.Locator("#ship-confirm")).ToHaveCountAsync(0, new() { Timeout = 15_000 });

        var order = await api.SendAsync(HttpMethod.Get, $"/api/admin/store/orders/{id}");
        Assert.That(order.GetProperty("status").GetInt32(), Is.EqualTo(3), "both packages have gone, so the order is Shipped");
    }

    [Test]
    [Description("A tracking number is required — unless 'No tracking provided' is chosen, which ships without one and tells the buyer so.")]
    public async Task Orders_ship_without_tracking_only_when_it_says_so()
    {
        var (id, email, _) = await PaidOrderAsync();
        await OpenOrderAsync(id);

        await ClickUntilAsync(Page.Locator("#order-ship"), Page.Locator("#ship-carrier"));
        await Page.SelectOptionAsync("#ship-carrier", "UPS");
        await Page.Locator("#ship-confirm").ClickAsync();
        await Expect(Page.Locator("#ship-refusal")).ToContainTextAsync("No tracking provided", new() { Timeout = 15_000 });

        await Page.Locator("#ship-no-tracking").CheckAsync();
        await Expect(Page.Locator("#ship-tracking")).ToHaveCountAsync(0);
        await Page.Locator("#ship-confirm").ClickAsync();

        await Expect(Page.Locator("[data-testid=order-status]")).ToHaveTextAsync("Shipped", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=order-shipment]")).ToContainTextAsync("No tracking provided");
        await Expect(Page.Locator("#order-correct-tracking")).ToHaveTextAsync("Add tracking…");
        var letter = await TryLetterFromTheOutboxAsync(email, body => body.Contains("without tracking"));
        Assert.That(letter, Is.Not.Null, "the buyer was not told it went without tracking");
    }

    [Test]
    [Description("A refund over the balance is refused with the balance named; a refund of the items goes through and restocks.")]
    public async Task Orders_refund_refusal_names_the_balance_then_a_refund_goes_through()
    {
        var (id, email, _) = await PaidOrderAsync(price: 30m);
        await OpenOrderAsync(id);
        var refundable = await Page.Locator("[data-testid=order-refundable]").InnerTextAsync();

        await ClickUntilAsync(Page.Locator("#order-refund"), Page.Locator("#refund-reason"));
        await Page.Locator("#refund-by-amount").CheckAsync();
        await FillAndConfirmAsync("#refund-amount", "9999");
        await FillAndConfirmAsync("#refund-reason", "Too much, on purpose");
        await Page.Locator("#refund-confirm").ClickAsync();
        await Expect(Page.Locator("#refund-refusal")).ToContainTextAsync($"{refundable} still refundable", new() { Timeout = 15_000 });

        await Page.Locator("#refund-by-items").CheckAsync();
        await FillAndConfirmAsync("input[id^=refund-qty-]", "1");
        await Page.Locator("#refund-confirm").ClickAsync();

        await Expect(Page.Locator("[data-testid=order-refunds]")).ToContainTextAsync("Refunded", new() { Timeout = 30_000 });
        var letter = await TryLetterFromTheOutboxAsync(email, body => body.Contains("refunded"));
        Assert.That(letter, Is.Not.Null, "the buyer's refund letter did not reach the outbox");
    }

    [Test]
    [Description("Resend email… queues the receipt again to the buyer.")]
    public async Task Orders_resend_queues_the_confirmation()
    {
        var (id, email, _) = await PaidOrderAsync();
        await OpenOrderAsync(id);

        await ClickUntilAsync(Page.Locator("#order-resend"), Page.Locator("#resend-kind"));
        await Page.SelectOptionAsync("#resend-kind", "confirmation");
        await Page.Locator("#resend-confirm").ClickAsync();

        await Expect(Page.Locator("[data-testid=order-history]")).ToContainTextAsync("receipt was re-sent", new() { Timeout = 30_000 });
        Assert.That(await TryLetterFromTheOutboxAsync(email, body => body.Contains("Thank you")), Is.Not.Null);
    }

    [Test]
    [Description("The dashboard's To pack tile opens the list filtered to paid orders, and the new order is on it.")]
    public async Task Dashboard_tiles_open_the_filtered_list()
    {
        var (id, _, _) = await PaidOrderAsync();
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/store");

        await ClickUntilAsync(Page.Locator("a[href='/admin/store/orders?status=Paid']"), Page.Locator("#orders"));
        Assert.That(Page.Url, Does.Contain("status=Paid"));
        await Expect(Page.Locator($"a[href='/admin/store/orders/{id}']").First).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    [Description("Export CSV downloads the filtered orders as a CSV file.")]
    public async Task Orders_export_downloads_a_csv()
    {
        var (id, email, _) = await PaidOrderAsync();
        await LoginAsync(SuperAdminEmail, SuperAdminPassword);
        await Page.GotoAsync($"{BaseUrl}/admin/store/orders?q={Uri.EscapeDataString(email)}");
        await Expect(Page.Locator("[data-testid=order-row]")).ToHaveCountAsync(1, new() { Timeout = 30_000 });

        var download = await Page.RunAndWaitForDownloadAsync(() => Page.Locator("#orders-export").ClickAsync());
        var path = await download.PathAsync();
        var text = await File.ReadAllTextAsync(path!);
        Assert.That(download.SuggestedFilename, Does.EndWith(".csv"));
        Assert.That(text, Does.Contain(email));
    }
}
