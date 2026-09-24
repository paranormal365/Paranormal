using System.Collections.Concurrent;

namespace Ben.Data.WebApi.Services.Billing.StripeIntegration;

/// <summary>
/// Pretend Stripe for the store (storefront S4.2): Development only, no secret key, and
/// <c>Stripe:AllowFakeCheckout</c> on (<c>StoreStripeMode.UseFakes</c>) — the e2e harness and a
/// developer's machine. Also what the tests drive, with switches to make it refuse.
/// </summary>
/// <remarks>
/// It never takes money and never receives a webhook: a test checkout is finished by
/// <c>POST api/store/checkout/dev/simulate-payment/{id}</c>, which marks the order paid exactly as
/// the real <c>payment_intent.succeeded</c> would.
/// </remarks>
public sealed class FakeStoreStripeGateway : IStoreStripeGateway
{
    public const string SecretSuffix = "_secret_fake";
    public const string PublishableKey = "pk_test_fake";

    public bool IsConfigured => true;

    /// <summary>Amount and metadata of every intent made or updated, by id.</summary>
    public ConcurrentDictionary<string, (long AmountCents, IReadOnlyDictionary<string, string> Metadata)> Intents { get; } = new();

    public ConcurrentBag<string> Cancelled { get; } = [];
    public ConcurrentBag<StoreRefundSpec> Refunds { get; } = [];
    public ConcurrentBag<string> CreateKeys { get; } = [];

    // ── switches for tests ──
    public bool RefuseLink { get; set; }
    public bool UpdateIsUnexpectedState { get; set; }
    public StripeCancelOutcome CancelAnswer { get; set; } = StripeCancelOutcome.Cancelled;
    public bool CreateFails { get; set; }
    public string RefundStatus { get; set; } = "succeeded";
    public string? RefundRefusal { get; set; }

    public static string IntentIdFor(Guid orderId) => $"pi_fake_{orderId:N}";

    public Task<StorePaymentIntentHandle> CreatePaymentIntentAsync(StorePaymentIntentSpec spec, CancellationToken ct)
    {
        if (CreateFails) throw new HttpRequestException("Stripe did not answer (fake).");
        CreateKeys.Add(spec.IdempotencyKey);
        var id = IntentIdFor(spec.OrderId);
        Intents[id] = (spec.AmountCents, spec.Metadata);
        return Task.FromResult(new StorePaymentIntentHandle(id, id + SecretSuffix, LinkRefused: spec.AllowLink && RefuseLink));
    }

    public Task UpdatePaymentIntentAsync(string paymentIntentId, long amountCents, IReadOnlyDictionary<string, string> metadata, CancellationToken ct)
    {
        if (UpdateIsUnexpectedState) throw new StoreStripeUnexpectedStateException("The payment is already confirming (fake).");
        Intents[paymentIntentId] = (amountCents, metadata);
        return Task.CompletedTask;
    }

    public Task<StripeCancelOutcome> CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct)
    {
        Cancelled.Add(paymentIntentId);
        return Task.FromResult(CancelAnswer);
    }

    public Task<StoreRefundOutcome> CreateRefundAsync(StoreRefundSpec spec, CancellationToken ct)
    {
        if (RefundRefusal is { } why) throw new StoreStripeRefusedException("charge_already_refunded", "invalid_request_error", why);
        Refunds.Add(spec);
        return Task.FromResult(new StoreRefundOutcome($"re_fake_{Refunds.Count}_{spec.IdempotencyKey}", RefundStatus, spec.Metadata));
    }

    public Task<IReadOnlyList<StoreRefundOutcome>> ListRefundsAsync(string paymentIntentId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StoreRefundOutcome>>(Refunds.Where(r => r.PaymentIntentId == paymentIntentId)
            .Select(r => new StoreRefundOutcome($"re_fake_{r.IdempotencyKey}", RefundStatus, r.Metadata)).ToList());

    /// <summary>A test checkout receives no webhooks; anything that arrives is not ours to act on.</summary>
    public StripeWebhookEvent? ParseEvent(string payload, string signatureHeader) => null;
}
