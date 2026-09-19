using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// What a period costs an organization, with the quantity behind the number (item 233).
/// </summary>
/// <remarks>
/// Four places priced a period — checkout, the quote, renewal and the admin screen — and each
/// counted members on its own. Per-tour pricing would have been a fifth copy in each. This is
/// the one place that counts, so the quote a business reads is the charge it gets.
/// </remarks>
public static class BillableUnits
{
    /// <summary>A period, priced: the band, the counts it was priced on, and the list price.</summary>
    /// <param name="Units">What the price is multiplied by — the live tours for a business, one for a group.</param>
    /// <param name="UnitPrice">One unit for one period at this cadence.</param>
    /// <param name="ListPrice"><see cref="UnitPrice"/> × <see cref="Units"/>, before any coupon.</param>
    public sealed record Priced(
        SubscriptionTier Tier, int Members, int Tours, int Units, decimal UnitPrice, decimal ListPrice);

    /// <summary>Tours the business still runs — the ones its plan is counted on.</summary>
    public static Task<int> ActiveToursAsync(BenDataContext db, Guid organizationId, CancellationToken ct)
        => db.Tours.CountAsync(t => t.OrganizationId == organizationId && t.RetiredAtUtc == null, ct);

    /// <summary>
    /// Prices one period for this organization at this cadence, or null when the band it
    /// resolves to is not sold that way.
    /// </summary>
    /// <remarks>The tier list must already have passed <see cref="SubscriptionTierResolver.Validate"/>.</remarks>
    public static async Task<Priced?> PriceAsync(
        BenDataContext db, IReadOnlyList<SubscriptionTier> tiers,
        Guid organizationId, OrganizationKind kind, BillingInterval interval, CancellationToken ct)
    {
        var members = await db.OrganizationUserMemberships
            .CountAsync(m => m.OrganizationId == organizationId && m.IsActive, ct);
        var tours = SubscriptionTierResolver.IsBusinessKind(kind)
            ? await ActiveToursAsync(db, organizationId, ct)
            : 0;

        var tier = SubscriptionTierResolver.Resolve(tiers, members, kind);

        // ── A group buying a plan buys the cheapest band that is actually SOLD ──
        //
        // Paying starts at the ADDITION of a person, not at existing: PaidPlan's rule is "One
        // person is free; working with other people is the paid part... the FIRST person never
        // meets this and the second is what asks for a plan." So the band a group BUYS is the one
        // it is moving into, not the one its current size sits in.
        //
        // Without this, adding a free band at 1-1 walls a new group in permanently (2026-09-18):
        // one member resolves to the free band, the free band lists at nothing,
        // OrganizationCheckoutController.Start refuses a list price of zero — "There is nothing to
        // subscribe to at that size" — and WhyCannotAddMemberAsync refuses the second member
        // without an active subscription. Cannot buy because too small, cannot grow because has
        // not bought. Every group begins with one member, its creator, so that is the whole
        // funnel rather than an edge case.
        //
        // Only the PURCHASE path steps up. SubscriptionTierResolver.Resolve stays as it is and
        // keeps answering the other question — which band's caps and capabilities govern this
        // group as it stands — where a solo group genuinely is on the free band.
        if (tier.IsBandedByMembers && SubscriptionPricing.PriceFor(tier, interval) is not > 0m)
        {
            var cheapestSold = tiers
                .Where(t => t.IsActive && t.IsBandedByMembers
                         && SubscriptionPricing.PriceFor(t, interval) is > 0m)
                .OrderBy(t => t.MinMembers)
                .FirstOrDefault();

            // When nothing at all is sold, the group is left on the band it actually resolved to,
            // so the zero list price reaches Start and it answers with the sentence written for
            // exactly this — "There is nothing to subscribe to at that size. A group is free by
            // not having a plan at all." Returning null here instead would have refused just as
            // firmly but said "that plan is not offered at that billing cadence", which is a
            // cadence problem and not what happened. A ladder with no paid band is a real state:
            // it is what a site that has not set its prices up yet looks like.
            if (cheapestSold is not null) tier = cheapestSold;
        }

        if (SubscriptionPricing.PriceFor(tier, interval) is not { } unitPrice) return null;

        // A business the ladder still prices (no business tier on offer) is one unit like a group:
        // its tours are counted only when the flat business tier is what it resolved to.
        var units = tier.IsBandedByMembers ? 1 : TourBilling.Units(kind, tours);
        return new Priced(tier, members, tours, units, unitPrice, TourBilling.ListPrice(unitPrice, units));
    }

    /// <summary>"3 tours" or "8 members" — the quantity a receipt line names.</summary>
    public static string Describe(Priced priced)
        => priced.Tier.IsBandedByMembers
            ? $"{priced.Members} members"
            : $"{priced.Units} tour{(priced.Units == 1 ? "" : "s")}";
}
