using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// Item 144, Ben's overflow-seat model: when a new member joins a group that has outgrown its
/// band, the group's contract stays at its band and the NEW MEMBER is billed individually at
/// the tier's per-extra-member price.
/// </summary>
/// <remarks>
/// <para><b>Joining is never blocked.</b> In the manual-billing era the seat is a billing
/// record, not a turnstile: the member is in from the moment they are accepted, holding a
/// <see cref="SubscriptionStatus.PendingPayment"/> seat, and a SuperAdmin activates it when the
/// payment is recorded — the same manual flow as group subscriptions.</para>
/// <para><b>The band that counts is the FROZEN one.</b> The group's current period was sold at
/// <c>MemberCountAtPeriodStart</c> on a specific tier; whether this join goes past the band is
/// judged against that tier's cap, not against whatever the live price list says today.</para>
/// <para>The price is frozen on the seat at offer time, the same rule as every money figure.</para>
/// </remarks>
public static class OverflowSeats
{
    /// <summary>
    /// Offers a seat when this join goes past the group's band, or does nothing. Adds the seat
    /// to the context WITHOUT saving — the caller owns the transaction — and returns it so the
    /// caller can word the acceptance message.
    /// </summary>
    public static async Task<MemberSeatSubscription?> MaybeOfferSeatAsync(
        BenDataContext db, Guid orgId, Guid newMemberUserId, Guid actingUserId, CancellationToken ct)
    {
        var sub = await db.OrganizationSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OrganizationId == orgId, ct);
        if (sub is null || sub.Status != SubscriptionStatus.Active || sub.SubscriptionTierId is not { } tierId)
            return null; // free or lapsed groups have no band to outgrow

        var tier = await db.SubscriptionTiers.AsNoTracking()
            .Include(t => t.Prices)
            .FirstOrDefaultAsync(t => t.Id == tierId, ct);
        if (tier?.MaxMembers is not { } bandMax) return null; // unbounded band — nothing to outgrow

        var pricePerExtra = tier.Prices
            .FirstOrDefault(p => p.IsActive && p.Interval == sub.Interval)?.PricePerExtraMember;
        if (pricePerExtra is not { } price) return null; // tier does not sell overflow seats

        // Counted INCLUDING the member being accepted — the caller adds the membership to the
        // same context before calling, so the tracked row is part of the count.
        var activeMembers = await db.OrganizationUserMemberships
            .CountAsync(m => m.OrganizationId == orgId && m.IsActive, ct)
            + db.ChangeTracker.Entries<OrganizationUserMembership>()
                .Count(e => e.State == EntityState.Added
                         && e.Entity.OrganizationId == orgId && e.Entity.IsActive);
        if (activeMembers <= bandMax) return null; // still inside the band

        if (await db.MemberSeatSubscriptions.AnyAsync(
                s => s.OrganizationId == orgId && s.AppUserId == newMemberUserId, ct))
            return null; // rejoining member already holds a seat row

        var seat = new MemberSeatSubscription
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = orgId,
            AppUserId          = newMemberUserId,
            Status             = SubscriptionStatus.PendingPayment,
            Interval           = sub.Interval,
            PriceAtStart       = price,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = actingUserId,
        };
        db.MemberSeatSubscriptions.Add(seat);
        return seat;
    }

    /// <summary>The cadence noun for a seat-offer sentence.</summary>
    public static string CadenceNoun(BillingInterval interval)
        => interval == BillingInterval.Yearly ? "year" : "month";
}
