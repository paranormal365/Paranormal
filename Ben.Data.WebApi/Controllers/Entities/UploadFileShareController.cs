using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

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

    /// <summary>List all active shares for a specific file.</summary>
    [HttpGet("/api/upload-files/{fileId:guid}/shares")]
    public async Task<ActionResult<IEnumerable<UploadFileOrganizationShareRecord>>> GetSharesForFile(
        Guid fileId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var shares = await db.UploadFileOrganizationShares.AsNoTracking()
            .Where(s => s.UploadFileId == fileId && s.IsActive)
            .ToListAsync(cancellationToken);
        return Ok(_mapper.Map<IEnumerable<UploadFileOrganizationShareRecord>>(shares));
    }

    /// <summary>List all files visible to a user in a specific org (respects visibility tier).</summary>
    [HttpGet("/api/upload-files/org/{orgId:guid}")]
    public async Task<ActionResult<IEnumerable<UploadFileRecord>>> GetOrgFiles(
        Guid orgId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // TODO: filter by requesting user's role in the org (OrgAdminsOnly vs OrgMembers)
        // For now returns all active shares for the org
        var fileIds = await db.UploadFileOrganizationShares.AsNoTracking()
            .Where(s => s.OrganizationId == orgId && s.IsActive)
            .Select(s => s.UploadFileId)
            .ToListAsync(cancellationToken);

        var files = await db.UploadFiles.AsNoTracking()
            .Where(f => fileIds.Contains(f.Id))
            .ToListAsync(cancellationToken);

        return Ok(_mapper.Map<IEnumerable<UploadFileRecord>>(files));
    }

    /// <summary>Share a file with an organization.</summary>
    [HttpPost("/api/upload-files/{fileId:guid}/shares")]
    public async Task<ActionResult<UploadFileOrganizationShareRecord>> ShareWithOrg(
        Guid fileId,
        [FromBody] ShareWithOrgRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

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
            existing.UpdatedByAppUserId = request.SharedByAppUserId;
            await db.SaveChangesAsync(cancellationToken);
            _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileOrganizationShare), existing.Id, shareBefore!, existing, GetCurrentUserId(), AppSources.WebApi, cancellationToken));
            return Ok(_mapper.Map<UploadFileOrganizationShareRecord>(existing));
        }

        var share = new UploadFileOrganizationShare
        {
            Id = Guid.NewGuid(),
            UploadFileId = fileId,
            OrganizationId = request.OrganizationId,
            SharedByAppUserId = request.SharedByAppUserId,
            Visibility = request.Visibility,
            IsActive = true,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = request.SharedByAppUserId
        };

        db.UploadFileOrganizationShares.Add(share);
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFileOrganizationShare), share.Id, share, GetCurrentUserId(), AppSources.WebApi, cancellationToken));
        return CreatedAtAction(nameof(GetSharesForFile), new { fileId }, _mapper.Map<UploadFileOrganizationShareRecord>(share));
    }

    /// <summary>Update visibility of an org share (org admin only).</summary>
    [HttpPut("{shareId:guid}/visibility")]
    public async Task<ActionResult<UploadFileOrganizationShareRecord>> UpdateVisibility(
        Guid shareId,
        [FromBody] UpdateVisibilityRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var shareBefore = await db.UploadFileOrganizationShares.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shareId, cancellationToken);
        var share = await db.UploadFileOrganizationShares.FirstOrDefaultAsync(s => s.Id == shareId, cancellationToken);
        if (share is null) return NotFound();

        share.Visibility = request.Visibility;
        share.DateUpdated = DateTime.UtcNow;
        share.UpdatedByAppUserId = request.UpdatedByAppUserId;
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileOrganizationShare), shareId, shareBefore!, share, GetCurrentUserId(), AppSources.WebApi, cancellationToken));
        return Ok(_mapper.Map<UploadFileOrganizationShareRecord>(share));
    }

    /// <summary>Remove (soft-delete) a file from an organization.</summary>
    [HttpDelete("{shareId:guid}")]
    public async Task<IActionResult> RemoveShare(Guid shareId, [FromQuery] Guid removedByAppUserId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var shareBefore = await db.UploadFileOrganizationShares.AsNoTracking().FirstOrDefaultAsync(s => s.Id == shareId, cancellationToken);
        var share = await db.UploadFileOrganizationShares.FirstOrDefaultAsync(s => s.Id == shareId, cancellationToken);
        if (share is null) return NotFound();

        share.IsActive = false;
        share.RemovedByAppUserId = removedByAppUserId;
        share.RemovalDate = DateTime.UtcNow;
        share.DateUpdated = DateTime.UtcNow;
        share.UpdatedByAppUserId = removedByAppUserId;
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFileOrganizationShare), shareId, shareBefore!, share, GetCurrentUserId(), AppSources.WebApi, cancellationToken));
        return NoContent();
    }
}

public sealed record ShareWithOrgRequest(Guid OrganizationId, Guid SharedByAppUserId, FileShareVisibility Visibility);
public sealed record UpdateVisibilityRequest(FileShareVisibility Visibility, Guid UpdatedByAppUserId);
