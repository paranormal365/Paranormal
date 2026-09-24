using System.Text.Json;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Ben.Web.Playwright.Tests;

/// <summary>
/// One order the whole way through real Stripe, in test mode (storefront S4 exit): a guest pays with
/// Stripe's 4242 card, the payment's event marks it paid, Tennessee sales tax is charged and filed,
/// and a refund reaches Stripe and reverses the tax.
/// </summary>
/// <remarks>
/// <para><b>Real mode only</b> — <c>BEN_STRIPE_E2E=1</c> with the developer's Stripe test keys, a
/// Tennessee tax registration in that test account, and <c>stripe listen</c> forwarding events
/// (run-e2e.sh starts it). Everything else in the store suite proves the pages against the test
/// checkout; this is the part only Stripe can answer.</para>
///
/// <para>The desk is read through its API, polled: paying, filing the tax and finishing the refund
/// all happen on Stripe's clock and arrive by webhook.</para>
/// </remarks>
[TestFixture]
[Category("Store")]
public class StoreRealStripeTests : BenTestBase
{
    private const int Paid = 1;          // StoreOrderStatus
    private const int Refunded = 6;      // StoreOrderStatus
    private const int Succeeded = 1;     // StoreRefundStatus

    private static async Task<JsonElement> WaitForAsync(StoreTestApi api, string orderId, Func<JsonElement, bool> done, string what)
    {
        JsonElement order = default;
        for (var i = 0; i < 45; i++)
        {
            order = await api.SendAsync(HttpMethod.Get, $"/api/admin/store/orders/{orderId}");
            if (done(order)) return order;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        Assert.Fail($"{what} did not happen within 90 seconds. Last read: {order}");
        return order;
    }

    [Test]
    [Description("A real test card pays; the order is paid with Tennessee tax filed; a refund reaches Stripe and reverses the tax.")]
    public async Task A_real_card_pays_the_tax_is_filed_and_a_refund_reaches_Stripe()
    {
        if (!StoreTestApi.RealStripe)
            Assert.Ignore("Real Stripe only: run with BEN_STRIPE_E2E=1 and Stripe test keys.");

        using var api = await StoreTestApi.OpenAsync();
        var product = await api.BuyableAsync($"Real Stripe bag {Guid.NewGuid().ToString("N")[..6]}", 39m);

        await Page.GotoAsync($"{BaseUrl}/store/p/{product.GetProperty("slug").GetString()}");
        await WaitForTheCircuitAsync();
        await Expect(Page.Locator("[data-testid=add-to-cart]")).ToBeEnabledAsync(new() { Timeout = 30_000 });
        await ClickUntilAsync(Page.Locator("[data-testid=add-to-cart]"), Page.Locator("[data-testid=cart-drawer]"));

        await Page.GotoAsync($"{BaseUrl}/store/checkout");
        await WaitForTheCircuitAsync();
        await FillAndConfirmAsync("#checkout-email", $"real-stripe-{Guid.NewGuid().ToString("N")[..8]}@example.com");
        await FillAndConfirmAsync("#shipping-full-name", "Ada Buyer");
        await FillAndConfirmAsync("#shipping-phone", "615-555-0100");
        await FillAndConfirmAsync("#shipping-street1", "401 Church St");
        await FillAndConfirmAsync("#shipping-city", "Nashville");
        await Page.SelectOptionAsync("#shipping-state", "TN");
        await FillAndConfirmAsync("#shipping-zip", "37219");
        await Page.Locator("#checkout-terms").CheckAsync();

        var payment = Page.Locator("[data-testid=checkout-payment]");
        await Page.Locator("#store-continue-payment").ClickAsync();
        var tooManyOpen = Page.Locator("#checkout-refusal").Filter(new() { HasText = "Too many checkouts are open" });
        await Expect(payment.Or(tooManyOpen)).ToBeVisibleAsync(new() { Timeout = 60_000 });
        if (await tooManyOpen.IsVisibleAsync())
        {
            await StoreTestApi.ReleaseOpenCheckoutsAsync();
            await Page.Locator("#store-continue-payment").ClickAsync();
            await Expect(payment).ToBeVisibleAsync(new() { Timeout = 60_000 });
        }
        var orderId = await payment.GetAttributeAsync("data-order-id");
        Assert.That(orderId, Is.Not.Null.And.Not.Empty);

        // Tennessee is registered in the test account, so the summary carries tax before anything is paid.
        await Expect(Page.Locator("[data-testid=checkout-fake]")).ToHaveCountAsync(0);

        // Stripe's own card form, in its frame.
        var card = Page.FrameLocator("#ben-payment-element iframe").First;
        await card.Locator("[name=number]").FillAsync("4242 4242 4242 4242", new() { Timeout = 30_000 });
        await card.Locator("[name=expiry]").FillAsync("12 / 34");
        await card.Locator("[name=cvc]").FillAsync("123");
        var postal = card.Locator("[name=postalCode]");
        if (await postal.CountAsync() > 0) await postal.FillAsync("37219");
        await Page.Locator("#store-place-order").ClickAsync();
        await Expect(Page.GetByText("Thank you for your order!")).ToBeVisibleAsync(new() { Timeout = 90_000 });

        // Paid by Stripe's event, with Tennessee tax, and the tax filed as a Stripe Tax transaction.
        var order = await WaitForAsync(api, orderId!, o =>
            o.GetProperty("status").GetInt32() == Paid && o.GetProperty("stripeTaxTransactionId").ValueKind == JsonValueKind.String,
            "the order being paid and its tax filed");
        Assert.That(order.GetProperty("stripePaymentIntentId").GetString(), Does.StartWith("pi_").And.Not.Contain("fake"));
        Assert.That(order.GetProperty("totals").GetProperty("tax").GetDecimal(), Is.GreaterThan(0m), "Tennessee sales tax");

        // A full refund of the one line, back on the shelf: it reaches Stripe and the tax is reversed.
        var item = order.GetProperty("items")[0].GetProperty("id").GetString();
        await api.SendAsync(HttpMethod.Post, $"/api/admin/store/orders/{orderId}/refunds",
            new { amount = (decimal?)null, items = new[] { new { orderItemId = item, quantity = 1 } }, reason = "Real Stripe test", restock = true });
        var refunded = await WaitForAsync(api, orderId!, o =>
            o.GetProperty("refunds").GetArrayLength() > 0
            && o.GetProperty("refunds")[0].GetProperty("status").GetInt32() == Succeeded
            && o.GetProperty("refunds")[0].GetProperty("stripeTaxReversalId").ValueKind == JsonValueKind.String,
            "the refund succeeding at Stripe with its tax reversed");
        var refund = refunded.GetProperty("refunds")[0];
        Assert.That(refund.GetProperty("stripeRefundId").GetString(), Does.StartWith("re_").And.Not.Contain("fake"));
        Assert.That(refund.GetProperty("taxReversed").GetDecimal(), Is.GreaterThan(0m));
        Assert.That(refunded.GetProperty("status").GetInt32(), Is.EqualTo(Refunded));
    }
}
