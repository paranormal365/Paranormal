using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// The checkout (storefront S4.11), in test checkout: the harness starts the API with the pretend
/// Stripe gateway and tax service (Development, no key, <c>Stripe__AllowFakeCheckout</c>) and gives
/// the store a ship-from address, so "Place order" pays through the dev route as the webhook would.
/// </summary>
/// <remarks>
/// Every test buys its own product with a name nobody else uses, from a fresh browser — a guest
/// whose cart is that browser's cookie — so no test reads another's order. A real card and a
/// signed webhook are exercised only under <c>BEN_STRIPE_E2E=1</c> with a Stripe test key.
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreCheckoutTests : BenTestBase
{
    private static string Unique(string name) => $"{name} {Guid.NewGuid().ToString("N")[..6]}";

    private static string Slug(JsonElement product) => product.GetProperty("slug").GetString()!;

    private ILocator Refusal => Page.Locator("#checkout-refusal");

    private ILocator CurrentStep => Page.Locator(".ben-steps__item[aria-current=step] .ben-steps__label");

    private async Task AddToCartAsync(JsonElement product, int quantity = 1)
    {
        await Page.GotoAsync($"{BaseUrl}/store/p/{Slug(product)}");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid=add-to-cart]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        if (quantity != 1) await FillAndConfirmAsync("#product-quantity", quantity.ToString());
        await ClickUntilAsync(Page.Locator("[data-testid=add-to-cart]"), Page.Locator("[data-testid=cart-drawer]"));
    }

    /// <summary>From the cart page's "To checkout", the way a buyer gets there.</summary>
    private async Task OpenCheckoutAsync()
    {
        await Page.GotoAsync($"{BaseUrl}/store/cart");
        await WaitForTheCircuitAsync();
        await ClickUntilAsync(Page.Locator("#cart-to-checkout"), Page.Locator("#checkout-email"));
    }

    private async Task FillAsync(string email)
    {
        await FillAndConfirmAsync("#checkout-email", email);
        await FillAndConfirmAsync("#shipping-full-name", "Ada Buyer");
        await FillAndConfirmAsync("#shipping-phone", "615-555-0100");
        await FillAndConfirmAsync("#shipping-street1", "1 Elm St");
        await FillAndConfirmAsync("#shipping-city", "Nashville");
        await Page.SelectOptionAsync("#shipping-state", "TN");
        await FillAndConfirmAsync("#shipping-zip", "37203");
        await Page.Locator("#checkout-terms").CheckAsync();
    }

    private static string Buyer() => $"buyer-{Guid.NewGuid().ToString("N")[..8]}@example.com";

    private async Task ContinueAsync(string button = "#store-continue-payment")
        => await PressPastTheLimitAsync(button, Page.Locator("[data-testid=checkout-payment]"));

    /// <summary>
    /// Presses a checkout button and waits for <paramref name="expected"/> — and when the page says
    /// to wait a minute, waits a minute and presses again, as a buyer would.
    /// </summary>
    /// <remarks>
    /// Every browser here is a guest from the same address, and the checkout allows ten tries a
    /// minute per address, so a fixture this size meets the limit. The page's answer to it is
    /// itself under test: it must be the sentence telling the buyer to wait.
    /// </remarks>
    private async Task PressPastTheLimitAsync(string button, ILocator expected)
    {
        await Page.Locator(button).ClickAsync();
        var limited = Refusal.Filter(new() { HasText = "wait a minute" });
        var testPaymentFailed = Refusal.Filter(new() { HasText = "test payment didn't go through" });
        await Expect(expected.Or(limited).Or(testPaymentFailed)).ToBeVisibleAsync(new() { Timeout = 60_000 });
        if (await expected.IsVisibleAsync()) return;

        await Task.Delay(TimeSpan.FromSeconds(61));
        await Page.Locator(button).ClickAsync();
        await Expect(expected).ToBeVisibleAsync(new() { Timeout = 60_000 });
    }

    /// <summary>Pays in whatever mode the harness runs: the test checkout's button, or a real Stripe test card.</summary>
    private async Task PayAsync(string button = "#store-place-order")
    {
        if (await Page.Locator("[data-testid=checkout-fake]").CountAsync() == 0)
        {
            var card = Page.FrameLocator("#ben-payment-element iframe").First;
            await card.Locator("[name=number]").FillAsync("4242 4242 4242 4242");
            await card.Locator("[name=expiry]").FillAsync("12 / 34");
            await card.Locator("[name=cvc]").FillAsync("123");
        }
        await PressPastTheLimitAsync(button, Page.GetByText("Thank you for your order!"));
    }

    private async Task<string> OrderNumberOnPageAsync()
        => Regex.Match(await Page.Locator("[data-testid=checkout-countdown]").InnerTextAsync(), @"Order no\. (\d+)").Groups[1].Value;

    [Test]
    [Description("A guest buys one item: cart → checkout → Payment step → thank-you page with the order number.")]
    public async Task A_guest_buys_one_item_end_to_end()
    {
        using var api = await StoreTestApi.OpenAsync();
        var product = await api.BuyableAsync(Unique("Investigators field bag"), 39m);
        await AddToCartAsync(product);
        await OpenCheckoutAsync();

        await Expect(CurrentStep).ToHaveTextAsync("Place order");
        await FillAsync(Buyer());
        await ContinueAsync();

        await Expect(CurrentStep).ToHaveTextAsync("Payment", new() { Timeout = 30_000 });
        await Expect(Page.Locator("[data-testid=checkout-locked-summary]")).ToContainTextAsync("Nashville, TN 37203");
        await Expect(Page.Locator("[data-testid=summary-total]")).ToContainTextAsync("Total");
        var number = await OrderNumberOnPageAsync();

        await PayAsync();
        await Expect(Page.Locator("[data-testid=complete-order-number]")).ToHaveTextAsync(number);
        await Expect(Page.Locator("[data-testid=complete-item]")).ToContainTextAsync(product.GetProperty("name").GetString()!);
        await Expect(Page.Locator(".ben-steps__item[aria-current=step] .ben-steps__label")).ToHaveTextAsync("Complete");

        // The order took the lines out of the cart.
        await Page.GotoAsync($"{BaseUrl}/store/cart");
        await Expect(Page.GetByText("Your cart is empty")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    [Description("Stock that went while the buyer was typing is refused in words on the page, and nothing is held.")]
    public async Task Checkout_refuses_more_than_on_hand()
    {
        using var api = await StoreTestApi.OpenAsync();
        var name = Unique("Single-unit probe");
        var product = await api.BuyableAsync(name, 24m, onHand: 1);
        await AddToCartAsync(product);
        await OpenCheckoutAsync();
        await FillAsync(Buyer());

        await api.SetStockAsync(product, 0);
        await PressPastTheLimitAsync("#store-continue-payment", Refusal.Filter(new() { HasText = name }));

        await Expect(Page.Locator("[data-testid=checkout-payment]")).ToHaveCountAsync(0);
        await Expect(CurrentStep).ToHaveTextAsync("Place order");
    }

    [Test]
    [Description("A missing ZIP is refused before anything is sent, in the server's own words.")]
    public async Task A_bad_address_is_refused_on_the_page()
    {
        using var api = await StoreTestApi.OpenAsync();
        await AddToCartAsync(await api.BuyableAsync(Unique("EMF meter"), 18m));
        await OpenCheckoutAsync();
        await FillAsync(Buyer());
        await FillAndConfirmAsync("#shipping-zip", "372");

        await Page.Locator("#store-continue-payment").ClickAsync();

        await Expect(Refusal).ToHaveTextAsync("Enter a 5-digit ZIP code.", new() { Timeout = 15_000 });
    }

    [Test]
    [Description("A discount code entered at checkout comes off before payment, and the order charges the lower total.")]
    public async Task Checkout_accepts_a_code_before_payment()
    {
        using var api = await StoreTestApi.OpenAsync();
        var code = ("E2E" + Guid.NewGuid().ToString("N")[..6]).ToUpperInvariant();
        await api.SendAsync(HttpMethod.Post, "/api/admin/store/coupons", new
        {
            code, name = "e2e ten percent", kind = 0, percentOff = 10, amountOff = (decimal?)null, minimumOrderAmount = (decimal?)null,
            startsUtc = (DateTime?)null, endsUtc = (DateTime?)null, maxRedemptions = (int?)null, maxRedemptionsPerBuyer = (int?)null, isActive = true,
        });
        await AddToCartAsync(await api.BuyableAsync(Unique("Spirit box"), 50m));
        await OpenCheckoutAsync();
        await FillAsync(Buyer());

        await FillAndConfirmAsync("#checkout-discount-code", code);
        await ClickUntilAsync(Page.Locator("#checkout-discount-apply"), Page.Locator("[data-testid=summary-discount]"));
        await Expect(Page.Locator("[data-testid=summary-discount]")).ToContainTextAsync("$5.00");

        await ContinueAsync();
        await Expect(Page.Locator("[data-testid=summary-discount]")).ToContainTextAsync("$5.00");
        await PayAsync();
    }

    [Test]
    [Description("Edit after Continue, change the code, Continue again: a new order is placed for the new total.")]
    public async Task Changing_the_code_after_continue_reprepares()
    {
        using var api = await StoreTestApi.OpenAsync();
        var code = ("E2E" + Guid.NewGuid().ToString("N")[..6]).ToUpperInvariant();
        await api.SendAsync(HttpMethod.Post, "/api/admin/store/coupons", new
        {
            code, name = "e2e five off", kind = 1, percentOff = (int?)null, amountOff = 5m, minimumOrderAmount = (decimal?)null,
            startsUtc = (DateTime?)null, endsUtc = (DateTime?)null, maxRedemptions = (int?)null, maxRedemptionsPerBuyer = (int?)null, isActive = true,
        });
        await AddToCartAsync(await api.BuyableAsync(Unique("Motion sensor"), 30m));
        await OpenCheckoutAsync();
        await FillAsync(Buyer());
        await ContinueAsync();
        var first = await OrderNumberOnPageAsync();

        await ClickUntilAsync(Page.Locator("#checkout-edit"), Page.Locator("#checkout-discount-code"));
        await Expect(CurrentStep).ToHaveTextAsync("Place order");
        await FillAndConfirmAsync("#checkout-discount-code", code);
        await ClickUntilAsync(Page.Locator("#checkout-discount-apply"), Page.Locator("[data-testid=summary-discount]"));
        await ContinueAsync();

        var second = await OrderNumberOnPageAsync();
        Assert.That(second, Is.Not.EqualTo(first), "a changed code must place a new order, not charge the old one");
        await Expect(Page.Locator("[data-testid=summary-discount]")).ToContainTextAsync("$5.00");
        // Paid, so no checkout is left open from this address (three at once are refused).
        await PayAsync();
    }

    [Test]
    [Description("When the hold runs out the page checks stock again by itself, and says so, instead of showing a Stripe error.")]
    public async Task Expired_reservation_re_prepares_instead_of_showing_a_Stripe_error()
    {
        using var api = await StoreTestApi.OpenAsync();
        await AddToCartAsync(await api.BuyableAsync(Unique("Laser grid"), 22m));
        await OpenCheckoutAsync();
        await FillAsync(Buyer());
        await ContinueAsync();
        var orderId = await Page.Locator("[data-testid=checkout-payment]").GetAttributeAsync("data-order-id");

        using (var http = new HttpClient { BaseAddress = new Uri(ApiUrl) })
        {
            // The dev route shares the checkout's ten-a-minute limit with the browser (one address).
            var expired = await http.PostAsync($"/api/store/checkout/dev/expire-reservation/{orderId}", JsonContent.Create(new { }));
            if (expired.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(TimeSpan.FromSeconds(61));
                expired = await http.PostAsync($"/api/store/checkout/dev/expire-reservation/{orderId}", JsonContent.Create(new { }));
            }
            Assert.That(expired.IsSuccessStatusCode, Is.True, $"the dev route answered {(int)expired.StatusCode}");
        }

        // The page re-reads the order every 30 seconds.
        await Expect(Page.Locator("[data-testid=checkout-notice]")).ToHaveTextAsync("Your reservation expired — we've checked stock again.", new() { Timeout = 45_000 });
        await Expect(Page.Locator("[data-testid=checkout-countdown]")).ToContainTextAsync(new Regex(@"for (1[0-5]|[5-9]):\d\d"));
        await Expect(Refusal).ToHaveCountAsync(0);
        await PayAsync();
    }

    [Test]
    [Description("With nothing in the cart, the checkout sends the buyer back to the cart.")]
    public async Task Checkout_WithEmptyCart_GoesBackToTheCart()
    {
        await Page.GotoAsync($"{BaseUrl}/store/checkout");
        await Page.WaitForURLAsync(new Regex("/store/cart$"), new() { Timeout = 30_000 });
        await Expect(Page.GetByText("Your cart is empty")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    [Test]
    [Description("On a phone the bar at the bottom carries the total and the step's one button, in both steps.")]
    public async Task Checkout_fits_a_phone()
    {
        await Page.SetViewportSizeAsync(375, 812);
        using var api = await StoreTestApi.OpenAsync();
        await AddToCartAsync(await api.BuyableAsync(Unique("Pocket thermometer"), 15m));
        await OpenCheckoutAsync();
        await FillAsync(Buyer());

        var bar = Page.Locator("[data-testid=checkout-bar]");
        await Expect(bar).ToBeVisibleAsync();
        // Pinned to the bottom of the screen, not waiting at the end of the page (it once was: sticky
        // inside the layout's scrolling container never stuck).
        var box = await bar.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);
        Assert.That(box!.Y + box.Height, Is.EqualTo(812).Within(2), "the checkout bar is not at the bottom of the phone's screen");
        await Expect(bar).ToContainTextAsync("Total before tax");
        await Expect(Page.Locator("#store-continue-payment")).ToBeHiddenAsync();
        await ContinueAsync("#store-continue-payment-bar");

        await Expect(bar).ToContainTextAsync("Total");
        await Expect(bar).Not.ToContainTextAsync("before tax");
        var sideways = await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1");
        Assert.That(sideways, Is.False, "the checkout scrolls sideways on a phone");

        await PayAsync("#store-place-order-bar");
    }
}
