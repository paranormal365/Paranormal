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
/// Whether somebody may <i>borrow</i> a piece is a richer question with its own answer — see
/// <see cref="ComputeBorrowEligibilityAsync"/>, which explains why rather than just saying no.
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
    /// Flags for a whole list of already-loaded items. Ownership is a column on the row, and the
    /// org verdict is resolved once by the caller and passed in, so this needs no query of its own
    /// — while keeping the batched shape every other access helper in this folder uses.
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

    // ── Who is looking at one item (Phase 6b/6c) ─────────────────────────────

    /// <summary>How a caller relates to one piece of equipment, most privileged first.</summary>
    public enum ItemAudience
    {
        /// <summary>No route to it at all. Answer 404 — an id must not be probeable.</summary>
        None,
        /// <summary>Can see the piece but not who owns it — a passer-by on the public catalog.</summary>
        Public,
        /// <summary>A member of a group the piece is shared with, or of the group that owns it.</summary>
        Member,
        /// <summary>Holds the owning group's Equipment permission.</summary>
        OrgManager,
        Owner,
        SuperAdmin,
    }

    /// <summary>
    /// Resolves, in one pass, which of those a caller is for this item.
    /// </summary>
    /// <remarks>
    /// The single visibility rule for a piece of equipment. The item page, its FAQ and its question
    /// channel all ask this rather than each deciding for itself — three near-identical predicates
    /// would eventually disagree, and the one that disagreed generously would be the leak.
    /// </remarks>
    public static async Task<ItemAudience> ResolveItemAudienceAsync(
        BenDataContext db, IOrganizationSecurityService security, EquipmentItem item,
        Guid userId, bool isSuperAdmin, CancellationToken ct)
    {
        if (isSuperAdmin) return ItemAudience.SuperAdmin;
        if (userId != Guid.Empty && item.OwnerAppUserId == userId) return ItemAudience.Owner;

        if (item.OwningOrganizationId is Guid orgId && userId != Guid.Empty)
        {
            if (await CanManageOrgEquipmentAsync(
                    security, userId, orgId, false, OrganizationSecurityAction.Read, ct))
                return ItemAudience.OrgManager;

            var isMember = await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);
            if (isMember) return ItemAudience.Member;
        }

        if (userId != Guid.Empty && !item.IsRetired
            && await IsSharedWithAGroupSharedWithAsync(db, item.Id, item.OwnerAppUserId, userId, ct))
            return ItemAudience.Member;

        // Retired gear leaves circulation — the public route closes with it.
        if (item.IncludeInGlobalCatalog && !item.IsRetired) return ItemAudience.Public;

        return ItemAudience.None;
    }

    /// <summary>
    /// Whether this caller is responsible for the piece, as opposed to merely allowed to look at it.
    /// The audience that may write its FAQ, answer its questions, and see its serial.
    /// </summary>
    public static bool IsCustodian(ItemAudience audience)
        => audience is ItemAudience.Owner or ItemAudience.OrgManager or ItemAudience.SuperAdmin;

    // ── Checkouts (Phase 4) ──────────────────────────────────────────────────

    /// <summary>
    /// Whether <paramref name="userId"/> may review loans of this item — approve, deny, hand over,
    /// receive back.
    /// </summary>
    /// <remarks>
    /// The approver is a property of the item, not of the loan: group-owned gear is reviewed by
    /// holders of <see cref="OrganizationSecurityTable.EquipmentCheckout"/> in the owning group,
    /// and a member's personal gear is always reviewed by its owner. Deliberately one place, so
    /// every transition endpoint asks the same question.
    /// </remarks>
    public static async Task<bool> CanReviewCheckoutAsync(
        IOrganizationSecurityService security, EquipmentItem item, Guid userId, bool isSuperAdmin,
        CancellationToken ct)
    {
        if (isSuperAdmin) return true;
        if (userId == Guid.Empty) return false;

        if (item.OwningOrganizationId is Guid orgId)
            return await security.HasAccessAsync(
                userId, orgId, OrganizationSecurityTable.EquipmentCheckout,
                OrganizationSecurityAction.Update, ct);

        // Personal gear: the owner decides, and nobody's group permission overrides that.
        return item.OwnerAppUserId == userId;
    }

    /// <summary>
    /// Whether the caller may ask to borrow this item, and which groups they could borrow it for.
    /// </summary>
    /// <remarks>
    /// <para>Reads <see cref="EquipmentItem.LoanAudience"/>, whose three flags answer different
    /// questions. <c>SharedGroups</c> offers the groups the item is shared with that the caller is
    /// also in — those loans are attributed to a group. <c>GroupMembers</c> and
    /// <c>IndividualUsers</c> both offer a personal loan with no group; they differ in reach, the
    /// former requiring a shared group with the owner and the latter requiring nothing.</para>
    ///
    /// <para>Group-owned gear ignores the audience entirely: it is borrowable by the group's active
    /// members, always for that group.</para>
    ///
    /// <para>The reasons are written to be shown to a person, because a borrow button that is
    /// simply missing tells them nothing.</para>
    /// </remarks>
    public static async Task<BorrowEligibilityRecord> ComputeBorrowEligibilityAsync(
        BenDataContext db, EquipmentItem item, Guid userId, CancellationToken ct)
    {
        // Somebody already has it, or has been promised it. Asking is still allowed — that forms a
        // queue — but the caller needs to know, or "Available to borrow" is a lie.
        var activeLoan = await db.EquipmentCheckouts.AsNoTracking()
            .Where(c => c.EquipmentItemId == item.Id
                     && (c.Status == EquipmentCheckoutStatus.Approved
                         || c.Status == EquipmentCheckoutStatus.CheckedOut))
            .Select(c => new { c.DateDue })
            .FirstOrDefaultAsync(ct);
        var isOut = activeLoan is not null;
        var backOn = activeLoan?.DateDue;

        if (userId == Guid.Empty)
            return new BorrowEligibilityRecord(item.Id, false, "You need to be signed in to borrow equipment.", [], isOut, backOn);

        if (item.IsRetired)
            return new BorrowEligibilityRecord(item.Id, false, "This equipment has been retired.", [], isOut, backOn);

        // Group-owned: any active member may ask, always on the group's behalf.
        if (item.OwningOrganizationId is Guid owningOrgId)
        {
            var isMember = await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == owningOrgId && m.AppUserId == userId && m.IsActive, ct);
            if (!isMember)
                return new BorrowEligibilityRecord(item.Id, false, "Only members of the group that owns this can borrow it.", [], isOut, backOn);

            var orgName = await db.Organizations.AsNoTracking()
                .Where(o => o.Id == owningOrgId).Select(o => o.Name).FirstOrDefaultAsync(ct);
            return new BorrowEligibilityRecord(item.Id, true, null, [new BorrowOptionRecord(owningOrgId, orgName ?? "the group")], isOut, backOn);
        }

        if (item.OwnerAppUserId == userId)
            return new BorrowEligibilityRecord(item.Id, false, "This is your own equipment.", [], isOut, backOn);

        if (item.LoanAudience == EquipmentLoanAudience.NotLoanable)
            return new BorrowEligibilityRecord(item.Id, false, "The owner isn't lending this out.", [], isOut, backOn);

        var options = new List<BorrowOptionRecord>();

        // Borrowing FOR a group: the groups this item is shared with that the caller is in too.
        if ((item.LoanAudience & EquipmentLoanAudience.SharedGroups) != 0)
        {
            var groups = await db.EquipmentItemShares.AsNoTracking()
                .Where(s => s.EquipmentItemId == item.Id)
                .Where(s => db.OrganizationUserMemberships.Any(m =>
                                m.OrganizationId == s.OrganizationId && m.AppUserId == userId && m.IsActive)
                         && db.OrganizationUserMemberships.Any(m =>
                                m.OrganizationId == s.OrganizationId && m.AppUserId == item.OwnerAppUserId && m.IsActive))
                .Join(db.Organizations.AsNoTracking(), s => s.OrganizationId, o => o.Id, (s, o) => new { o.Id, o.Name })
                .Distinct()
                .ToListAsync(ct);

            options.AddRange(groups.Select(g => new BorrowOptionRecord(g.Id, $"For {g.Name}")));
        }

        // Borrowing personally. Either flag allows it; they differ only in who qualifies.
        var personalAllowed =
            (item.LoanAudience & EquipmentLoanAudience.IndividualUsers) != 0
            || ((item.LoanAudience & EquipmentLoanAudience.GroupMembers) != 0
                && await SharesAnActiveGroupWithOwnerAsync(db, item.OwnerAppUserId, userId, ct));

        if (personalAllowed)
            options.Add(new BorrowOptionRecord(null, "For myself"));

        if (options.Count == 0)
        {
            var reason = (item.LoanAudience & EquipmentLoanAudience.SharedGroups) != 0
                ? "You're not in a group this equipment is shared with."
                : "The owner only lends this to people in their groups.";
            return new BorrowEligibilityRecord(item.Id, false, reason, [], isOut, backOn);
        }

        return new BorrowEligibilityRecord(item.Id, true, null, options, isOut, backOn);
    }

    /// <summary>Whether two people are both active members of at least one group in common.</summary>
    private static Task<bool> SharesAnActiveGroupWithOwnerAsync(
        BenDataContext db, Guid? ownerUserId, Guid viewerUserId, CancellationToken ct)
    {
        if (ownerUserId is null) return Task.FromResult(false);

        return db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == viewerUserId && m.IsActive)
            .AnyAsync(m => db.OrganizationUserMemberships.Any(o =>
                o.OrganizationId == m.OrganizationId && o.AppUserId == ownerUserId && o.IsActive), ct);
    }

    /// <summary>Whether this loan is out and past its due date. Computed, never stored.</summary>
    public static bool IsOverdue(EquipmentCheckout checkout, DateTime utcNow)
        => checkout.Status == EquipmentCheckoutStatus.CheckedOut
           && checkout.DateDue is not null
           && checkout.DateDue < utcNow;

    /// <summary>What the viewer may do with this loan, given whether they are its approver.</summary>
    public static EquipmentCheckoutFlags ComputeCheckoutFlags(
        EquipmentCheckout checkout, Guid userId, bool isApprover)
    {
        var isBorrower = userId != Guid.Empty && checkout.BorrowerAppUserId == userId;
        var status     = checkout.Status;

        return new EquipmentCheckoutFlags(
            IsBorrower: isBorrower,
            IsApprover: isApprover,
            // The borrower can pull out any time before they actually have it.
            CanCancel: isBorrower && status is EquipmentCheckoutStatus.Requested or EquipmentCheckoutStatus.Approved,
            CanApprove: isApprover && status == EquipmentCheckoutStatus.Requested,
            CanDeny: isApprover && status == EquipmentCheckoutStatus.Requested,
            // Each party attests to the transfer coming toward them.
            CanConfirmHandoff: isBorrower && status == EquipmentCheckoutStatus.Approved,
            CanReceiveReturn: isApprover && status == EquipmentCheckoutStatus.CheckedOut);
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
