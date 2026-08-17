using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Equipment as a group sees it. Phase 2 covers members' shared personal gear; Phase 3 adds the
/// group's own equipment alongside it, gated on the new Equipment permission.
/// </summary>
/// <remarks>
/// Reading shared gear needs plain active membership, not a permission bit. Sharing <i>is</i> the
/// owner's consent — the group's Equipment permission governs the group's own property, not what
/// its members have chosen to show it.
/// </remarks>
[ApiController]
[Route("api/organizations/{orgId:guid}/equipment")]
[Authorize]
public sealed class OrganizationEquipmentController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public OrganizationEquipmentController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>
    /// Personal gear members have shared with this group.
    /// </summary>
    /// <remarks>
    /// Membership is re-checked here rather than trusted from the share row, so a share left behind
    /// by someone who has since left the group grants nothing. Answers 404 for a non-member: whether
    /// a group exists is not something this endpoint should confirm to outsiders.
    /// </remarks>
    [HttpGet("shared")]
    public async Task<ActionResult<IEnumerable<SharedEquipmentItemRecord>>> GetSharedWithOrg(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var isSuperAdmin = User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);
        var isMember = await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);
        if (!isMember && !isSuperAdmin) return NotFound();

        // A share only counts while its owner is still an active member of this group.
        var items = await db.EquipmentItemShares.AsNoTracking()
            .Where(s => s.OrganizationId == orgId)
            .Join(db.EquipmentItems.AsNoTracking()
                    .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentBrand)
                    .Include(i => i.EquipmentModel).ThenInclude(m => m.EquipmentCategory)
                    .Include(i => i.Photos),
                  s => s.EquipmentItemId, i => i.Id, (s, i) => i)
            .Where(i => !i.IsRetired
                     && i.OwnerAppUserId != null
                     && db.OrganizationUserMemberships.Any(m =>
                            m.OrganizationId == orgId && m.AppUserId == i.OwnerAppUserId && m.IsActive))
            .Select(i => new SharedEquipmentItemRecord(
                i.Id,
                i.OwnerAppUserId!.Value,
                db.AppUsers.Where(u => u.Id == i.OwnerAppUserId).Select(u => u.DisplayName).FirstOrDefault(),
                i.DisplayName,
                i.EquipmentModel.EquipmentBrand.Name,
                i.EquipmentModel.Name,
                i.EquipmentModel.EquipmentCategory.Name,
                i.Notes,
                i.LoanAudience,
                i.IsRetired,
                i.Photos.OrderBy(p => p.SortOrder)
                    .Select(p => new EquipmentItemPhotoRecord(p.Id, p.EquipmentItemId, p.UploadFileId, p.IsPrimary, p.Caption, p.SortOrder))
                    .ToList()))
            .ToListAsync(ct);

        return Ok(items.OrderBy(i => i.OwnerDisplayName).ThenBy(i => i.DisplayName));
    }
}
