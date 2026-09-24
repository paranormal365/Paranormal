using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Stripe;
using Xunit;

namespace Ben.Web.Tests.Store;

/// <summary>
/// The store's webhook events, reduced to what an order acts on (storefront S4.2). Built from
/// Stripe.net's own objects — the signature path is proven on payloads captured from a real
/// test-mode account (StripeWebhookSignatureStoreTests, once captured — see the README).
/// </summary>
public sealed class StoreStripeGatewayTests
{
    private static Event Wrap(string type, IHasObject obj) => new() { Type = type, Data = new EventData { Object = obj } };

    private static readonly Dictionary<string, string> OrderMeta = new()
    {
        [StoreStripeKeys.Order] = Guid.NewGuid().ToString(), [StoreStripeKeys.TaxCalculation] = "taxcalc_1",
    };

    [Fact]
    public void A_succeeded_intent_carries_its_amount_charge_and_metadata()
    {
        var e = StripeGateway.MapEvent(Wrap("payment_intent.succeeded", new PaymentIntent
        {
            Id = "pi_1", AmountReceived = 5999, LatestChargeId = "ch_1", Metadata = OrderMeta,
        }))!;

        Assert.Equal(("pi_1", "ch_1", (long?)5999), (e.PaymentIntentId, e.ChargeId, e.AmountReceivedCents));
        Assert.Equal("taxcalc_1", e.Metadata[StoreStripeKeys.TaxCalculation]);
    }

    [Fact]
    public void Only_a_succeeded_intent_reports_an_amount_received()
    {
        var processing = StripeGateway.MapEvent(Wrap("payment_intent.processing", new PaymentIntent { Id = "pi_1", AmountReceived = 5999 }))!;
        var failed = StripeGateway.MapEvent(Wrap("payment_intent.payment_failed", new PaymentIntent
        {
            Id = "pi_1", LastPaymentError = new StripeError { Code = "card_declined" },
        }))!;

        Assert.Null(processing.AmountReceivedCents);
        Assert.Equal("card_declined", failed.FailureCode);
    }

    [Theory]
    [InlineData("refund.created")]
    [InlineData("refund.updated")]
    [InlineData("refund.failed")]
    public void A_refund_event_carries_the_refund_and_its_own_metadata(string type)
    {
        var e = StripeGateway.MapEvent(Wrap(type, new Refund
        {
            Id = "re_1", PaymentIntentId = "pi_1", Amount = 1200, Status = "failed", FailureReason = "expired_or_canceled_card",
            Metadata = new Dictionary<string, string> { [StoreStripeKeys.Refund] = "ours" },
        }))!;

        Assert.Equal(("re_1", "pi_1", (long?)1200, "failed"), (e.RefundId, e.PaymentIntentId, e.RefundAmountCents, e.RefundStatus));
        Assert.Equal("ours", e.RefundMetadata[StoreStripeKeys.Refund]);
        Assert.Equal("expired_or_canceled_card", e.RefundFailureReason);
    }

    /// <summary>charge.refunded would announce every refund twice beside refund.created — not subscribed, not parsed.</summary>
    /// <remarks>
    /// Built with objects the mapper WOULD read — a refund, an intent — so only the event-type
    /// filter stands between them and an order; a Charge would be ignored whatever the type.
    /// </remarks>
    [Fact]
    public void Anything_else_is_not_the_stores()
    {
        Assert.Null(StripeGateway.MapEvent(Wrap("charge.refunded", new Refund { Id = "re_1", PaymentIntentId = "pi_1", Status = "succeeded" })));
        Assert.Null(StripeGateway.MapEvent(Wrap("payment_intent.created", new PaymentIntent { Id = "pi_1", Metadata = OrderMeta })));
    }

    [Fact]
    public void The_store_listens_to_exactly_eight_event_types()
    {
        Assert.Equal(8, StoreStripeKeys.EventTypes.Count);
        Assert.DoesNotContain("charge.refunded", StoreStripeKeys.EventTypes);
    }

    [Fact]
    public async Task The_fake_gives_one_intent_per_order_and_honours_its_switches()
    {
        var fake = new FakeStoreStripeGateway();
        var order = Guid.NewGuid();
        var spec = new StorePaymentIntentSpec(order, 5999, "usd", OrderMeta, new StoreShippingDetails("Sarah", null, "1 Main", null, "Nashville", "TN", "37203"),
            "IsHaunted store order 100001", "100001", $"store-order-{order:N}", AllowLink: true);

        var a = await fake.CreatePaymentIntentAsync(spec, default);
        var b = await fake.CreatePaymentIntentAsync(spec, default);
        Assert.Equal(a.PaymentIntentId, b.PaymentIntentId);
        Assert.EndsWith(FakeStoreStripeGateway.SecretSuffix, a.ClientSecret);

        fake.UpdateIsUnexpectedState = true;
        await Assert.ThrowsAsync<StoreStripeUnexpectedStateException>(() => fake.UpdatePaymentIntentAsync(a.PaymentIntentId, 1, OrderMeta, default));

        fake.CancelAnswer = StripeCancelOutcome.StillProcessing;
        Assert.Equal(StripeCancelOutcome.StillProcessing, await fake.CancelPaymentIntentAsync(a.PaymentIntentId, default));
    }
}
