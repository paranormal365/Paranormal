using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Access;

/// <summary>
/// What a viewer may do with an <see cref="EquipmentItem"/>. A plain static helper, matching the
/// convention <see cref="InvestigationAccess"/>/<see cref="FileAudienceAccess"/> already set here.
/// </summary>
/// <remarks>
/// Phase 1 only ever sees personal items (<c>OwnerAppUserId</c> set), so the resolution below is
/// just owner-or-SuperAdmin. Later phases extend this in place rather than duplicating it:
/// Phase 3 adds the org-owned branch (org <c>Equipment</c>/<c>EquipmentCheckout</c> permission,
/// resolved the same way <see cref="InvestigationAccess.HasOrgAuthorityAsync"/> style helpers do
/// elsewhere in this folder), and Phase 4 adds <c>CanRequestCheckout</c>'s real borrow-eligibility
/// rule (shared-with-a-group-the-caller-belongs-to, or org membership for org-owned gear).
/// </remarks>
public static class EquipmentAccess
{
    /// <summary>Flags for a single item, already loaded.</summary>
    public static EquipmentItemFlags ComputeItemFlags(EquipmentItem item, Guid userId, bool isSuperAdmin)
    {
        var isOwner = userId != Guid.Empty && item.OwnerAppUserId == userId;
        var canManage = isOwner || isSuperAdmin;

        return new EquipmentItemFlags(
            IsOwner: isOwner,
            CanEdit: canManage,
            // Once checkout history exists (Phase 4), delete is replaced by retire for rows with
            // any history — that guard lives in the checkout-aware controller, not here.
            CanDelete: canManage,
            CanManageSharing: isOwner,
            CanSeeSerial: canManage,
            // No checkout workflow exists yet (Phase 4) — nobody can request a loan.
            CanRequestCheckout: false,
            CanManageServiceLog: false);
    }

    /// <summary>
    /// Flags for a whole list of already-loaded items, computed without a per-row query — there is
    /// nothing to batch-query yet in Phase 1 (ownership is a column on the row itself), but the
    /// signature matches the batched shape every other access helper in this folder uses, so later
    /// phases that DO need per-org lookups (org-owned items, sharing) can extend this in place.
    /// </summary>
    public static IReadOnlyDictionary<Guid, EquipmentItemFlags> ComputeItemFlags(
        IEnumerable<EquipmentItem> items, Guid userId, bool isSuperAdmin)
        => items.ToDictionary(i => i.Id, i => ComputeItemFlags(i, userId, isSuperAdmin));

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
