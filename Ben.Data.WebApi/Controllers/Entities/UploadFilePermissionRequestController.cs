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
/// Previously had no authorization beyond <c>[Authorize]</c> on any action: <c>Review</c> let any
/// authenticated user approve their own (or anyone's) access to any file, since
/// <c>ReviewedByAppUserId</c> was read from the request body rather than the caller's identity,
/// and neither it nor <c>GetForFile</c> checked that the caller actually owned the file or
/// administered the relevant org. <c>Submit</c> similarly let the caller spoof
/// <c>RequestedByAppUserId</c>. Only <c>Cancel</c> checked correctly — every other action below
/// now follows its pattern.
/// </summary>
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

    /// <summary>Get all permission requests for a file. Visible to the file owner (sees every
    /// request) and SuperAdmin; an org admin sees only the requests scoped to an org they
    /// administer (a request's <c>OrganizationId</c> is nullable — person-to-person requests with
    /// no org are owner/SuperAdmin-only).</summary>
    [HttpGet("/api/upload-files/{fileId:guid}/permission-requests")]
    public async Task<ActionResult<IEnumerable<UploadFilePermissionRequestRecord>>> GetForFile(
        Guid fileId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, cancellationToken);
        if (file is null) return NotFound();

        var requests = await db.UploadFilePermissionRequests.AsNoTracking()
            .Where(r => r.UploadFileId == fileId)
            .OrderByDescending(r => r.DateCreated)
            .ToListAsync(cancellationToken);

        var isFileOwnerOrSuperAdmin = file.AppUserId == userId || User.IsInRole(RoleNames.SuperAdmin);
        if (!isFileOwnerOrSuperAdmin)
        {
            var adminOrgIds = await db.OrganizationUserMemberships.AsNoTracking()
                .Where(m => m.AppUserId == userId && m.IsActive && m.Role <= OrganizationMemberRole.Administrator)
                .Select(m => m.OrganizationId)
                .ToListAsync(cancellationToken);
            requests = requests.Where(r => r.OrganizationId.HasValue && adminOrgIds.Contains(r.OrganizationId.Value)).ToList();
        }

        return Ok(_mapper.Map<IEnumerable<UploadFilePermissionRequestRecord>>(requests));
    }

    /// <summary>Get pending requests that require action from this user (as file owner or org admin).</summary>
    [HttpGet("pending-for/{reviewerAppUserId:guid}")]
    public async Task<ActionResult<IEnumerable<UploadFilePermissionRequestRecord>>> GetPendingForReviewer(
        Guid reviewerAppUserId, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        if (reviewerAppUserId != userId && !User.IsInRole(RoleNames.SuperAdmin)) return Forbid();

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

    /// <summary>
    /// Pending requests on the caller's own files, with the file, requester and org names joined in.
    /// </summary>
    /// <remarks>
    /// The reviewer-id-in-the-route form above predates this and is still used by the raw
    /// per-file view. This one is always "me" — a reviewer list has no business accepting someone
    /// else's id — and returns names, because a list of ids is not something a person can act on.
    /// </remarks>
    [HttpGet("/api/me/permission-requests/pending")]
    public async Task<ActionResult<IEnumerable<PendingPermissionRequestRecord>>> GetPendingForMe(
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var items = await db.UploadFilePermissionRequests.AsNoTracking()
            .Where(r => r.RequestStatus == FilePermissionRequestStatus.Pending
                     && db.UploadFiles.Any(f => f.Id == r.UploadFileId && f.AppUserId == userId))
            .OrderByDescending(r => r.DateCreated)
            .Select(r => new PendingPermissionRequestRecord(
                r.Id,
                r.UploadFileId,
                db.UploadFiles.Where(f => f.Id == r.UploadFileId).Select(f => f.FileName).FirstOrDefault(),
                r.OrganizationId,
                r.OrganizationId == null
                    ? null
                    : db.Organizations.Where(o => o.Id == r.OrganizationId).Select(o => o.Name).FirstOrDefault(),
                r.RequestedByAppUserId,
                db.AppUsers.Where(u => u.Id == r.RequestedByAppUserId)
                           .Select(u => u.DisplayName ?? u.Email).FirstOrDefault(),
                r.PermissionType,
                r.RequestNotes,
                r.DateCreated))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    /// <summary>Submit a permission request for a file.</summary>
    [HttpPost("/api/upload-files/{fileId:guid}/permission-requests")]
    public async Task<ActionResult<UploadFilePermissionRequestRecord>> Submit(
        Guid fileId,
        [FromBody] SubmitRequestBody body,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var request = new UploadFilePermissionRequest
        {
            Id = Guid.NewGuid(),
            UploadFileId = fileId,
            OrganizationId = body.OrganizationId,
            RequestedByAppUserId = userId,
            PermissionType = body.PermissionType,
            RequestStatus = FilePermissionRequestStatus.Pending,
            RequestNotes = body.RequestNotes,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId
        };

        db.UploadFilePermissionRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFilePermissionRequest), request.Id, request, userId, AppSources.WebApi));
        return CreatedAtAction(nameof(GetForFile), new { fileId }, _mapper.Map<UploadFilePermissionRequestRecord>(request));
    }

    /// <summary>Approve or deny a permission request. File owner, SuperAdmin, or (when the request
    /// targets an org) an admin-tier member of that org.</summary>
    [HttpPut("{requestId:guid}/review")]
    public async Task<ActionResult<UploadFilePermissionRequestRecord>> Review(
        Guid requestId,
        [FromBody] ReviewRequestBody body,
        CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var before = await db.UploadFilePermissionRequests.AsNoTracking().FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        var request = await db.UploadFilePermissionRequests.Include(r => r.UploadFile)
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (request is null) return NotFound();

        var isFileOwnerOrSuperAdmin = request.UploadFile.AppUserId == userId || User.IsInRole(RoleNames.SuperAdmin);
        if (!isFileOwnerOrSuperAdmin)
        {
            var isOrgAdmin = request.OrganizationId.HasValue
                && await FileAudienceAccess.IsOrgAdminAsync(db, request.OrganizationId.Value, userId, cancellationToken);
            if (!isOrgAdmin) return Forbid();
        }

        request.RequestStatus = body.RequestStatus;
        request.ReviewNotes = body.ReviewNotes;
        request.ReviewedByAppUserId = userId;
        request.DateReviewed = DateTime.UtcNow;
        request.DateUpdated = DateTime.UtcNow;
        request.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFilePermissionRequest), requestId, before!, request, userId, AppSources.WebApi));
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
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFilePermissionRequest), requestId, before!, request, GetCurrentUserId(), AppSources.WebApi));
        return Ok(_mapper.Map<UploadFilePermissionRequestRecord>(request));
    }
}

public sealed record SubmitRequestBody(
    Guid? OrganizationId,
    FilePermissionType PermissionType,
    string? RequestNotes);

public sealed record ReviewRequestBody(
    FilePermissionRequestStatus RequestStatus,
    string? ReviewNotes);
