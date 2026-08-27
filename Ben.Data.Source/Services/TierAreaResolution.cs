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
    /// The tier that governs this group: its subscription's tier when it has one, otherwise the
    /// free tier.
    /// </summary>
    /// <remarks>
    /// <para><b>Free is a choice, not a size (Ben, 2026-08-27).</b> "A free version doesn't care
    /// about the number of people. It only cares about privacy." Any group may stay free forever
    /// and do public investigations; paying is what buys private-residence work, and member count
    /// then decides only what the paid plan COSTS.</para>
    ///
    /// <para><b>What this replaced, and the hole it closed.</b> A group with no subscription used
    /// to be assigned a band by headcount, so growing past three members silently granted the paid
    /// capability to somebody paying nothing — two of the five seeded groups were in exactly that
    /// state when this was found. Headcount buying privacy is backwards, and it was also a
    /// straightforward revenue leak.</para>
    ///
    /// <para><b>Member-count banding still exists</b>, and is still right, for PRICING: the quote
    /// asks what a group of this size would pay. It just no longer decides whether they are free.
    /// See <see cref="SubscriptionTierResolver.Resolve"/>, still used there.</para>
    ///
    /// <para><b>Fail-open survives where it belongs.</b> No tiers configured at all means no
    /// pricing model to enforce, and everything stays included — that is the state every database
    /// is in before pricing is set up. Once tiers EXIST, "no subscription" is an answer rather
    /// than an absence, and the answer is free.</para>
    /// </remarks>
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

        return await FreeTierAsync(db, ct);
    }

    /// <summary>
    /// The tier a group is on when it pays nothing, or (null, null) when no pricing exists yet.
    /// </summary>
    /// <remarks>
    /// Identified by costing nothing rather than by a name or a flag: "free" is a fact about the
    /// price list, so reading it from the price list keeps the two from drifting. Renaming the
    /// band, or reordering it, cannot break this; only changing its price can, which is the one
    /// change that SHOULD.
    /// </remarks>
    public static async Task<(Guid? TierId, string? TierName)> FreeTierAsync(
        BenDataContext db, CancellationToken ct = default)
    {
        var free = await db.SubscriptionTiers.AsNoTracking()
            .Where(t => t.IsActive && t.Prices.Any() && t.Prices.All(p => p.Price == 0m))
            .OrderBy(t => t.SortOrder)
            .Select(t => new { t.Id, t.Name })
            .FirstOrDefaultAsync(ct);

        // No free band configured means the pricing model does not describe this case, and
        // inventing a restriction would lock people out of a site that never said it would.
        return free is null ? (null, null) : (free.Id, free.Name);
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

        // Free is a choice, not a size: a group with no subscription is on the free tier, whatever
        // its headcount. Resolved once for the whole page, the same answer EffectiveTierAsync
        // gives one group at a time.
        var (freeTierId, _) = await FreeTierAsync(db, ct);

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

        if (freeTierId is { } freeId)
            foreach (var id in organizationIds.Where(id => !tierByOrg.ContainsKey(id)))
                tierByOrg[id] = freeId;

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
