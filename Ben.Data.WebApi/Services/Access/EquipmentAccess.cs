using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Access;

/// <summary>
/// What a viewer may do with an <see cref="EquipmentItem"/>. A plain static helper, matching the
/// convention <see cref="InvestigationAccess"/>/<see cref="FileAudienceAccess"/> already set here.
/// </summary>
/// <remarks>
/// Personal items resolve to owner-or-SuperAdmin from the row itself, with no query. Group-owned
/// items need the caller's authority in that group, which is a query — so the org-owned overload
/// takes the answer as a parameter (<c>canManageOrgEquipment</c>) rather than resolving it per row.
/// Phase 4 adds <c>CanRequestCheckout</c>'s real borrow-eligibility rule.
/// </remarks>
public static class EquipmentAccess
{
    /// <summary>Flags for a single item, already loaded.</summary>
    public static EquipmentItemFlags ComputeItemFlags(EquipmentItem item, Guid userId, bool isSuperAdmin)
        => ComputeItemFlags(item, userId, isSuperAdmin, canManageOrgEquipment: false);

    /// <summary>
    /// Flags for a single item, given whether the caller may manage the owning group's equipment.
    /// </summary>
    /// <remarks>
    /// <paramref name="canManageOrgEquipment"/> is passed in rather than resolved here because it
    /// costs a query and is the same answer for every row in one group's list — resolving it per
    /// item is the N+1 that <c>OrganizationController</c>'s own comments warn about. It is ignored
    /// for personal items, whose only authority is ownership.
    /// </remarks>
    public static EquipmentItemFlags ComputeItemFlags(
        EquipmentItem item, Guid userId, bool isSuperAdmin, bool canManageOrgEquipment)
    {
        var isOwner   = userId != Guid.Empty && item.OwnerAppUserId == userId;
        var isOrgItem = item.OwningOrganizationId is not null;

        // Group gear has no owner to fall back on: authority over it comes from the group.
        var canManage = isSuperAdmin || (isOrgItem ? canManageOrgEquipment : isOwner);

        return new EquipmentItemFlags(
            IsOwner: isOwner,
            CanEdit: canManage,
            // Once checkout history exists (Phase 4), delete is replaced by retire for rows with
            // any history — that guard lives in the checkout-aware controller, not here.
            CanDelete: canManage,
            // Sharing is a personal-item idea: group gear already belongs to a group.
            CanManageSharing: isOwner && !isOrgItem,
            CanSeeSerial: canManage,
            // No checkout workflow exists yet (Phase 4) — nobody can request a loan.
            CanRequestCheckout: false,
            CanManageServiceLog: canManage);
    }

    /// <summary>
    /// Whether the caller may manage one organization's own equipment, resolved once for a whole
    /// request rather than per row.
    /// </summary>
    /// <remarks>
    /// Delegates to <see cref="IOrganizationSecurityService.HasAccessAsync"/>, so an org-created
    /// "Equipment Management" role, a direct access grant, and owner/administrator standing all
    /// behave identically here to everywhere else — the resolution order lives in one place, and
    /// this helper does not re-implement it.
    /// </remarks>
    public static async Task<bool> CanManageOrgEquipmentAsync(
        IOrganizationSecurityService security, Guid userId, Guid orgId, bool isSuperAdmin,
        OrganizationSecurityAction action, CancellationToken ct)
    {
        if (isSuperAdmin) return true;
        if (userId == Guid.Empty) return false;
        return await security.HasAccessAsync(userId, orgId, OrganizationSecurityTable.Equipment, action, ct);
    }

    /// <summary>
    /// Flags for a whole list of already-loaded items, computed without a per-row query — there is
    /// nothing to batch-query yet in Phase 1 (ownership is a column on the row itself), but the
    /// signature matches the batched shape every other access helper in this folder uses, so later
    /// phases that DO need per-org lookups (org-owned items, sharing) can extend this in place.
    /// </summary>
    public static IReadOnlyDictionary<Guid, EquipmentItemFlags> ComputeItemFlags(
        IEnumerable<EquipmentItem> items, Guid userId, bool isSuperAdmin, bool canManageOrgEquipment = false)
        => items.ToDictionary(i => i.Id, i => ComputeItemFlags(i, userId, isSuperAdmin, canManageOrgEquipment));

    /// <summary>
    /// Whether <paramref name="viewerUserId"/> can see this item by way of a group it is shared
    /// with — that is, a group both they and the owner are currently active members of.
    /// </summary>
    /// <remarks>
    /// Membership is verified live for both sides rather than being inferred from the share row
    /// existing. A share is a standing statement ("this group may see my recorder"), and it should
    /// stop meaning anything the moment either party is no longer in that group — otherwise leaving
    /// a group would quietly leave your gear visible to it.
    /// </remarks>
    public static Task<bool> IsSharedWithAGroupSharedWithAsync(
        BenDataContext db, Guid itemId, Guid? ownerUserId, Guid viewerUserId, CancellationToken ct)
    {
        if (ownerUserId is null || viewerUserId == Guid.Empty) return Task.FromResult(false);

        return db.EquipmentItemShares.AsNoTracking()
            .Where(s => s.EquipmentItemId == itemId)
            .AnyAsync(s =>
                db.OrganizationUserMemberships.Any(m =>
                    m.OrganizationId == s.OrganizationId && m.AppUserId == viewerUserId && m.IsActive)
                && db.OrganizationUserMemberships.Any(m =>
                    m.OrganizationId == s.OrganizationId && m.AppUserId == ownerUserId && m.IsActive),
                ct);
    }

    /// <summary>
    /// Ownership check for the <c>api/me/equipment</c> surface: matches id AND owner together and
    /// answers with the row (or null) rather than a bool, so callers return 404 — not 403 — on a
    /// mismatch. Confirming an id exists to someone who does not own it is its own small leak.
    /// </summary>
    public static Task<EquipmentItem?> FindOwnedAsync(
        BenDataContext db, Guid itemId, Guid ownerUserId, CancellationToken ct)
        => db.EquipmentItems
            .Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.OwnerAppUserId == ownerUserId, ct);
}
