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
        var (tierId, tierName) = await EffectiveTierAsync(db, organizationId, ct);
        if (tierId is null) return (All, null);

        var areas = await db.SubscriptionTierPermissionAreas.AsNoTracking()
            .Where(a => a.SubscriptionTierId == tierId)
            .Select(a => a.Area)
            .ToListAsync(ct);

        return (areas.Count == 0 ? All : areas.ToHashSet(), tierName);
    }

    /// <summary>
    /// Whether this group's plan includes a capability (item 167). Capabilities are stored as
    /// EXCLUSION rows, so the fail-open property is structural: no resolvable tier, or a tier
    /// nobody has excluded anything from, reads as everything-included — only a row that SAYS
    /// so may take a capability away.
    /// </summary>
    public static async Task<(bool Included, string? TierName)> HasCapabilityAsync(
        BenDataContext db, Guid organizationId, TierCapability capability, CancellationToken ct = default)
    {
        var (tierId, tierName) = await EffectiveTierAsync(db, organizationId, ct);
        if (tierId is null) return (true, null);

        var excluded = await db.SubscriptionTierExcludedCapabilities.AsNoTracking()
            .AnyAsync(c => c.SubscriptionTierId == tierId && c.Capability == capability, ct);

        return (!excluded, tierName);
    }

    /// <summary>
    /// The tier that governs this group: its subscription row's tier when one exists, else the
    /// band its member count lands in. (null, null) means the fail-open rules answer
    /// everything-included without landing on a tier at all.
    /// </summary>
    private static async Task<(Guid? TierId, string? TierName)> EffectiveTierAsync(
        BenDataContext db, Guid organizationId, CancellationToken ct)
    {
        var sub = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(s => s.OrganizationId == organizationId)
            .OrderByDescending(s => s.DateCreated)
            .FirstOrDefaultAsync(ct);

        if (sub?.SubscriptionTierId is { } tierId)
        {
            var tierName = await db.SubscriptionTiers.AsNoTracking()
                .Where(t => t.Id == tierId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(ct);
            return (tierId, tierName);
        }

        var tiers = await db.SubscriptionTiers.AsNoTracking().ToListAsync(ct);
        if (tiers.Count == 0 || SubscriptionTierResolver.Validate(tiers) is not null)
            return (null, null);

        var members = await db.OrganizationUserMemberships.AsNoTracking()
            .CountAsync(m => m.OrganizationId == organizationId && m.IsActive, ct);
        var resolved = SubscriptionTierResolver.Resolve(tiers, members);
        return (resolved.Id, resolved.Name);
    }

    /// <summary>
    /// Which of <paramref name="organizationIds"/> hold <paramref name="capability"/> — resolved
    /// for a whole listing in a fixed number of queries.
    /// </summary>
    /// <remarks>
    /// <para>Same rules as <see cref="HasCapabilityAsync"/>, including fail-open: a group with no
    /// resolvable tier, or a tier with no exclusion row, HOLDS the capability. Only a checklist
    /// that says so refuses.</para>
    ///
    /// <para>Exists because item 194 puts the answer on every card in the group finder, and asking
    /// per card is the N+1 that turns a browse page into forty round trips. The per-group method
    /// stays for the single-group questions the gates ask.</para>
    /// </remarks>
    public static async Task<HashSet<Guid>> WithCapabilityAsync(
        BenDataContext db, IReadOnlyCollection<Guid> organizationIds,
        TierCapability capability, CancellationToken ct = default)
    {
        var holders = new HashSet<Guid>(organizationIds);
        if (organizationIds.Count == 0) return holders;

        var tiers = await db.SubscriptionTiers.AsNoTracking().ToListAsync(ct);
        var tiersUsable = tiers.Count > 0 && SubscriptionTierResolver.Validate(tiers) is null;

        // The tier each group actually answers to: its subscription's, else its member band.
        var subs = await db.OrganizationSubscriptions.AsNoTracking()
            .Where(s => organizationIds.Contains(s.OrganizationId))
            .OrderByDescending(s => s.DateCreated)
            .Select(s => new { s.OrganizationId, s.SubscriptionTierId })
            .ToListAsync(ct);
        var tierByOrg = subs
            .GroupBy(s => s.OrganizationId)
            .Where(g => g.First().SubscriptionTierId is not null)
            .ToDictionary(g => g.Key, g => g.First().SubscriptionTierId!.Value);

        if (tiersUsable)
        {
            var needBand = organizationIds.Where(id => !tierByOrg.ContainsKey(id)).ToList();
            if (needBand.Count > 0)
            {
                var counts = await db.OrganizationUserMemberships.AsNoTracking()
                    .Where(m => needBand.Contains(m.OrganizationId) && m.IsActive)
                    .GroupBy(m => m.OrganizationId)
                    .Select(g => new { OrganizationId = g.Key, Count = g.Count() })
                    .ToListAsync(ct);
                var countByOrg = counts.ToDictionary(c => c.OrganizationId, c => c.Count);

                foreach (var id in needBand)
                    tierByOrg[id] = SubscriptionTierResolver
                        .Resolve(tiers, countByOrg.TryGetValue(id, out var n) ? n : 0).Id;
            }
        }

        if (tierByOrg.Count == 0) return holders;   // nothing resolvable: everyone holds it

        var excludedTiers = await db.SubscriptionTierExcludedCapabilities.AsNoTracking()
            .Where(c => c.Capability == capability
                     && tierByOrg.Values.Contains(c.SubscriptionTierId))
            .Select(c => c.SubscriptionTierId)
            .ToHashSetAsync(ct);

        foreach (var (orgId, tierId) in tierByOrg)
            if (excludedTiers.Contains(tierId)) holders.Remove(orgId);

        return holders;
    }

    /// <summary>Whether one table's area is included. User-scoped tables are never tier-gated.</summary>
    public static async Task<bool> IsIncludedAsync(
        BenDataContext db, Guid organizationId, OrganizationSecurityTable table, CancellationToken ct = default)
    {
        if (PermissionAreas.AreaFor(table) is not { } area) return true;
        return (await IncludedAreasAsync(db, organizationId, ct)).Contains(area);
    }
}
