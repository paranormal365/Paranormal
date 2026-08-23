using Ben.Data.Source.Services;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// Which permission areas a group's plan includes (item 156, decisions D1/D4).
/// </summary>
/// <remarks>
/// <para><b>Resolution mirrors <see cref="SubscriptionLimitGuard"/>:</b> the group's current
/// subscription names the tier; a group with no subscription resolves by member count through
/// <see cref="SubscriptionTierResolver"/> — the same tier the limits and the pricing page would
/// show them.</para>
///
/// <para><b>Fail-open, deliberately, in every ambiguous case:</b> no tiers configured, an
/// invalid tier list, or a tier with zero area rows all read as ALL areas. A billing hiccup or a
/// half-configured price list must never lock a group out of its own permission model — a
/// deliberate downgrade (D4) is a tier whose checklist SAYS so, not an absence of data. Checked
/// at runtime on every ask, so a lapse or upgrade takes effect immediately (D4's
/// stop-at-runtime / resume-on-upgrade).</para>
/// </remarks>
public sealed class IncludedAreasResolver
{
    private static readonly IReadOnlySet<OrganizationPermissionArea> All =
        Enum.GetValues<OrganizationPermissionArea>().ToHashSet();

    private readonly IDbContextFactory<BenDataContext> _db;

    public IncludedAreasResolver(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>The areas included for this group, resolved from its effective tier.</summary>
    public async Task<IReadOnlySet<OrganizationPermissionArea>> ForOrganizationAsync(
        Guid organizationId, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        return await TierAreaResolution.IncludedAreasAsync(db, organizationId, ct);
    }

    /// <summary>Whether one table's area is included for this group. The Phase-D enforcement hook.</summary>
    public async Task<bool> IsIncludedAsync(
        Guid organizationId, OrganizationSecurityTable table, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        return await TierAreaResolution.IsIncludedAsync(db, organizationId, table, ct);
    }
}
