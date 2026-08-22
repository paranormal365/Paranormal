using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// Opens one billing period: freezes the inputs, snapshots the contract, sets the flags.
/// </summary>
/// <remarks>
/// <para><b>One code path on purpose.</b> Today the manual Administration screen calls this; when
/// Square or PayPal arrives, its webhook calls the same method. Period-opening carries every
/// contract rule at once — freeze the member count, freeze the price, snapshot the terms, set
/// first-paid exactly once — and two implementations of that list would disagree within a month.</para>
///
/// <para><b>Renewal is not a special case.</b> Opening the next period from the LIVE tier is
/// precisely how a queued reduction lands: the new snapshot simply does not contain the old
/// terms. The pre-renewal notice (phase C) promised "at your renewal", and this is the mechanism
/// that keeps the promise.</para>
///
/// <para>The mutation happens on the caller's tracked entities and context — the opener decides
/// <i>what</i> the period looks like, the caller decides the transaction it commits in.</para>
/// </remarks>
public static class PeriodOpener
{
    /// <summary>
    /// Writes one period onto the subscription and returns the snapshot to add, or null when the
    /// state has no contract to snapshot (free, or no dates).
    /// </summary>
    /// <param name="subscription">The tracked subscription the period is written onto.</param>
    /// <param name="tier">The tier to snapshot, or null for free — which is what makes the return null.</param>
    /// <param name="status">The status the subscription moves to.</param>
    /// <param name="interval">Monthly or yearly, snapshotted so a later price change cannot rewrite this period.</param>
    /// <param name="periodStart">Start of the period; null (with <paramref name="periodEnd"/>) means there is nothing to snapshot.</param>
    /// <param name="periodEnd">End of the period.</param>
    /// <param name="memberCount">Active members right now — frozen for the whole period.</param>
    /// <param name="byUserId">Who caused the period to open, recorded on the snapshot.</param>
    public static SubscriptionContractTerms? Open(
        OrganizationSubscription subscription,
        SubscriptionTier? tier,
        SubscriptionStatus status,
        BillingInterval interval,
        DateTime? periodStart,
        DateTime? periodEnd,
        int memberCount,
        Guid byUserId)
    {
        var now = DateTime.UtcNow;

        subscription.Status                  = status;
        subscription.SubscriptionTierId      = tier?.Id;
        subscription.Interval                = interval;
        subscription.CurrentPeriodStart      = periodStart;
        subscription.CurrentPeriodEnd        = periodEnd;
        subscription.MemberCountAtPeriodStart = memberCount;
        subscription.PriceAtPeriodStart      = tier is null
            ? 0m
            : SubscriptionPricing.PriceFor(tier, interval) ?? 0m;

        // Set once and never cleared — the fact behind renewal-vs-acquisition coupons. A lapsed
        // group has still paid before.
        if (status == SubscriptionStatus.Active && subscription.FirstPaidPeriodStartUtc is null)
            subscription.FirstPaidPeriodStartUtc = periodStart ?? now;

        subscription.LapsedAtUtc = status == SubscriptionStatus.Lapsed
            ? subscription.LapsedAtUtc ?? now
            : null;

        // A contract exists when something was actually bought for an actual period.
        return status == SubscriptionStatus.Active && tier is not null
               && periodStart is { } ps && periodEnd is { } pe
            ? EffectiveTermsResolver.Snapshot(
                subscription, tier, interval, subscription.PriceAtPeriodStart, ps, pe, byUserId)
            : null;
    }

    /// <summary>
    /// Un-pauses exactly the cases a lapse paused, each back to its own prior status.
    /// </summary>
    /// <remarks>
    /// <c>StatusBeforePause</c> is the marker AND the destination: only cases the lapse job
    /// suspended carry it, so a case paused for any other future reason is left alone, and an
    /// Active investigation resumes Active rather than a guessed default. Returns how many came
    /// back, for the log line.
    /// </remarks>
    public static async Task<int> RestorePausedCasesAsync(
        BenDataContext db, Guid organizationId, DateTime now, CancellationToken ct)
    {
        var pausedByLapse = await db.Cases
            .Where(c => c.OrganizationId == organizationId
                     && c.Status == CaseStatus.Paused
                     && c.StatusBeforePause != null)
            .ToListAsync(ct);

        foreach (var c in pausedByLapse)
        {
            c.Status            = c.StatusBeforePause!.Value;
            c.StatusBeforePause = null;
            c.DateUpdated       = now;
        }

        return pausedByLapse.Count;
    }

    /// <summary>
    /// Removes any snapshot already covering <paramref name="periodStart"/> for this subscription,
    /// so re-setting the same period replaces its contract rather than stacking two.
    /// </summary>
    public static async Task ReplaceSnapshotAsync(
        BenDataContext db, Guid subscriptionId, DateTime periodStart, CancellationToken ct)
    {
        var existing = await db.SubscriptionContractTerms
            .Where(t => t.OrganizationSubscriptionId == subscriptionId && t.PeriodStartUtc == periodStart)
            .ToListAsync(ct);

        db.SubscriptionContractTerms.RemoveRange(existing);
    }
}
