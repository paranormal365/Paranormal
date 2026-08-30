using Stripe;
using Stripe.Checkout;

namespace Ben.Data.WebApi.Services.Billing.StripeIntegration;

/// <summary>Configuration for the Stripe integration. Absent keys mean the feature reports
/// itself unavailable — never a failed subscribe with a stack trace.</summary>
public sealed class StripeOptions
{
    public string? SecretKey { get; set; }
    public string? PublishableKey { get; set; }
    /// <summary>Signing secret for the webhook endpoint (whsec_…). Without it the webhook
    /// refuses everything — an unverified event is an instruction from a stranger.</summary>
    public string? WebhookSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(SecretKey);
}

/// <summary>Everything the checkout endpoint needs Stripe to know, computed by OUR billing
/// engine. Stripe prices nothing here — it is handed the amounts.</summary>
public sealed record StripeCheckoutSpec(
    Guid OrganizationId,
    string OrganizationName,
    string? ExistingCustomerRef,
    decimal Payable,
    decimal TaxAmount,
    string LineDescription,
    string SuccessUrl,
    string CancelUrl,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>What checkout creation hands back: where to send the person, and the customer the
/// session was opened under (created on first use).</summary>
public sealed record StripeCheckoutHandle(string SessionUrl, string CustomerRef);

/// <summary>A completed checkout, reduced to what fulfillment needs.</summary>
public sealed record StripeCompletedCheckout(
    string SessionId,
    string? PaymentIntentRef,
    string? CustomerRef,
    string? PaymentMethodRef,
    IReadOnlyDictionary<string, string> Metadata);

/// <summary>
/// The seam between this API and Stripe's — everything that talks to their servers.
/// </summary>
/// <remarks>
/// An interface for the same reason the field sensors have one: the fulfillment logic must be
/// testable without a network, and a test that stubs "Stripe said the payment succeeded" is
/// exercising exactly the trust boundary the webhook crosses.
/// </remarks>
public interface IStripeGateway
{
    bool IsConfigured { get; }

    /// <summary>Opens a hosted Checkout session for one period's payment, card saved for
    /// off-session renewal.</summary>
    Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(StripeCheckoutSpec spec, CancellationToken ct);

    /// <summary>
    /// Verifies a webhook delivery's signature and returns the completed checkout it announces,
    /// or null for event types fulfillment does not care about.
    /// </summary>
    /// <exception cref="StripeException">The signature does not verify — the caller answers 400
    /// and Stripe retries or gives up; either way nothing unverified proceeds.</exception>
    StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader);
}

/// <summary>The real thing. Small on purpose: amounts and decisions arrive computed, so this is
/// translation, not logic.</summary>
public sealed class StripeGateway : IStripeGateway
{
    private readonly StripeOptions _options;
    private readonly StripeClient? _client;

    public StripeGateway(Microsoft.Extensions.Options.IOptions<StripeOptions> options)
    {
        _options = options.Value;
        _client = _options.IsConfigured ? new StripeClient(_options.SecretKey) : null;
    }

    public bool IsConfigured => _client is not null;

    public async Task<StripeCheckoutHandle> CreateCheckoutSessionAsync(
        StripeCheckoutSpec spec, CancellationToken ct)
    {
        if (_client is null) throw new InvalidOperationException("Stripe is not configured.");

        // One Stripe customer per organization, forever. Idempotent by our own ref: reuse when
        // the subscription already carries one, create otherwise.
        var customerId = spec.ExistingCustomerRef;
        if (string.IsNullOrEmpty(customerId))
        {
            var customer = await new CustomerService(_client).CreateAsync(new CustomerCreateOptions
            {
                Name = spec.OrganizationName,
                Description = $"IsHaunted group {spec.OrganizationId}",
                Metadata = new() { ["organizationId"] = spec.OrganizationId.ToString() },
            }, cancellationToken: ct);
            customerId = customer.Id;
        }

        var lineItems = new List<SessionLineItemOptions>
        {
            new()
            {
                Quantity = 1,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "usd",
                    UnitAmount = (long)(spec.Payable * 100m),
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                        { Name = spec.LineDescription },
                },
            },
        };
        if (spec.TaxAmount > 0m)
        {
            // Tax as its own visible line, computed by OUR TaxResolver and frozen in metadata —
            // Stripe Tax is deliberately not enrolled; two tax engines would disagree by a cent.
            lineItems.Add(new SessionLineItemOptions
            {
                Quantity = 1,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "usd",
                    UnitAmount = (long)(spec.TaxAmount * 100m),
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                        { Name = "Sales tax" },
                },
            });
        }

        var session = await new SessionService(_client).CreateAsync(new SessionCreateOptions
        {
            Mode = "payment",
            Customer = customerId,
            LineItems = lineItems,
            SuccessUrl = spec.SuccessUrl,
            CancelUrl = spec.CancelUrl,
            Metadata = new Dictionary<string, string>(spec.Metadata),
            PaymentIntentData = new SessionPaymentIntentDataOptions
            {
                // The card is saved so the renewal job can charge it without the person present.
                SetupFutureUsage = "off_session",
                Metadata = new Dictionary<string, string>(spec.Metadata),
            },
        }, cancellationToken: ct);

        return new StripeCheckoutHandle(session.Url, customerId);
    }

    public StripeCompletedCheckout? ParseCompletedCheckout(string payload, string signatureHeader)
    {
        // Throws on a bad signature — deliberately not caught here. Verification failing is the
        // one case where nothing downstream may run.
        // throwOnApiVersionMismatch: false — the signature is the security boundary; the API
        // version is a compatibility hint, and refusing a genuine, signed event because Stripe
        // shipped a minor version bump would silently stop fulfilling real payments.
        var stripeEvent = EventUtility.ConstructEvent(
            payload, signatureHeader, _options.WebhookSecret, throwOnApiVersionMismatch: false);

        if (stripeEvent.Type != "checkout.session.completed") return null;
        if (stripeEvent.Data.Object is not Session session) return null;

        // The session payload carries ids, not expanded objects. The payment method ref rides in
        // for the renewal job; fetched here, while we hold a verified event, not later on trust.
        string? paymentMethodRef = null;
        if (_client is not null && !string.IsNullOrEmpty(session.PaymentIntentId))
        {
            var intent = new PaymentIntentService(_client).Get(session.PaymentIntentId);
            paymentMethodRef = intent.PaymentMethodId;
        }

        return new StripeCompletedCheckout(
            session.Id, session.PaymentIntentId, session.CustomerId, paymentMethodRef,
            session.Metadata ?? new Dictionary<string, string>());
    }
}
