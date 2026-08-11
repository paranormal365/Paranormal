using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Tiered org-only file sharing (Public/OrgMembers/OrgAdminsOnly visibility). Additive alongside
/// the newer, more general 4-target sharing in <see cref="UploadFileShareV2Controller"/>
/// (person/investigation-team/org/public) — this one predates it and is still the live mechanism
/// behind the org-CMS asset picker and the personal Media Library's "share with an org" dialog,
/// so both coexist rather than one replacing the other.
/// </summary>
/// <remarks>
/// Every action here previously trusted route/body IDs with no ownership or membership check at
/// all — any authenticated user could share someone else's private file into any org, retarget
/// any share's visibility, or delete any org's shares, and <see cref="ShareWithOrgRequest"/> even
/// let the caller spoof who the audit trail says did it. The fix mirrors
/// <see cref="UploadFileShareV2Controller"/>'s already-correct pattern: mutations require the
/// caller to actually own the file (or be SuperAdmin); the org-scoped read/manage actions require
/// active org membership, honouring <see cref="FileShareVisibility"/> the same way
/// <see cref="FileAudienceAccess"/> does; and every actor id is taken from
/// <see cref="BenControllerBase.GetCurrentUserIdOrThrow"/>, never the request body.
/// </remarks>
[ApiController]
[Route("api/upload-file-shares")]
[Authorize]
public sealed class UploadFileShareController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    public UploadFileShareController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _auditLog = auditLog;
    }

    /// <summary>List all active shares for a specific file. File owner or SuperAdmin only —
    /// this is the file's sharing configuration, not something every viewer needs to see.</summary>
    [HttpGet("/api/upload-files/{fileId:guid}/shares")]
    public async Task<ActionResult<IEnumerable<UploadFileOrganizationShareRecord>>> GetSharesForFile(
        Guid fileId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null) return NotFound();
        if (file.AppUserId != userId && !User.IsInRole(RoleNames.SuperAdmin)) return Forbid();

        var shares = await db.UploadFileOrganizationShares.AsNoTracking()
            .Where(s => s.UploadFileId == fileId && s.IsActive)
            .ToListAsync(cancellationToken);
        return Ok(_mapper.Map<IEnumerable<UploadFileOrganizationShareRecord>>(shares));
    }

    /// <summary>List files shared with an org, filtered to what the caller may actually see there:
    /// <see cref="FileShareVisibility.Public"/>/<see cref="FileShareVisibility.OrgMembers"/> shares
    /// for any active member, <see cref="FileShareVisibility.OrgAdminsOnly"/> only for admin-tier
    /// members — the same tiering <see cref="FileAudienceAccess"/> applies elsewhere.</summary>
    [HttpGet("/api/upload-files/org/{orgId:guid}")]
    public async Task<ActionResult<IEnumerable<UploadFileRecord>>> GetOrgFiles(
        Guid orgId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var membership = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive)
            .Select(m => (OrganizationMemberRole?)m.Role)
            .FirstOrDefaultAsync(cancellationToken);
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        if (membership is null && !isSuperAdmin) return Forbid();

        var isAdminTier = isSuperAdmin || membership <= OrganizationMemberRole.Administrator;

        var shareQuery = db.UploadFileOrganizationShares.AsNoTracking()
            .Where(s => s.OrganizationId == orgId && s.IsActive);
        if (!isAdminTier)
            shareQuery = shareQuery.Where(s =>
                s.Visibility == FileShareVisibility.Public || s.Visibility == FileShareVisibility.OrgMembers);

        var fileIds = await shareQuery.Select(s => s.UploadFileId).ToListAsync(cancellationToken);

        var files = await db.UploadFiles.AsNoTracking()
            .Where(f => fileIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        return Ok(_mapper.Map<IEnumerable<UploadFileRecord>>(files));
    }

    /// <summary>Share a file with an organization. File owner or SuperAdmin only — sharing is a
    /// decision for whoever owns the content, not whoever happens to know its id and an org id.</summary>
    [HttpPost("/api/upload-files/{fileId:guid}/shares")]
    public async Task<ActionResult<UploadFileOrganizationShareRecord>> ShareWithOrg(
        Guid fileId,
        [FromBody] ShareWithOrgRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null) return NotFound();
        if (file.AppUserId != userId && !User.IsInRole(RoleNames.SuperAdmin)) return Forbid();

        // Check if already shared (reactivate if soft-deleted)
        var shareBefore = await db.UploadFileOrganizationShares.AsNoTracking()
            .FirstOrDefaultAsync(s => s.UploadFileId == fileId && s.OrganizationId == request.OrganizationId, cancellationToken);

        var existing = await db.UploadFileOrganizationShares
            .FirstOrDefaultAsync(s => s.UploadFileId == fileId && s.OrganizationId == request.OrganizationId, cancellationToken);

        if (existing is not null)
        {
            existing.IsActive = true;
            existing.Visibility = request.Visibility;
            existing.RemovedByAppUserId = null;
            existing.RemovalDate = null;
            existing.DateUpdated = DateTime.UtcNow;
            existing.UpdatedByAppUserId = userId;
            await db.SaveChangesAsync(cancellationToken);
            _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileOrganizationShare), existing.Id, shareBefore!, existing, userId, AppSources.WebApi, cancellationToken));
            return Ok(_mapper.Map<UploadFileOrganizationShareRecord>(existing));
        }

        var share = new UploadFileOrganizationShare
        {
            Id = Guid.NewGuid(),
            UploadFileId = fileId,
            OrganizationId = request.OrganizationId,
            SharedByAppUserId = userId,
            Visibility = request.Visibility,
            IsActive = true,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId
        };

        db.UploadFileOrganizationShares.Add(share);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Lost the race against a concurrent share of the same file into the same org — this
            // is effectively an upsert, so reactivate/update the row that won instead of erroring.
            db.Entry(share).State = EntityState.Detached;
            var winner = await db.UploadFileOrganizationShares
                .FirstAsync(s => s.UploadFileId == fileId && s.OrganizationId == request.OrganizationId, cancellationToken);
            winner.IsActive           = true;
            winner.Visibility         = request.Visibility;
            winner.RemovedByAppUserId = null;
            winner.RemovalDate        = null;
            winner.DateUpdated        = DateTime.UtcNow;
            winner.UpdatedByAppUserId = userId;
            await db.SaveChangesAsync(cancellationToken);
            _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileOrganizationShare), winner.Id, winner, winner, userId, AppSources.WebApi, cancellationToken));
            return Ok(_mapper.Map<UploadFileOrganizationShareRecord>(winner));
        }
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFileOrganizationShare), share.Id, share, userId, AppSources.WebApi, cancellationToken));
        return CreatedAtAction(nameof(GetSharesForFile), new { fileId }, _mapper.Map<UploadFileOrganizationShareRecord>(share));
    }

    /// <summary>Update visibility of an org share. Admin-tier member of the share's org, or
    /// SuperAdmin — once a file is shared into an org, how visible it is *within* that org is the
    /// org's own internal policy call, not the original file owner's from outside the org.</summary>
    [HttpPut("{shareId:guid}/visibility")]
    public async Task<ActionResult<UploadFileOrganizationShareRecord>> UpdateVisibility(
        Guid shareId,
        [FromBody] UpdateVisibilityRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var shareBefore = await db.UploadFileOrganizationShares.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shareId, cancellationToken);
        var share = await db.UploadFileOrganizationShares.FirstOrDefaultAsync(s => s.Id == shareId, cancellationToken);
        if (share is null) return NotFound();

        if (!await IsAdminTierMemberAsync(db, share.OrganizationId, userId, cancellationToken))
            return Forbid();

        share.Visibility = request.Visibility;
        share.DateUpdated = DateTime.UtcNow;
        share.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileOrganizationShare), shareId, shareBefore!, share, userId, AppSources.WebApi, cancellationToken));
        return Ok(_mapper.Map<UploadFileOrganizationShareRecord>(share));
    }

    /// <summary>Remove (soft-delete) a file from an organization. The file's owner (revoking
    /// access they granted) or an admin-tier member of that org (the org dropping it), or
    /// SuperAdmin.</summary>
    [HttpDelete("{shareId:guid}")]
    public async Task<IActionResult> RemoveShare(Guid shareId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var shareBefore = await db.UploadFileOrganizationShares.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shareId, cancellationToken);
        var share = await db.UploadFileOrganizationShares.Include(s => s.UploadFile)
            .FirstOrDefaultAsync(s => s.Id == shareId, cancellationToken);
        if (share is null) return NotFound();

        var isFileOwner = share.UploadFile.AppUserId == userId;
        if (!isFileOwner && !await IsAdminTierMemberAsync(db, share.OrganizationId, userId, cancellationToken))
            return Forbid();

        share.IsActive = false;
        share.RemovedByAppUserId = userId;
        share.RemovalDate = DateTime.UtcNow;
        share.DateUpdated = DateTime.UtcNow;
        share.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileOrganizationShare), shareId, shareBefore!, share, userId, AppSources.WebApi, cancellationToken));
        return NoContent();
    }

    private async Task<bool> IsAdminTierMemberAsync(BenDataContext db, Guid organizationId, Guid userId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == organizationId && m.AppUserId == userId && m.IsActive
                        && m.Role <= OrganizationMemberRole.Administrator, ct);
    }
}

public sealed record ShareWithOrgRequest(Guid OrganizationId, FileShareVisibility Visibility);
public sealed record UpdateVisibilityRequest(FileShareVisibility Visibility);
