using Stripe;

namespace Ben.Data.WebApi.Services.Billing.StripeIntegration;

// The store's side of Stripe (storefront S4.2): one PaymentIntent per order, refunds, and the
// webhook events that move an order along. Amounts arrive computed — this is translation.

/// <summary>Where a parcel goes, for the PaymentIntent's shipping (Stripe shows it in the dashboard).</summary>
public sealed record StoreShippingDetails(
    string Name, string? Phone, string Line1, string? Line2, string City, string State, string PostalCode, string Country = "US");

/// <param name="IdempotencyKey"><c>store-order-{id:N}</c> — one intent per order however often the create is retried.</param>
/// <param name="AllowLink">Offer Stripe Link beside cards (<c>store.link-enabled</c>).</param>
public sealed record StorePaymentIntentSpec(
    Guid OrderId, long AmountCents, string Currency, IReadOnlyDictionary<string, string> Metadata,
    StoreShippingDetails Shipping, string Description, string StatementDescriptorSuffix, string IdempotencyKey,
    bool AllowLink);

public sealed record StorePaymentIntentHandle(string PaymentIntentId, string ClientSecret, bool LinkRefused = false);

public sealed record StoreRefundSpec(
    string PaymentIntentId, long AmountCents, string Reason, IReadOnlyDictionary<string, string> Metadata, string IdempotencyKey);

/// <param name="Status">Stripe's own word: <c>succeeded</c>, <c>pending</c>, <c>requires_action</c>, <c>failed</c>, <c>canceled</c>.</param>
public sealed record StoreRefundOutcome(string StripeRefundId, string Status, IReadOnlyDictionary<string, string> Metadata, string? FailureReason = null);

/// <summary>A verified webhook delivery, reduced to what the store acts on.</summary>
/// <param name="AmountReceivedCents">Null for a checkout-session event, which carries no amount.</param>
public sealed record StripeWebhookEvent(
    string Type, string? PaymentIntentId, string? ChargeId, long? AmountReceivedCents,
    string? RefundId, long? RefundAmountCents, string? RefundStatus, string? RefundFailureReason,
    IReadOnlyDictionary<string, string> RefundMetadata, IReadOnlyDictionary<string, string> Metadata,
    string? FailureCode = null);

/// <summary>What asking Stripe to cancel an intent came to.</summary>
/// <remarks>
/// No "no intent" value: the gateway is always handed one. That case belongs to the caller
/// (<c>StoreOrderPayments.CancelOutcome</c>), which never asks Stripe when there is nothing to cancel.
/// </remarks>
public enum StripeCancelOutcome
{
    /// <summary>Cancelled at Stripe (or already was): nothing can be charged on it now.</summary>
    Cancelled,
    /// <summary>Stripe would not: the payment is confirming or already succeeded. The webhook finishes the order.</summary>
    StillProcessing,
}

/// <summary>Stripe refused to change an intent that is already being paid (<c>payment_intent_unexpected_state</c>).</summary>
public sealed class StoreStripeUnexpectedStateException(string message) : Exception(message);

/// <summary>Stripe answered with something other than a network failure — a refusal worth reading.</summary>
public sealed class StoreStripeRefusedException(string? code, string? type, string message) : Exception(message)
{
    public string? Code { get; } = code;
    public string? Type { get; } = type;
}

/// <summary>
/// The store's seam to Stripe, beside <see cref="IStripeGateway"/> (subscriptions). Registered as
/// the SAME singleton <see cref="StripeGateway"/> in production, or <see cref="FakeStoreStripeGateway"/>
/// in a Development test checkout (<c>StoreStripeMode</c>).
/// </summary>
/// <summary>Stripe's fee on a payment and what reached the balance, in cents.</summary>
public sealed record StoreChargeFee(long FeeCents, long NetCents);

public interface IStoreStripeGateway
{
    bool IsConfigured { get; }

    /// <summary>A card intent for one order. Retries once as cards-only when Stripe refuses Link.</summary>
    Task<StorePaymentIntentHandle> CreatePaymentIntentAsync(StorePaymentIntentSpec spec, CancellationToken ct);

    /// <exception cref="StoreStripeUnexpectedStateException">The intent is already confirming or paid.</exception>
    Task UpdatePaymentIntentAsync(string paymentIntentId, long amountCents, IReadOnlyDictionary<string, string> metadata, CancellationToken ct);

    Task<StripeCancelOutcome> CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct);

    /// <summary>An existing intent's client secret — how a reused order's page mounts the same payment form.</summary>
    Task<string> GetClientSecretAsync(string paymentIntentId, CancellationToken ct);

    Task<StoreRefundOutcome> CreateRefundAsync(StoreRefundSpec spec, CancellationToken ct);

    /// <summary>
    /// What Stripe kept of a payment, from its charge's balance transaction (store sellers P9); null
    /// while the balance transaction isn't there yet — a sweep asks again later.
    /// </summary>
    Task<StoreChargeFee?> GetChargeFeeAsync(string paymentIntentId, CancellationToken ct);

    /// <summary>Every refund Stripe holds on an intent — how a retry finds one it already made.</summary>
    Task<IReadOnlyList<StoreRefundOutcome>> ListRefundsAsync(string paymentIntentId, CancellationToken ct);

    /// <summary>
    /// Verifies a delivery and maps the eight event types the store acts on; null for any other.
    /// </summary>
    /// <exception cref="StripeException">The signature does not verify.</exception>
    StripeWebhookEvent? ParseEvent(string payload, string signatureHeader);
}

/// <summary>The store's own names for what it puts in Stripe metadata — the only way an event finds its order.</summary>
public static class StoreStripeKeys
{
    public const string Order = "ih_store_order";
    public const string Cart = "ih_cart";
    public const string TaxCalculation = "ih_tax_calc";
    public const string OrderNumber = "ih_order_number";
    public const string Refund = "ih_store_refund";

    /// <summary>The eight event types the store subscribes to. <c>charge.refunded</c> is deliberately absent.</summary>
    public static readonly IReadOnlyList<string> EventTypes =
    [
        "payment_intent.succeeded", "checkout.session.completed", "payment_intent.processing",
        "payment_intent.payment_failed", "payment_intent.canceled", "refund.created", "refund.updated", "refund.failed",
    ];
}
