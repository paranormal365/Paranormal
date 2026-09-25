using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// A buyer's orders (storefront S4.13): My Orders, one order, its invoice, "Find my order", and all
/// of them still open with the shop switched off.
/// </summary>
/// <remarks>
/// Reads the seven demo orders StoreDemoSeeder gives the e2e database — opened by their fixed ids,
/// because their numbers are whichever were free when they were seeded. Sarah (the seeded user) has
/// five, James (the seeded member) one, and a guest one.
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreOrderTests : BenTestBase
{
    private static string Seeded(int n) => $"a1000000-0000-0000-0000-{n:D12}";
    private static readonly string SarahPaid = Seeded(101), SarahShipped = Seeded(102), SarahRefunded = Seeded(104), SarahAbandoned = Seeded(107);

    private ILocator Cards => Page.Locator("[data-testid=order-card]");

    private async Task OpenAsync(string path)
    {
        await Page.GotoAsync($"{BaseUrl}{path}");
        await WaitForTheCircuitAsync();
    }

    [Test]
    [Description("A signed-in buyer's paid orders are listed under My Orders, each opening its own page.")]
    public async Task A_signed_in_buyer_sees_the_order_under_My_Orders()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenAsync("/store/orders");

        await Expect(Cards.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator($"a[href='/store/orders/{SarahPaid}']")).ToBeVisibleAsync();
        await Expect(Page.Locator($"a[href='/store/orders/{SarahShipped}'] [data-testid=order-status]")).ToHaveTextAsync("Shipped");

        await Page.Locator($"a[href='/store/orders/{SarahShipped}']").ClickAsync();
        await Expect(Page.Locator("[data-testid=order-number]")).ToContainTextAsync("Order no.", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=order-tracking]")).ToHaveAttributeAsync("href", new Regex("usps\\.com"));
        await Expect(Page.Locator("[data-testid=order-item]")).ToContainTextAsync("H1n Handy Recorder");
    }

    [Test]
    [Description("A checkout that was never paid for is not an order: it is not listed.")]
    public async Task Mine_hides_abandoned_checkouts()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenAsync("/store/orders");
        await Expect(Cards.First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        await Expect(Page.Locator($"a[href='/store/orders/{SarahAbandoned}']")).ToHaveCountAsync(0);
    }

    [Test]
    [Description("A refunded order's page and invoice both show the refund, and the invoice balance is nothing.")]
    public async Task A_refunded_order_shows_it_on_the_page_and_the_invoice()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenAsync($"/store/orders/{SarahRefunded}");

        await Expect(Page.Locator("[data-testid=order-status]")).ToHaveTextAsync("Refunded", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=order-refunds]")).ToContainTextAsync("Arrived damaged");
        await Expect(Page.Locator("[data-testid=order-tax]")).ToContainTextAsync("on shipping");

        await Page.Locator("#order-invoice").ClickAsync();
        await Expect(Page.Locator("[data-testid=invoice-sheet]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=invoice-balance]")).ToHaveTextAsync("$0.00");
        await Expect(Page.Locator("#invoice-print")).ToBeVisibleAsync();
    }

    [Test]
    [Description("Somebody else's order reads as nothing there — never 'not yours'.")]
    public async Task A_stranger_sees_no_order()
    {
        await LoginAsync(MemberEmail, MemberPassword);
        await OpenAsync($"/store/orders/{SarahPaid}");

        await Expect(Page.GetByText("There's no order here")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=order-number]")).ToHaveCountAsync(0);
    }

    [Test]
    [Description("Signed out, My Orders and a member's order ask for a sign-in and come back.")]
    public async Task Signed_out_goes_to_sign_in()
    {
        await OpenAsync("/store/orders");
        await Page.WaitForURLAsync(new Regex(@"/login\?returnUrl=%2Fstore%2Forders$"), new() { Timeout = 30_000 });

        await OpenAsync($"/store/orders/{SarahPaid}");
        await Page.WaitForURLAsync(new Regex(@"/login\?returnUrl=%2Fstore%2Forders%2F"), new() { Timeout = 30_000 });
    }

    [Test]
    [Description("A guest buys in test checkout, and the letter's link opens the order with no account.")]
    public async Task Guest_FindsTheirOrder_ByTheEmailedLink()
    {
        // Pays through the test checkout's panel; the order link is the subject, not the payment.
        if (StoreTestApi.RealStripe) Assert.Ignore("Pays through the test checkout; real payments are StoreRealStripeTests' to prove.");
        using var api = await StoreTestApi.OpenAsync();
        var name = $"Guest letter bag {Guid.NewGuid().ToString("N")[..6]}";
        var product = await api.BuyableAsync(name, 21m);
        var email = $"guest-{Guid.NewGuid().ToString("N")[..8]}@example.com";

        await OpenAsync($"/store/p/{product.GetProperty("slug").GetString()}");
        await ClickUntilAsync(Page.Locator("[data-testid=add-to-cart]"), Page.Locator("[data-testid=cart-drawer]"));
        await OpenAsync("/store/checkout");
        await FillAndConfirmAsync("#checkout-email", email);
        await FillAndConfirmAsync("#shipping-full-name", "Gail Guest");
        await FillAndConfirmAsync("#shipping-phone", "615-555-0199");
        await FillAndConfirmAsync("#shipping-street1", "5 Pine St");
        await FillAndConfirmAsync("#shipping-city", "Nashville");
        await Page.SelectOptionAsync("#shipping-state", "TN");
        await FillAndConfirmAsync("#shipping-zip", "37203");
        await Page.Locator("#checkout-terms").CheckAsync();
        await ClickUntilAsync(Page.Locator("#store-continue-payment"), Page.Locator("[data-testid=checkout-fake]"));
        await Page.Locator("#store-place-order").ClickAsync();
        var number = await Page.Locator("[data-testid=complete-order-number]").InnerTextAsync(new() { Timeout = 60_000 });

        // The thank-you page's own way to the order.
        await Expect(Page.Locator("#complete-see-order")).ToHaveAttributeAsync("href", new Regex(@"/store/orders/[0-9a-f-]+\?t="));

        // The confirmation letter's link, in a browser that has never seen this cart.
        var link = await LinkFromTheOutboxAsync(email, "/store/orders/");
        await Context.ClearCookiesAsync();
        await OpenAsync(link);
        await Expect(Page.Locator("[data-testid=order-number]")).ToHaveTextAsync($"Order no. {number}", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=order-item]")).ToContainTextAsync(name);
    }

    [Test]
    [Description("Find my order says the same thing either way, and a member's match is emailed a sign-in link, not a private one.")]
    public async Task A_member_lookup_emails_a_sign_in_link()
    {
        await LoginAsync(UserEmail, UserPassword);
        await OpenAsync($"/store/orders/{SarahPaid}");
        var number = Regex.Match(await Page.Locator("[data-testid=order-number]").InnerTextAsync(new() { Timeout = 30_000 }), @"\d+").Value;

        await OpenAsync("/store/orders/lookup");
        await FillAndConfirmAsync("#lookup-number", "999999999");
        await FillAndConfirmAsync("#lookup-email", "nobody@example.com");
        await Page.Locator("#lookup-send").ClickAsync();
        var miss = await Page.Locator("[data-testid=lookup-sent]").InnerTextAsync(new() { Timeout = 30_000 });

        await Page.GetByRole(AriaRole.Button, new() { Name = "Look up another order" }).ClickAsync();
        await FillAndConfirmAsync("#lookup-number", number);
        await FillAndConfirmAsync("#lookup-email", UserEmail);
        await Page.Locator("#lookup-send").ClickAsync();
        var hit = await Page.Locator("[data-testid=lookup-sent]").InnerTextAsync(new() { Timeout = 30_000 });

        Assert.That(hit, Is.EqualTo(miss), "the answer must not tell a stranger whether the email bought anything");
        var letter = await TryLetterFromTheOutboxAsync(UserEmail, body => body.Contains("Sign in to see this order"));
        Assert.That(letter, Is.Not.Null, "no sign-in letter reached the outbox");
        Assert.That(letter, Does.Not.Contain("?t="), "a member's letter must not carry the private link");
    }

    [Test]
    [Description("With the shop switched off, a buyer's orders, invoice, lookup and thank-you page all still open, and My Orders stays in the menu.")]
    public async Task Order_pages_still_open_with_the_store_off()
    {
        var wasOn = await SetTheStoreAsync(false);
        try
        {
            await LoginAsync(UserEmail, UserPassword);

            await OpenAsync("/store/orders");
            await Expect(Cards.First).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await Expect(Page.Locator("nav a[href='/store/orders']").First).ToBeAttachedAsync();

            await OpenAsync($"/store/orders/{SarahPaid}");
            await Expect(Page.Locator("[data-testid=order-number]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await OpenAsync($"/store/orders/{SarahPaid}/invoice");
            await Expect(Page.Locator("[data-testid=invoice-sheet]")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await OpenAsync("/store/orders/lookup");
            await Expect(Page.Locator("#lookup-number")).ToBeVisibleAsync(new() { Timeout = 30_000 });
            await OpenAsync($"/store/checkout/complete?order={SarahPaid}");
            await Expect(Page.GetByText("Thank you for your order!")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        }
        finally
        {
            await PutTheStoreBackAsync(wasOn);
        }
    }
}
