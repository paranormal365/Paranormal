using System.Globalization;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Billing;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// Opening a Stripe checkout for extra months on one show's run (10/24/2026).
/// </summary>
/// <remarks>
/// <para><b>Only by choosing the show.</b> Ben: "The discount only applies if they select the show to extend and
/// it matches one currently running or in the future." So there is no way to buy months on their own: the show
/// is checked here, named in the metadata, and the months are applied to it when the payment lands.</para>
///
/// <para><b>Priced here, frozen into the metadata</b>, like credits: the percentage of what this buyer pays
/// for a credit (a group with a paid plan pays its percentage first), times the months, plus tax for a
/// group. Fulfilment never re-prices.</para>
/// </remarks>
public static class EventRunMonthCheckout
{
    /// <summary>
    /// Starts the checkout. <paramref name="payerOrganizationId"/> is the group paying, or null for the person.
    /// </summary>
    public static async Task<EventCreditCheckout.Outcome> StartAsync(
        BenDataContext db, IStripeGateway stripe, Guid showId,
        Guid? payerOrganizationId, string buyerName, string? existingCustomerRef,
        Guid userId, int count, string returnUrl, CancellationToken ct)
    {
        var offer = await EventCreditOffer.ReadAsync(db, ct);
        if (!offer.OnSale) return new(null, "Extra months aren't on sale at the moment.", null);

        var show = await db.HostedEvents.AsNoTracking().Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == showId, ct);
        if (show is null) return new(null, "That show isn't there any more.", null);

        var now = DateTime.UtcNow;
        var (percent, cap) = await EventRunMonths.ReadAsync(db, ct);
        var applied = await EventRunMonths.AppliedToAsync(db, showId, ct);
        var lastEnds = HostedEventCalendarSync.Window(show, [.. show.Nights.OrderBy(n => n.Date)]).EndUtc;
        if (EventRunMonths.WhyNotExtend(show, applied, count, cap, lastEnds, now) is { } why)
            return new(null, why, null);

        var subscriber = payerOrganizationId is { } org && await EventCreditOffer.IsPaidSubscriberAsync(db, org, now, ct);
        var each = EventRunMonths.PriceOfAMonth(offer, subscriber, percent);
        var payable = EventCreditPricing.Cents(each * count);
        if (payable <= 0m) return new(null, null, "Extra months aren't priced yet.");

        decimal taxRate = 0m;
        if (payerOrganizationId is { } taxedOrg)
            (_, taxRate) = await TaxResolver.ForOrganizationAsync(db, taxedOrg, ct);
        var tax = TaxResolver.TaxOn(payable, taxRate);

        if (!stripe.IsConfigured)
            return new(null, null, "Online payment isn't set up yet — contact us and we'll sort it out directly.");

        var inv = CultureInfo.InvariantCulture;
        var metadata = new Dictionary<string, string>
        {
            [StripeFulfillmentService.CheckoutFacts.Keys.User] = userId.ToString(),
            [StripeFulfillmentService.CheckoutFacts.Keys.RunMonths] = count.ToString(inv),
            [StripeFulfillmentService.CheckoutFacts.Keys.RunShow] = showId.ToString(),
            [StripeFulfillmentService.CheckoutFacts.Keys.List] = payable.ToString(inv),
            [StripeFulfillmentService.CheckoutFacts.Keys.CreditBasis] = subscriber ? "single+subscriber" : "single",
            [StripeFulfillmentService.CheckoutFacts.Keys.TaxRate] = taxRate.ToString(inv),
            [StripeFulfillmentService.CheckoutFacts.Keys.TaxAmount] = tax.ToString(inv),
        };
        if (payerOrganizationId is { } owner)
            metadata[StripeFulfillmentService.CheckoutFacts.Keys.Organization] = owner.ToString();
        else
            metadata[StripeFulfillmentService.CheckoutFacts.Keys.CreditOwnerUser] = userId.ToString();

        var handle = await stripe.CreateCheckoutSessionAsync(new StripeCheckoutSpec(
            payerOrganizationId ?? userId, buyerName, existingCustomerRef,
            payable, tax,
            $"IsHaunted — {Describe(count, show.Name)}",
            SuccessUrl: $"{returnUrl}?months=success",
            CancelUrl:  $"{returnUrl}?months=cancelled",
            metadata), ct);

        return new(new StartCheckoutResponse(handle.SessionUrl, PaidWithoutCharge: false), null, null);
    }

    /// <summary>What the purchase is, in the words the card statement, the ledger and the receipt use.</summary>
    public static string Describe(int count, string? showName)
        => $"{(count == 1 ? "1 extra month" : $"{count} extra months")} for {showName ?? "a show"}'s run";
}
