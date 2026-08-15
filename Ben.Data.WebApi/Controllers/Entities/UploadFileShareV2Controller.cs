using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Generalized 4-target sharing (person / investigation team / organization / public) for the
/// universal media library, additive alongside the existing tiered org-only
/// <see cref="UploadFileShareController"/> — see that class's doc comment for why they coexist.
/// </summary>
[ApiController]
[Authorize]
public sealed class UploadFileShareV2Controller : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IAuditLogService _auditLog;

    public UploadFileShareV2Controller(IDbContextFactory<BenDataContext> db, IAuditLogService auditLog)
    {
        _db = db;
        _auditLog = auditLog;
    }

    [HttpGet("api/upload-files/{fileId:guid}/shares-v2")]
    public async Task<ActionResult<IEnumerable<UploadFileShareRecord>>> GetShares(Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return NotFound();
        if (file.AppUserId != userId && !User.IsInRole(RoleNames.SuperAdmin)) return Forbid();

        var shares = await db.UploadFileShares.AsNoTracking()
            .Where(s => s.UploadFileId == fileId && s.IsActive)
            .OrderByDescending(s => s.DateCreated)
            .ToListAsync(ct);
        return Ok(shares.Select(ToRecord));
    }

    [HttpPost("api/upload-files/{fileId:guid}/shares-v2")]
    public async Task<ActionResult<UploadFileShareRecord>> CreateShare(
        Guid fileId, [FromBody] CreateShareRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var file = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return NotFound();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        if (file.AppUserId != userId && !isSuperAdmin) return Forbid();

        switch (request.TargetType)
        {
            case ShareTargetType.Person:
                if (request.TargetAppUserId is null || request.TargetInvestigationId is not null || request.TargetOrganizationId is not null)
                    return BadRequest("Person shares must set only TargetAppUserId.");
                if (!await db.AppUsers.AnyAsync(u => u.Id == request.TargetAppUserId, ct))
                    return BadRequest("Target user not found.");
                break;
            case ShareTargetType.InvestigationTeam:
                if (request.TargetInvestigationId is null || request.TargetAppUserId is not null || request.TargetOrganizationId is not null)
                    return BadRequest("Investigation-team shares must set only TargetInvestigationId.");
                if (!isSuperAdmin && !await IsOrgMemberOfInvestigationAsync(db, request.TargetInvestigationId.Value, userId, ct))
                    return Forbid();
                break;
            case ShareTargetType.Organization:
                if (request.TargetOrganizationId is null || request.TargetAppUserId is not null || request.TargetInvestigationId is not null)
                    return BadRequest("Organization shares must set only TargetOrganizationId.");
                if (!await db.Organizations.AnyAsync(o => o.Id == request.TargetOrganizationId, ct))
                    return BadRequest("Target organization not found.");
                break;
            case ShareTargetType.Public:
                if (request.TargetAppUserId is not null || request.TargetInvestigationId is not null || request.TargetOrganizationId is not null)
                    return BadRequest("Public shares must not set a target field.");
                break;
            default:
                return BadRequest("Unknown TargetType.");
        }

        var share = new Ben.Data.Source.Entities.UploadFileShare
        {
            Id = Guid.NewGuid(),
            UploadFileId = fileId,
            TargetType = request.TargetType,
            TargetAppUserId = request.TargetAppUserId,
            TargetInvestigationId = request.TargetInvestigationId,
            TargetOrganizationId = request.TargetOrganizationId,
            SharedByAppUserId = userId,
            IsActive = true,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.UploadFileShares.Add(share);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(Ben.Data.Source.Entities.UploadFileShare), share.Id, share, userId, AppSources.WebApi, ct));

        return CreatedAtAction(nameof(GetShares), new { fileId }, ToRecord(share));
    }

    [HttpDelete("api/upload-file-shares-v2/{shareId:guid}")]
    public async Task<IActionResult> RemoveShare(Guid shareId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var share = await db.UploadFileShares.Include(s => s.UploadFile)
            .FirstOrDefaultAsync(s => s.Id == shareId, ct);
        if (share is null) return NotFound();
        if (share.UploadFile.AppUserId != userId && !User.IsInRole(RoleNames.SuperAdmin)) return Forbid();

        share.IsActive = false;
        share.RemovedByAppUserId = userId;
        share.RemovalDate = DateTime.UtcNow;
        share.DateUpdated = DateTime.UtcNow;
        share.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(Ben.Data.Source.Entities.UploadFileShare), share.Id, share, userId, AppSources.WebApi, ct));
        return NoContent();
    }

    private static async Task<bool> IsOrgMemberOfInvestigationAsync(BenDataContext db, Guid investigationId, Guid userId, CancellationToken ct)
    {
        // Read straight off the investigation. Going through the case returned Guid.Empty for a
        // case-less visit, which this method reads as "deny everybody" — a permission check that
        // fails closed is still a permission check that is wrong, and nobody would see why.
        var orgId = await db.Investigations.AsNoTracking()
            .Where(i => i.Id == investigationId)
            .Select(i => i.OrganizationId)
            .FirstOrDefaultAsync(ct);
        if (orgId == Guid.Empty) return false;
        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);
    }

    private static UploadFileShareRecord ToRecord(Ben.Data.Source.Entities.UploadFileShare s) => new()
    {
        Id = s.Id,
        UploadFileId = s.UploadFileId,
        TargetType = s.TargetType,
        TargetAppUserId = s.TargetAppUserId,
        TargetInvestigationId = s.TargetInvestigationId,
        TargetOrganizationId = s.TargetOrganizationId,
        SharedByAppUserId = s.SharedByAppUserId,
        DateCreated = s.DateCreated,
    };
}
