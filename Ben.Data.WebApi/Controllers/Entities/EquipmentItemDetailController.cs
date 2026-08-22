using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// One piece of equipment, served to whoever is entitled to see it.
/// </summary>
/// <remarks>
/// <para>Personal gear had no detail page at all before this, and group gear had one reachable only
/// under an organization path — so the make/model page had nowhere to send a viewer for two thirds
/// of what it lists. One endpoint answers for all of them.</para>
///
/// <para>The audience is resolved once, in order, and decides which optional sub-records the
/// payload carries. Nothing is nulled out field by field: a viewer who may not see the serial gets
/// a response with no management section at all.</para>
/// </remarks>
[ApiController]
[Route("api/equipment/items")]
public sealed class EquipmentItemDetailController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IOrganizationSecurityService _security;

    public EquipmentItemDetailController(IDbContextFactory<BenDataContext> db, IOrganizationSecurityService security)
    {
        _db       = db;
        _security = security;
    }

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<EquipmentItemDetailRecord>> GetItem(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        // [AllowAnonymous] endpoint, so the claim check alone misses an Entra session and shows
        // an admin the visitor's view of the item. Item 140.
        var isSuperAdmin = await CallerIsSuperAdminAsync();

        await using var db = await _db.CreateDbContextAsync(ct);
        var item = await db.EquipmentItems.AsNoTracking()
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
            .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentCategory)
            .Include(i => i.Photos)
            .FirstOrDefaultAsync(i => i.Id == id, ct);
        if (item is null) return NotFound();

        var audience = await EquipmentAccess.ResolveItemAudienceAsync(db, _security, item, userId, isSuperAdmin, ct);
        if (audience == EquipmentAccess.ItemAudience.None) return NotFound();

        // Custodians — the people responsible for the thing, as opposed to people who may look at it.
        var isCustodian = EquipmentAccess.IsCustodian(audience);
        var knowsOwner  = audience is not EquipmentAccess.ItemAudience.Public;
        var canSeeCounters = audience == EquipmentAccess.ItemAudience.SuperAdmin
            || (item.OwningOrganizationId is Guid cOrgId && await IsOrgAdministratorAsync(db, cOrgId, userId, ct));

        var names = await NamesAsync(db, [item.OwnerAppUserId, item.CurrentHolderAppUserId], ct);
        var orgName = item.OwningOrganizationId is Guid oid
            ? await db.Organizations.AsNoTracking().Where(o => o.Id == oid).Select(o => o.Name).FirstOrDefaultAsync(ct)
            : null;

        return Ok(new EquipmentItemDetailRecord(
            item.Id,
            item.EquipmentModelId,
            item.EquipmentModel.Name,
            item.EquipmentModel.EquipmentBrand.Name,
            item.EquipmentModel.EquipmentCategory.Name,
            item.DisplayName,
            item.Notes,
            item.AcquisitionDate,
            item.IsRetired,
            item.LoanAudience,
            item.WebsiteUrl,
            [.. item.Photos.OrderBy(p => p.SortOrder)
                .Select(p => new EquipmentItemPhotoRecord(
                    p.Id, p.EquipmentItemId, p.UploadFileId, p.IsPrimary, p.Caption, p.SortOrder, p.ExcludeFromCatalog))],
            knowsOwner
                ? new EquipmentItemOwnershipRecord(
                    item.OwnerAppUserId,
                    item.OwnerAppUserId is Guid ownerId && names.TryGetValue(ownerId, out var on) ? on : null,
                    item.OwningOrganizationId,
                    orgName)
                : null,
            isCustodian
                ? new EquipmentItemManagementRecord(
                    item.SerialNumber,
                    item.CurrentHolderAppUserId,
                    item.CurrentHolderAppUserId is Guid hid && names.TryGetValue(hid, out var hn) ? hn : null,
                    item.LastServicedDate,
                    item.DefectNotes)
                : null,
            canSeeCounters ? new EquipmentItemCountersRecord(item.ViewCount, item.LinkClickCount) : null,
            new EquipmentItemDetailFlags(
                IsOwner: audience == EquipmentAccess.ItemAudience.Owner,
                CanEdit: isCustodian,
                CanRetire: isCustodian,
                CanManagePhotos: isCustodian,
                CanSeeCounters: canSeeCounters)));
    }

    /// <summary>
    /// Whether this caller administers the owning group.
    /// </summary>
    /// <remarks>
    /// The membership role, not the Equipment permission — the audience for interest numbers is
    /// administrators, and a group may hand its equipment role to someone who is neither.
    /// </remarks>
    private static Task<bool> IsOrgAdministratorAsync(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => userId == Guid.Empty
            ? Task.FromResult(false)
            : db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive
                            && (m.Role == OrganizationMemberRole.Owner
                                || m.Role == OrganizationMemberRole.Administrator), ct);

    private static async Task<Dictionary<Guid, string?>> NamesAsync(
        BenDataContext db, IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var wanted = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        if (wanted.Count == 0) return [];
        return await db.AppUsers.AsNoTracking()
            .Where(u => wanted.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }
}
