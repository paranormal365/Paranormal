using Stripe;

namespace Ben.Data.WebApi.Services.Billing.StripeIntegration;

/// <summary>The store's half of the real gateway (storefront S4.2) — see <see cref="IStoreStripeGateway"/>.</summary>
/// <remarks>
/// <para><b>Its own client, bounded at 20 seconds.</b> Stripe.net waits 80 seconds by default, and
/// a checkout that holds stock and a coupon must not sit that long on one slow call. The
/// subscription half keeps its client as it was. Both live in this one singleton — never a second
/// registration building a second gateway.</para>
///
/// <para><b>No receipt email and no automatic payment methods.</b> The store sends its own
/// confirmation (one reference number, not two), and bank debits and buy-now-pay-later sit in
/// "processing" for days while stock is held; cards, and Link when switched on, only.</para>
/// </remarks>
public sealed partial class StripeGateway
{
    private StripeClient? _storeClient;

    private StripeClient StoreClient
        => _client is null
            ? throw new InvalidOperationException("Stripe is not configured.")
            : _storeClient ??= new StripeClient(_options.SecretKey, httpClient:
                new SystemNetHttpClient(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }));

    public async Task<StorePaymentIntentHandle> CreatePaymentIntentAsync(StorePaymentIntentSpec spec, CancellationToken ct)
    {
        try
        {
            return await CreateIntentAsync(spec, spec.AllowLink, spec.IdempotencyKey, ct);
        }
        catch (StripeException ex) when (spec.AllowLink && ex.StripeError?.Type == "invalid_request_error"
                                         && (ex.StripeError?.Message ?? ex.Message).Contains("link", StringComparison.OrdinalIgnoreCase))
        {
            // Link not activated in the dashboard: cards only, under a key of its own (the first
            // key's refusal would otherwise be replayed for a day).
            var handle = await CreateIntentAsync(spec, allowLink: false, spec.IdempotencyKey + "-card", ct);
            return handle with { LinkRefused = true };
        }
    }

    private async Task<StorePaymentIntentHandle> CreateIntentAsync(StorePaymentIntentSpec spec, bool allowLink, string key, CancellationToken ct)
    {
        var intent = await new PaymentIntentService(StoreClient).CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = spec.AmountCents,
            Currency = spec.Currency,
            PaymentMethodTypes = allowLink ? ["card", "link"] : ["card"],
            Metadata = new Dictionary<string, string>(spec.Metadata),
            Description = spec.Description,
            StatementDescriptorSuffix = spec.StatementDescriptorSuffix,
            Shipping = new ChargeShippingOptions
            {
                Name = spec.Shipping.Name,
                Phone = spec.Shipping.Phone,
                Address = new AddressOptions
                {
                    Line1 = spec.Shipping.Line1, Line2 = spec.Shipping.Line2, City = spec.Shipping.City,
                    State = spec.Shipping.State, PostalCode = spec.Shipping.PostalCode, Country = spec.Shipping.Country,
                },
            },
        }, new RequestOptions { IdempotencyKey = key }, ct);
        return new StorePaymentIntentHandle(intent.Id, intent.ClientSecret);
    }

    public async Task UpdatePaymentIntentAsync(string paymentIntentId, long amountCents, IReadOnlyDictionary<string, string> metadata, CancellationToken ct)
    {
        try
        {
            await new PaymentIntentService(StoreClient).UpdateAsync(paymentIntentId, new PaymentIntentUpdateOptions
            {
                Amount = amountCents,
                Metadata = new Dictionary<string, string>(metadata),
            }, cancellationToken: ct);
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "payment_intent_unexpected_state")
        {
            throw new StoreStripeUnexpectedStateException(ex.StripeError.Message ?? ex.Message);
        }
    }

    public async Task<string> GetClientSecretAsync(string paymentIntentId, CancellationToken ct)
        => (await new PaymentIntentService(StoreClient).GetAsync(paymentIntentId, cancellationToken: ct)).ClientSecret;

    public async Task<StripeCancelOutcome> CancelPaymentIntentAsync(string paymentIntentId, CancellationToken ct)
    {
        try
        {
            await new PaymentIntentService(StoreClient).CancelAsync(paymentIntentId, cancellationToken: ct);
            return StripeCancelOutcome.Cancelled;
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "resource_missing")
        {
            // Stripe has no such payment — nothing can ever be charged on it, so the checkout is over.
            // Before this, one such id made every expiry pass throw on it, for good (09/24).
            return StripeCancelOutcome.Cancelled;
        }
        catch (StripeException ex) when (ex.StripeError?.Code == "payment_intent_unexpected_state")
        {
            // Already canceled reads as done; confirming or succeeded means the money may be moving.
            return ex.StripeError.PaymentIntent?.Status == "canceled" ? StripeCancelOutcome.Cancelled : StripeCancelOutcome.StillProcessing;
        }
    }

    public async Task<StoreRefundOutcome> CreateRefundAsync(StoreRefundSpec spec, CancellationToken ct)
    {
        try
        {
            var refund = await new RefundService(StoreClient).CreateAsync(new RefundCreateOptions
            {
                PaymentIntent = spec.PaymentIntentId,
                Amount = spec.AmountCents,
                Reason = spec.Reason,
                Metadata = new Dictionary<string, string>(spec.Metadata),
            }, new RequestOptions { IdempotencyKey = spec.IdempotencyKey }, ct);
            return Outcome(refund);
        }
        catch (StripeException ex)
        {
            throw new StoreStripeRefusedException(ex.StripeError?.Code, ex.StripeError?.Type, ex.StripeError?.Message ?? ex.Message);
        }
    }

    public async Task<StoreChargeFee?> GetChargeFeeAsync(string paymentIntentId, CancellationToken ct)
    {
        var intent = await new PaymentIntentService(StoreClient).GetAsync(paymentIntentId,
            new PaymentIntentGetOptions { Expand = ["latest_charge.balance_transaction"] }, cancellationToken: ct);
        return intent.LatestCharge?.BalanceTransaction is { } balance ? new StoreChargeFee(balance.Fee, balance.Net) : null;
    }

    public async Task<IReadOnlyList<StoreRefundOutcome>> ListRefundsAsync(string paymentIntentId, CancellationToken ct)
    {
        var list = await new RefundService(StoreClient).ListAsync(
            new RefundListOptions { PaymentIntent = paymentIntentId, Limit = 100 }, cancellationToken: ct);
        return list.Data.Select(Outcome).ToList();
    }

    private static StoreRefundOutcome Outcome(Refund r)
        => new(r.Id, r.Status, r.Metadata ?? new Dictionary<string, string>(), r.FailureReason);

    public StripeWebhookEvent? ParseEvent(string payload, string signatureHeader)
        => MapEvent(EventUtility.ConstructEvent(payload, signatureHeader, _options.WebhookSecret, throwOnApiVersionMismatch: false));

    /// <summary>The eight store event types, reduced; anything else is null. Public for tests.</summary>
    public static StripeWebhookEvent? MapEvent(Event stripeEvent)
    {
        if (!StoreStripeKeys.EventTypes.Contains(stripeEvent.Type)) return null;
        var empty = new Dictionary<string, string>();

        return stripeEvent.Data.Object switch
        {
            PaymentIntent pi => new StripeWebhookEvent(
                stripeEvent.Type, pi.Id, pi.LatestChargeId,
                stripeEvent.Type == "payment_intent.succeeded" ? pi.AmountReceived : null,
                null, null, null, null, empty, pi.Metadata ?? empty, pi.LastPaymentError?.Code),
            Refund r => new StripeWebhookEvent(
                stripeEvent.Type, r.PaymentIntentId, r.ChargeId, null,
                r.Id, r.Amount, r.Status, r.FailureReason, r.Metadata ?? empty, empty),
            Stripe.Checkout.Session s => new StripeWebhookEvent(
                stripeEvent.Type, s.PaymentIntentId, null, null, null, null, null, null, empty, s.Metadata ?? empty),
            _ => null,
        };
    }
}
