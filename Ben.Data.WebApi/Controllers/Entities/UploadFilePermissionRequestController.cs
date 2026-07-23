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
[Route("api/upload-file-permission-requests")]
[Authorize]
public sealed class UploadFilePermissionRequestController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    public UploadFilePermissionRequestController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _auditLog = auditLog;
    }

    /// <summary>Get all permission requests for a file (visible to file owner and org admins).</summary>
    [HttpGet("/api/upload-files/{fileId:guid}/permission-requests")]
    public async Task<ActionResult<IEnumerable<UploadFilePermissionRequestRecord>>> GetForFile(
        Guid fileId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var requests = await db.UploadFilePermissionRequests.AsNoTracking()
            .Where(r => r.UploadFileId == fileId)
            .OrderByDescending(r => r.DateCreated)
            .ToListAsync(cancellationToken);
        return Ok(_mapper.Map<IEnumerable<UploadFilePermissionRequestRecord>>(requests));
    }

    /// <summary>Get pending requests that require action from this user (as file owner or org admin).</summary>
    [HttpGet("pending-for/{reviewerAppUserId:guid}")]
    public async Task<ActionResult<IEnumerable<UploadFilePermissionRequestRecord>>> GetPendingForReviewer(
        Guid reviewerAppUserId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Requests on files owned by this user
        var ownedFileIds = await db.UploadFiles.AsNoTracking()
            .Where(f => f.AppUserId == reviewerAppUserId)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        var requests = await db.UploadFilePermissionRequests.AsNoTracking()
            .Where(r => r.RequestStatus == FilePermissionRequestStatus.Pending
                        && ownedFileIds.Contains(r.UploadFileId))
            .OrderByDescending(r => r.DateCreated)
            .ToListAsync(cancellationToken);

        return Ok(_mapper.Map<IEnumerable<UploadFilePermissionRequestRecord>>(requests));
    }

    /// <summary>Submit a permission request for a file.</summary>
    [HttpPost("/api/upload-files/{fileId:guid}/permission-requests")]
    public async Task<ActionResult<UploadFilePermissionRequestRecord>> Submit(
        Guid fileId,
        [FromBody] SubmitRequestBody body,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = new UploadFilePermissionRequest
        {
            Id = Guid.NewGuid(),
            UploadFileId = fileId,
            OrganizationId = body.OrganizationId,
            RequestedByAppUserId = body.RequestedByAppUserId,
            PermissionType = body.PermissionType,
            RequestStatus = FilePermissionRequestStatus.Pending,
            RequestNotes = body.RequestNotes,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = body.RequestedByAppUserId
        };

        db.UploadFilePermissionRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFilePermissionRequest), request.Id, request, request.RequestedByAppUserId, AppSources.WebApi, cancellationToken));
        return CreatedAtAction(nameof(GetForFile), new { fileId }, _mapper.Map<UploadFilePermissionRequestRecord>(request));
    }

    /// <summary>Approve or deny a permission request (file owner or org admin).</summary>
    [HttpPut("{requestId:guid}/review")]
    public async Task<ActionResult<UploadFilePermissionRequestRecord>> Review(
        Guid requestId,
        [FromBody] ReviewRequestBody body,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var before = await db.UploadFilePermissionRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        var request = await db.UploadFilePermissionRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null) return NotFound();

        request.RequestStatus = body.RequestStatus;
        request.ReviewNotes = body.ReviewNotes;
        request.ReviewedByAppUserId = body.ReviewedByAppUserId;
        request.DateReviewed = DateTime.UtcNow;
        request.DateUpdated = DateTime.UtcNow;
        request.UpdatedByAppUserId = body.ReviewedByAppUserId;
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFilePermissionRequest), requestId, before!, request, GetCurrentUserId(), AppSources.WebApi, cancellationToken));
        return Ok(_mapper.Map<UploadFilePermissionRequestRecord>(request));
    }

    /// <summary>Cancel a permission request (requester only).</summary>
    [HttpPut("{requestId:guid}/cancel")]
    public async Task<ActionResult<UploadFilePermissionRequestRecord>> Cancel(
        Guid requestId, [FromQuery] Guid cancelledByAppUserId, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var before = await db.UploadFilePermissionRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        var request = await db.UploadFilePermissionRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null) return NotFound();
        if (request.RequestedByAppUserId != cancelledByAppUserId
            && !User.IsInRole(RoleNames.SuperAdmin)) return Forbid();

        request.RequestStatus = FilePermissionRequestStatus.Cancelled;
        request.DateUpdated = DateTime.UtcNow;
        request.UpdatedByAppUserId = cancelledByAppUserId;
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFilePermissionRequest), requestId, before!, request, GetCurrentUserId(), AppSources.WebApi, cancellationToken));
        return Ok(_mapper.Map<UploadFilePermissionRequestRecord>(request));
    }
}

public sealed record SubmitRequestBody(
    Guid? OrganizationId,
    Guid RequestedByAppUserId,
    FilePermissionType PermissionType,
    string? RequestNotes);

public sealed record ReviewRequestBody(
    FilePermissionRequestStatus RequestStatus,
    string? ReviewNotes,
    Guid ReviewedByAppUserId);
