using System.Globalization;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Service.Models;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Opening a Stripe checkout for event credits, for a group or for a person (10/21/2026).
/// </summary>
/// <remarks>
/// <para><b>Priced here, before Stripe hears about it</b>, the way every checkout on the site is: the
/// offer from the settings, the paid-subscriber percentage when a group with a standing paid plan is
/// buying, the bundle when one was chosen, and tax. The results are frozen into the session's metadata
/// and fulfilment never re-prices — a price edited between the click and the card cannot reach a
/// purchase already shown.</para>
///
/// <para><b>No free path.</b> A credit costs what it costs or it is not sold; nothing here can price one
/// at zero, because the subscriber percentage is at least 1 and a bundle must have a price.</para>
/// </remarks>
public static class EventCreditCheckout
{
    /// <summary>What happened: a place to send the buyer, or the sentence that says why not.</summary>
    /// <param name="Response">Where Stripe's page is.</param>
    /// <param name="Refusal">A refusal the buyer can act on (answered as 400).</param>
    /// <param name="Unavailable">The site cannot take the money right now (answered as 503).</param>
    public sealed record Outcome(StartCheckoutResponse? Response, string? Refusal, string? Unavailable);

    /// <summary>The refusal while credits are off sale.</summary>
    public const string NotOnSale = "Event credits aren't on sale at the moment.";

    /// <summary>
    /// Starts the checkout. Exactly one of <paramref name="organizationId"/> (a group buying for itself)
    /// or a person buying for themselves (<paramref name="organizationId"/> null) applies.
    /// </summary>
    /// <param name="bundle">The bundle's quantity, when a bundle was chosen; null for single credits.</param>
    public static async Task<Outcome> StartAsync(
        BenDataContext db, IStripeGateway stripe,
        Guid? organizationId, string buyerName, string? existingCustomerRef,
        Guid userId, int quantity, int? bundle, string returnUrl, CancellationToken ct)
    {
        var offer = await EventCreditOffer.ReadAsync(db, ct);
        if (!offer.OnSale) return new Outcome(null, NotOnSale, null);

        EventCreditBundle? chosen = null;
        if (bundle is { } wanted)
        {
            chosen = offer.BundleFor(wanted);
            if (chosen is null)
                return new Outcome(null, "That bundle isn't offered any more. The page shows what is on sale now.", null);
        }

        var count = Math.Clamp(quantity <= 0 ? 1 : quantity, 1, EventCredits.MaximumPerPurchase);
        var now = DateTime.UtcNow;
        var subscriber = organizationId is { } org && await EventCreditOffer.IsPaidSubscriberAsync(db, org, now, ct);
        var quote = EventCreditPricing.Quote(offer.UnitPrice, count, chosen, subscriber, offer.SubscriberPercent);

        if (quote.Payable <= 0m)
            return new Outcome(null, null, "Event credits aren't priced yet.");

        // A group's tax follows its address; a person has no address on file, so no rule matches and
        // the rate resolver says zero — the same answer it gives a group with no address.
        decimal taxRate = 0m;
        if (organizationId is { } taxedOrg)
            (_, taxRate) = await TaxResolver.ForOrganizationAsync(db, taxedOrg, ct);
        var tax = TaxResolver.TaxOn(quote.Payable, taxRate);

        if (!stripe.IsConfigured)
            return new Outcome(null, null, "Online payment isn't set up yet — contact us and we'll sort it out directly.");

        var inv = CultureInfo.InvariantCulture;
        var metadata = new Dictionary<string, string>
        {
            [StripeFulfillmentService.CheckoutFacts.Keys.User] = userId.ToString(),
            [StripeFulfillmentService.CheckoutFacts.Keys.EventCredits] = quote.Quantity.ToString(inv),
            // What is charged before tax. The fulfilment reads it as the purchase's total.
            [StripeFulfillmentService.CheckoutFacts.Keys.List] = quote.Payable.ToString(inv),
            [StripeFulfillmentService.CheckoutFacts.Keys.CreditFullPrice] = quote.FullPrice.ToString(inv),
            [StripeFulfillmentService.CheckoutFacts.Keys.CreditBasis] =
                (chosen is not null ? "bundle" : "single") + (subscriber ? "+subscriber" : ""),
            [StripeFulfillmentService.CheckoutFacts.Keys.TaxRate] = taxRate.ToString(inv),
            [StripeFulfillmentService.CheckoutFacts.Keys.TaxAmount] = tax.ToString(inv),
        };
        if (organizationId is { } owner)
            metadata[StripeFulfillmentService.CheckoutFacts.Keys.Organization] = owner.ToString();
        else
            metadata[StripeFulfillmentService.CheckoutFacts.Keys.CreditOwnerUser] = userId.ToString();

        var description = Describe(quote, subscriber, offer.SubscriberPercent);

        var handle = await stripe.CreateCheckoutSessionAsync(new StripeCheckoutSpec(
            organizationId ?? userId, buyerName, existingCustomerRef,
            quote.Payable, tax,
            $"IsHaunted — {description}",
            SuccessUrl: $"{returnUrl}?credits=success",
            CancelUrl:  $"{returnUrl}?credits=cancelled",
            metadata), ct);

        return new Outcome(new StartCheckoutResponse(handle.SessionUrl, PaidWithoutCharge: false), null, null);
    }

    /// <summary>What the purchase is, in the words the card statement and the ledger use.</summary>
    public static string Describe(EventCreditQuote quote, bool subscriber, decimal percent)
    {
        var what = quote.Quantity == 1 ? "1 event credit" : $"{quote.Quantity} event credits";
        if (quote.Bundle is not null) what += " (bundle)";
        if (subscriber) what += $", paid-plan price ({percent:0.##}%)";
        return what;
    }
}
