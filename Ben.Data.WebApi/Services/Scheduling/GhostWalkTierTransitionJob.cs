using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Tours;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// The one-time move from item 233's per-tour business plan to the Ghost Walk Tour subscription
/// (Ben, 10/21/2026: "merge the two").
/// </summary>
/// <remarks>
/// <para><b>Nobody is charged twice.</b> A group on the per-tour plan has already paid for its tours
/// up to the end of its current period. It is given a Ghost Walk Tour subscription for exactly that
/// time — the same number of routes as the tours it paid for, at no charge, ending when its plan's
/// period ends — with the plan's saved card, so the first Ghost Walk bill is the next one it would
/// have had anyway. Its per-tour plan is set not to renew, so that bill is not also taken by the
/// plan.</para>
///
/// <para><b>The per-tour tiers are retired.</b> Every active tier that is not banded by members is
/// switched off, so no new group is quoted the per-tour plan and business kinds are priced by the
/// member ladder for investigations, like anyone else.</para>
///
/// <para><b>It waits for the switch.</b> Nothing happens while the Ghost Walk Tour subscription is
/// off sale: until Ben turns it on, per-tour groups stay exactly as they are. On the first pass after
/// it is turned on, the move happens once, and it is recorded so it never happens again.</para>
/// </remarks>
public sealed class GhostWalkTierTransitionJob : IScheduledJob
{
    /// <summary>The setting that records the move has happened. Not in the settings page's list.</summary>
    public const string DoneKey = "tours.per-tour-plan-transition-done-utc";

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly ILogger<GhostWalkTierTransitionJob> _log;

    public GhostWalkTierTransitionJob(IDbContextFactory<BenDataContext> dbFactory, ILogger<GhostWalkTierTransitionJob> log)
    {
        _dbFactory = dbFactory;
        _log = log;
    }

    public string Name => "ghost-walk-tier-transition";

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (await SiteSettingsService.GetAsync(db, DoneKey, ct) is not null) return;

        var offer = await GhostWalkOffer.ReadAsync(db, ct);
        if (!offer.OnSale) return;

        var author = await db.Users.AsNoTracking().OrderBy(u => u.DateCreated)
            .Select(u => (Guid?)u.Id).FirstOrDefaultAsync(ct);
        if (author is null) return;

        var now = DateTime.UtcNow;
        var (moved, retired) = await MoveAsync(db, offer, author.Value, now, ct);

        db.SiteSettings.Add(new SiteSetting
        {
            Id = Guid.NewGuid(), Key = DoneKey, Value = now.ToString("O"),
            Description = "When the per-tour plan's groups were moved onto the Ghost Walk Tour subscription.",
            DateCreated = now, CreatedByAppUserId = author.Value,
        });
        await db.SaveChangesAsync(ct);

        _log.LogInformation(
            "Per-tour plan retired: {Groups} group(s) moved onto the Ghost Walk Tour subscription, {Tiers} tier(s) switched off.",
            moved, retired);
    }

    /// <summary>Moves the groups and retires the tiers, without saving.</summary>
    internal static async Task<(int Moved, int Retired)> MoveAsync(
        BenDataContext db, GhostWalkOffer offer, Guid authorId, DateTime now, CancellationToken ct)
    {
        var perTourTiers = await db.SubscriptionTiers
            .Where(t => !t.IsBandedByMembers)
            .ToListAsync(ct);
        var tierIds = perTourTiers.Select(t => t.Id).ToList();

        var plans = tierIds.Count == 0
            ? []
            : await db.OrganizationSubscriptions
                .Where(s => s.SubscriptionTierId != null && tierIds.Contains(s.SubscriptionTierId.Value)
                         && s.Status == SubscriptionStatus.Active
                         && s.CurrentPeriodEnd != null && s.CurrentPeriodEnd > now)
                .ToListAsync(ct);

        var moved = 0;
        foreach (var plan in plans)
        {
            var existing = await db.GhostWalkSubscriptions.FirstOrDefaultAsync(s => s.OrganizationId == plan.OrganizationId, ct);
            if (existing is not null && existing.IsStanding(now)) continue;

            var start = plan.CurrentPeriodStart ?? now;
            var end = plan.CurrentPeriodEnd!.Value;
            var routes = Math.Max(1, plan.TourCountAtPeriodStart);

            // One route for the period being carried over, at today's monthly price for each month
            // in it, so a route added part-way through a carried yearly period is prorated at the
            // monthly rate rather than at a twelfth of it.
            var months = Math.Max(1, (int)Math.Round((end - start).TotalDays / 30.4375, MidpointRounding.AwayFromZero));

            var sub = existing ?? new GhostWalkSubscription
            {
                Id = Guid.NewGuid(), OrganizationId = plan.OrganizationId,
                DateCreated = now, CreatedByAppUserId = authorId,
            };
            sub.Status = SubscriptionStatus.Active;
            sub.Quantity = routes;
            sub.PriceAtPeriodStart = 0m;          // already paid, on the plan
            sub.UnitPriceAtPeriodStart = offer.MonthlyPrice * months;
            sub.CurrentPeriodStart = start;
            sub.CurrentPeriodEnd = end;
            sub.CancelAtPeriodEnd = plan.CancelAtPeriodEnd;
            sub.ProviderName = plan.ProviderName;
            sub.ProviderSubscriptionRef = plan.ProviderSubscriptionRef;
            sub.ProviderCustomerRef = plan.ProviderCustomerRef;
            sub.ProviderPaymentMethodRef = plan.ProviderPaymentMethodRef;
            if (existing is null) db.GhostWalkSubscriptions.Add(sub);
            else { sub.DateUpdated = now; sub.UpdatedByAppUserId = authorId; }

            // The plan stands until its paid period ends, and is not renewed: the Ghost Walk bill is
            // what takes its place.
            plan.CancelAtPeriodEnd = true;
            plan.DateUpdated = now;
            plan.UpdatedByAppUserId = authorId;
            moved++;
        }

        var retired = 0;
        foreach (var tier in perTourTiers.Where(t => t.IsActive))
        {
            tier.IsActive = false;
            tier.DateUpdated = now;
            tier.UpdatedByAppUserId = authorId;
            retired++;
        }

        return (moved, retired);
    }
}
