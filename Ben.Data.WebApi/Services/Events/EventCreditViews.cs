using Ben.Data.Source.Context;
using Ben.Service.Models;
using Ben.Service.Models.Admin;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The credits card, for a group or for a person, priced the way checkout will price it.
/// </summary>
/// <remarks>
/// One builder for the two pages that show a holder their credits, so the price on the card and the
/// price at checkout come from the same <see cref="EventCreditOffer"/> and the same subscriber rule.
/// </remarks>
public static class EventCreditViews
{
    /// <summary>The card for a group (<paramref name="organizationId"/>) or a person (<paramref name="appUserId"/>).</summary>
    public static async Task<OrgEventCreditsView> BuildAsync(
        BenDataContext db, Guid? organizationId, Guid? appUserId, DateTime now, CancellationToken ct)
    {
        var credits = await db.EventCredits.AsNoTracking()
            .Where(c => organizationId != null
                ? c.OwnerOrganizationId == organizationId
                : c.OwnerAppUserId == appUserId)
            .OrderByDescending(c => c.PurchasedUtc)
            .Select(c => new OrgEventCreditRecord(
                c.Id, c.PriceAtPurchase, c.Currency, c.PurchasedUtc, c.ExpiresUtc,
                c.SpentUtc, c.SpentOnHostedEventId,
                c.SpentOnHostedEvent != null ? c.SpentOnHostedEvent.Name : null,
                c.RefundedUtc, c.ReceiptNumber, c.GrantedReason))
            .ToListAsync(ct);

        var offer = await EventCreditOffer.ReadAsync(db, ct);

        // A person buying for themselves is not a group, so has no plan to be a subscriber of.
        var subscriber = organizationId is { } org
                         && await EventCreditOffer.IsPaidSubscriberAsync(db, org, now, ct);

        var unit = EventCreditPricing.Quote(offer.UnitPrice, 1, null, subscriber, offer.SubscriberPercent).Payable;

        return new OrgEventCreditsView(
            OnSale: offer.OnSale,
            UnitPrice: unit,
            Currency: "USD",
            MaximumPerPurchase: EventCredits.MaximumPerPurchase,
            Spendable: credits.Count(c => c.SpentUtc is null && c.RefundedUtc is null && c.ExpiresUtc > now),
            Credits: credits,
            FullUnitPrice: offer.UnitPrice,
            PaysSubscriberPrice: subscriber,
            SubscriberPercent: offer.SubscriberPercent,
            Bundles: [.. offer.Bundles.Select(b => new EventCreditBundleOffer(
                b.Quantity, b.Price,
                EventCreditPricing.Quote(offer.UnitPrice, b.Quantity, b, subscriber, offer.SubscriberPercent).Payable,
                EventCreditPricing.AtPercent(b.Price, offer.SubscriberPercent)))]);
    }
}
