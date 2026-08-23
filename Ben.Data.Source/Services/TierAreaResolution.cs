using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.Source.Services;

/// <summary>
/// Which permission areas a group's plan includes — the shared core behind the WebApi's
/// <c>IncludedAreasResolver</c> and the security service's Phase-D area gate (item 156).
/// </summary>
/// <remarks>
/// One implementation on purpose: the role editor's graying, the runtime enforcement, and any
/// future surface must give the same answer to "does this plan include Cases?", and two copies
/// of the fail-open rules would eventually disagree. The rules: the group's current subscription
/// names the tier; no subscription resolves by member count; no tiers, an invalid tier list, or
/// a tier with zero checklist rows all read as ALL areas — only a checklist that SAYS so may
/// exclude (decision D4's deliberate-downgrade, never an accident of missing data).
/// </remarks>
public static class TierAreaResolution
{
    private static readonly IReadOnlySet<OrganizationPermissionArea> All =
        Enum.GetValues<OrganizationPermissionArea>().ToHashSet();

    /// <summary>The areas included for this group, resolved from its effective tier.</summary>
    public static async Task<IReadOnlySet<OrganizationPermissionArea>> IncludedAreasAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct = default)
        => (await ResolveAsync(db, organizationId, ct)).Areas;

    /// <summary>
    /// The areas plus the effective tier's name — the name a plan-limitation notice should say,
    /// whether the tier came from a subscription row or was resolved by member count. Name is
    /// null exactly when the fail-open rules answered ALL without landing on a tier.
    /// </summary>
    public static async Task<(IReadOnlySet<OrganizationPermissionArea> Areas, string? TierName)>
        ResolveAsync(BenDataContext db, Guid organizationId, CancellationToken ct = default)
    {
        var sub = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.DateCreated)
            .FirstOrDefaultAsync(ct);

        Guid? tierId = sub?.SubscriptionTierId;
        string? tierName = null;
        if (tierId is null)
        {
            var tiers = await db.SubscriptionTiers.AsNoTracking().ToListAsync(ct);
            if (tiers.Count == 0 || SubscriptionTierResolver.Validate(tiers) is not null)
                return (All, null);

            var members = await db.OrganizationUserMemberships.AsNoTracking()
                .CountAsync(m => m.OrganizationId == organizationId && m.IsActive, ct);
            var resolved = SubscriptionTierResolver.Resolve(tiers, members);
            tierId   = resolved.Id;
            tierName = resolved.Name;
        }
        else
        {
            tierName = await db.SubscriptionTiers.AsNoTracking()
                .Where(t => t.Id == tierId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(ct);
        }

        var areas = await db.SubscriptionTierPermissionAreas.AsNoTracking()
            .Where(a => a.SubscriptionTierId == tierId)
            .Select(a => a.Area)
            .ToListAsync(ct);

        return (areas.Count == 0 ? All : areas.ToHashSet(), tierName);
    }

    /// <summary>Whether one table's area is included. User-scoped tables are never tier-gated.</summary>
    public static async Task<bool> IsIncludedAsync(
        BenDataContext db, Guid organizationId, OrganizationSecurityTable table, CancellationToken ct = default)
    {
        if (PermissionAreas.AreaFor(table) is not { } area) return true;
        return (await IncludedAreasAsync(db, organizationId, ct)).Contains(area);
    }
}
