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

    /// <summary>A payment Stripe cannot be reached about — its cancel throws, as a network or Stripe fault would.</summary>
    public string? CancelThrowsFor { get; set; }
    public bool CreateFails { get; set; }
    public string RefundStatus { get; set; } = "succeeded";
    public string? RefundRefusal { get; set; }

    public static string IntentIdFor(Guid orderId) => $"pi_fake_{orderId:N}";

    /// <summary>What Stripe says about a statement descriptor suffix, or null when it takes it.</summary>
    public static string? StatementSuffixProblem(string? suffix)
    {
        if (string.IsNullOrEmpty(suffix)) return null;
        if (!suffix.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')) return "The statement descriptor must contain at least one Latin character.";
        if (suffix.Length > 22) return "The statement descriptor suffix is at most 22 characters.";
        if (suffix.IndexOfAny(['<', '>', '\\', '\'', '"', '*']) >= 0) return "The statement descriptor cannot contain < > \\ ' \" *.";
        return null;
    }

    public Task<StorePaymentIntentHandle> CreatePaymentIntentAsync(StorePaymentIntentSpec spec, CancellationToken ct)
    {
        if (CreateFails) throw new HttpRequestException("Stripe did not answer (fake).");
        // Stripe's own rule, so the test checkout refuses what Stripe would: a bare order number
        // passed every fake run and was refused by the first real one (09/24).
        if (StatementSuffixProblem(spec.StatementDescriptorSuffix) is { } problem)
            throw new InvalidOperationException($"Stripe would refuse this payment: {problem}");
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

    public Task<string> GetClientSecretAsync(string paymentIntentId, CancellationToken ct)
        => Task.FromResult(paymentIntentId + SecretSuffix);

    public Task<StripeCancelOutcome> CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct)
    {
        if (paymentIntentId == CancelThrowsFor) throw new HttpRequestException("Stripe could not be reached (fake).");
        Cancelled.Add(paymentIntentId);
        return Task.FromResult(CancelAnswer);
    }

    public Task<StoreRefundOutcome> CreateRefundAsync(StoreRefundSpec spec, CancellationToken ct)
    {
        if (RefundRefusal is { } why) throw new StoreStripeRefusedException("charge_already_refunded", "invalid_request_error", why);
        Refunds.Add(spec);
        return Task.FromResult(new StoreRefundOutcome($"re_fake_{spec.IdempotencyKey}", RefundStatus, spec.Metadata));
    }

    /// <summary>The fake fee: Stripe's US card rate, 2.9% + 30¢ of the intent's amount.</summary>
    public Task<StoreChargeFee?> GetChargeFeeAsync(string paymentIntentId, CancellationToken ct)
    {
        if (!Intents.TryGetValue(paymentIntentId, out var intent)) return Task.FromResult<StoreChargeFee?>(null);
        var fee = (long)Math.Round(intent.AmountCents * 0.029m, MidpointRounding.AwayFromZero) + 30;
        return Task.FromResult<StoreChargeFee?>(new StoreChargeFee(fee, intent.AmountCents - fee));
    }

    public Task<IReadOnlyList<StoreRefundOutcome>> ListRefundsAsync(string paymentIntentId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<StoreRefundOutcome>>(Refunds.Where(r => r.PaymentIntentId == paymentIntentId)
            .Select(r => new StoreRefundOutcome($"re_fake_{r.IdempotencyKey}", RefundStatus, r.Metadata)).ToList());

    /// <summary>A test checkout receives no webhooks; anything that arrives is not ours to act on.</summary>
    public StripeWebhookEvent? ParseEvent(string payload, string signatureHeader) => null;
}
