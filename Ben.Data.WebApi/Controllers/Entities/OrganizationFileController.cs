using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

[Route("api/organizations/{orgId:guid}/files")]
[Authorize]
public sealed class OrganizationFileController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IMapper _mapper;
    private readonly IOrganizationSecurityService _security;
    private readonly IFileStorageService _storage;
    private readonly IAuditLogService _auditLog;

    public OrganizationFileController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        IFileStorageService storage,
        IAuditLogService auditLog)
    {
        _dbFactory = dbFactory;
        _mapper    = mapper;
        _security  = security;
        _storage   = storage;
        _auditLog  = auditLog;
    }

    private Guid? CurrentUserId()
    {
        var appUserIdClaim = User.FindFirst("app_user_id")?.Value;
        if (appUserIdClaim is not null && Guid.TryParse(appUserIdClaim, out var id1)) return id1;
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return sub is not null && Guid.TryParse(sub, out var id2) ? id2 : null;
    }

    private static IQueryable<OrganizationFile> WithIncludes(IQueryable<OrganizationFile> q) =>
        q.Include(f => f.UploadFileType)
         .Include(f => f.CreatedByAppUser)
         .Include(f => f.PublishedByAppUser);

    // GET /api/organizations/{orgId}/files
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationFileRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Reading the list is open to any active member of the group; the writes below stay
        // permission-gated.
        //
        // It used to require OrganizationFiles/Read through the security service, which returns
        // false for a plain Member on every table — so the Files tab, which the hub shows to every
        // member, was refused for everyone below Administrator. And because the website's API
        // client turns a non-2xx into an empty list, the refusal rendered as "No records
        // available": a member with a group handbook sitting on the server was told their group
        // had no files at all. Item 109; the same shape as the phase 5 messaging faults, and
        // invisible from every seat the test suite used to sign in from.
        if (!isSuperAdmin)
        {
            var isActiveMember = await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId.Value && m.IsActive, ct);

            if (!isActiveMember)
            {
                var ok = await _security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Read, ct);
                if (!ok) return Forbid();
            }
        }

        var files = await WithIncludes(db.OrganizationFiles)
            .Where(f => f.OrganizationId == orgId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.FileName)
            .AsNoTracking().ToListAsync(ct);
        return Ok(_mapper.Map<List<OrganizationFileRecord>>(files));
    }

    // GET /api/organizations/{orgId}/files/delete-log
    [HttpGet("delete-log")]
    public async Task<ActionResult<IEnumerable<OrganizationFileDeleteLogRecord>>> GetDeleteLog(
        Guid orgId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        if (!isSuperAdmin)
        {
            var ok = await _security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Delete, ct);
            if (!ok) return Forbid();
        }
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var logs = await db.OrganizationFileDeleteLogs
            .Where(l => l.OrganizationId == orgId)
            .OrderByDescending(l => l.DateDeleted)
            .AsNoTracking().ToListAsync(ct);
        return Ok(_mapper.Map<List<OrganizationFileDeleteLogRecord>>(logs));
    }

    // GET /api/organizations/{orgId}/files/{id}/download
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid orgId, Guid id, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        if (!isSuperAdmin)
        {
            var ok = await _security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Read, ct);
            if (!ok) return Forbid();
        }
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var file = await db.OrganizationFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == orgId, ct);
        if (file is null) return NotFound();
        Stream? stream = null;
        if (!string.IsNullOrEmpty(file.StoragePath) && _storage.Exists(file.StoragePath))
            stream = await _storage.OpenReadAsync(file.StoragePath, ct);
        else if (file.FileData is { Length: > 0 })
            stream = new MemoryStream(file.FileData);
        if (stream is null) return NotFound("File data not found in storage.");
        return File(stream, file.ContentType, file.FileName);
    }

    // POST /api/organizations/{orgId}/files  (direct upload)
    // The uploader may set IsPublic; publish audit is recorded if IsPublic=true.
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<OrganizationFileRecord>> Upload(
        Guid orgId, [FromForm] OrgFileUploadRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        if (!isSuperAdmin)
        {
            var ok = await _security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Create, ct);
            if (!ok) return Forbid();
        }
        if (request.File is null || request.File.Length == 0) return BadRequest("No file provided.");
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (!await db.Organizations.AnyAsync(o => o.Id == orgId, ct)) return NotFound("Organization not found.");
        if (!await db.UploadFileTypes.AnyAsync(t => t.Id == request.UploadFileTypeId, ct)) return BadRequest("Invalid file type.");

        var storedName  = $"{Guid.NewGuid():N}{Path.GetExtension(request.File.FileName)}";
        var storagePath = _storage.OrgFilePath(orgId, storedName);
        await using var stream = request.File.OpenReadStream();
        await _storage.WriteAsync(storagePath, stream, ct);

        var now = DateTime.UtcNow;
        var orgFile = new OrganizationFile
        {
            Id                   = Guid.NewGuid(),
            OrganizationId       = orgId,
            UploadFileTypeId     = request.UploadFileTypeId,
            FileName             = request.File.FileName,
            StoredFileName       = storedName,
            ContentType          = request.File.ContentType,
            FileSize             = request.File.Length,
            StoragePath          = storagePath,
            Description          = request.Description?.Trim(),
            IsPublic             = request.IsPublic,
            SortOrder            = request.SortOrder,
            PublishedByAppUserId = request.IsPublic ? userId : null,
            DatePublished        = request.IsPublic ? now : null,
            DateCreated          = now,
            CreatedByAppUserId   = userId.Value,
        };
        db.OrganizationFiles.Add(orgFile);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrganizationFile), orgFile.Id, orgFile, userId.Value, AppSources.WebApi));
        var created = await WithIncludes(db.OrganizationFiles).AsNoTracking().FirstAsync(f => f.Id == orgFile.Id, ct);
        return CreatedAtAction(nameof(GetAll), new { orgId }, _mapper.Map<OrganizationFileRecord>(created));
    }

    // GET /api/organizations/{orgId}/files/shareable-user-files
    /// <summary>
    /// The user files this group could take a copy of (item 175) — exactly the set
    /// <see cref="CopyFromUser"/> would accept: public files, and files their owners shared
    /// with this group. Feeds the content picker that replaced the paste-a-Guid dialog.
    /// </summary>
    /// <remarks>Gated like the copy itself (OrganizationFiles Create): browsing the candidates
    /// is preparation for the copy, and someone the copy would refuse has no business with the
    /// list. Owner names come through the display name only.</remarks>
    [HttpGet("shareable-user-files")]
    public async Task<ActionResult<IEnumerable<ShareableUserFileRecord>>> GetShareableUserFiles(
        Guid orgId, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        if (!User.IsInRole(RoleNames.SuperAdmin))
        {
            var ok = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Create, ct);
            if (!ok) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var candidates = await db.UploadFiles.AsNoTracking()
            .Where(f => f.IsPublic
                     || db.UploadFileOrganizationShares.Any(sh =>
                            sh.UploadFileId == f.Id && sh.OrganizationId == orgId && sh.IsActive))
            .OrderByDescending(f => db.UploadFileOrganizationShares.Any(sh =>
                sh.UploadFileId == f.Id && sh.OrganizationId == orgId && sh.IsActive))
            .ThenByDescending(f => f.DateCreated)
            .Select(f => new ShareableUserFileRecord(
                f.Id, f.FileName, f.ContentType, f.FileSize, f.Description,
                f.AppUser!.DisplayName, f.DateCreated,
                db.UploadFileOrganizationShares.Any(sh =>
                    sh.UploadFileId == f.Id && sh.OrganizationId == orgId && sh.IsActive)))
            .ToListAsync(ct);

        return Ok(candidates);
    }

    // POST /api/organizations/{orgId}/files/copy-from-user/{uploadFileId}
    // Shares a user file into the org. Always non-public by default.
    // If the member has Update permission they may request PublishImmediately.
    // Returns OrgFileCopyResult which includes whether publish was possible/done.
    [HttpPost("copy-from-user/{uploadFileId:guid}")]
    public async Task<ActionResult<OrgFileCopyResult>> CopyFromUser(
        Guid orgId, Guid uploadFileId, [FromBody] CopyFromUserRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        if (!isSuperAdmin)
        {
            var ok = await _security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Create, ct);
            if (!ok) return Forbid();
        }
        bool canPublish = isSuperAdmin || await _security.HasAccessAsync(userId.Value, orgId,
            OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Update, ct);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var source = await db.UploadFiles.Include(f => f.UploadFileType).AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == uploadFileId, ct);
        if (source is null) return NotFound("Source file not found.");

        bool canAccess = source.IsPublic
            || await db.UploadFileOrganizationShares.AnyAsync(
                s => s.UploadFileId == uploadFileId && s.OrganizationId == orgId && s.IsActive, ct);
        if (!canAccess && !isSuperAdmin)
            return Forbid("The source file is not public or shared with this organization.");

        Stream? srcStream = null;
        if (!string.IsNullOrEmpty(source.StoragePath) && _storage.Exists(source.StoragePath))
            srcStream = await _storage.OpenReadAsync(source.StoragePath, ct);
        else if (source.FileData is { Length: > 0 })
            srcStream = new MemoryStream(source.FileData);
        if (srcStream is null) return UnprocessableEntity("Source file has no accessible data in storage.");

        var storedName  = $"{Guid.NewGuid():N}{Path.GetExtension(source.FileName)}";
        var storagePath = _storage.OrgFilePath(orgId, storedName);
        await using (srcStream) await _storage.WriteAsync(storagePath, srcStream, ct);

        bool publishNow = request.PublishImmediately && canPublish;
        var  now        = DateTime.UtcNow;

        var orgFile = new OrganizationFile
        {
            Id                   = Guid.NewGuid(),
            OrganizationId       = orgId,
            UploadFileTypeId     = source.UploadFileTypeId,
            FileName             = source.FileName,
            StoredFileName       = storedName,
            ContentType          = source.ContentType,
            FileSize             = source.FileSize,
            StoragePath          = storagePath,
            Description          = request.Description?.Trim() ?? source.Description,
            IsPublic             = publishNow,
            PublishedByAppUserId = publishNow ? userId : null,
            DatePublished        = publishNow ? now : null,
            SortOrder            = 0,
            SourceUploadFileId   = source.Id,
            DateCreated          = now,
            CreatedByAppUserId   = userId.Value,
        };
        db.OrganizationFiles.Add(orgFile);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrganizationFile), orgFile.Id, orgFile, userId.Value, AppSources.WebApi));

        var created = await WithIncludes(db.OrganizationFiles).AsNoTracking().FirstAsync(f => f.Id == orgFile.Id, ct);
        return CreatedAtAction(nameof(GetAll), new { orgId },
            new OrgFileCopyResult(_mapper.Map<OrganizationFileRecord>(created), canPublish, publishNow));
    }

    // PUT /api/organizations/{orgId}/files/{id}/publish
    // Approve or revoke public access. Logs approver and timestamp.
    [HttpPut("{id:guid}/publish")]
    public async Task<ActionResult<OrganizationFileRecord>> Publish(
        Guid orgId, Guid id, [FromBody] PublishOrgFileRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        if (!isSuperAdmin)
        {
            var ok = await _security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Update, ct);
            if (!ok) return Forbid();
        }
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var fileBefore = await db.OrganizationFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == orgId, ct);
        var file = await db.OrganizationFiles.FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == orgId, ct);
        if (file is null) return NotFound();

        var now = DateTime.UtcNow;
        file.IsPublic             = request.IsPublic;
        file.DateUpdated          = now;
        file.UpdatedByAppUserId   = userId.Value;
        file.PublishedByAppUserId = request.IsPublic ? userId.Value : null;
        file.DatePublished        = request.IsPublic ? now : null;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(OrganizationFile), id, fileBefore!, file, userId.Value, AppSources.WebApi));
        var updated = await WithIncludes(db.OrganizationFiles).AsNoTracking().FirstAsync(f => f.Id == id, ct);
        return Ok(_mapper.Map<OrganizationFileRecord>(updated));
    }

    // PUT /api/organizations/{orgId}/files/{id}  (metadata only, not publish status)
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrganizationFileRecord>> Update(
        Guid orgId, Guid id, [FromBody] OrgFileUpdateRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        if (!isSuperAdmin)
        {
            var ok = await _security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Update, ct);
            if (!ok) return Forbid();
        }
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var fileBefore = await db.OrganizationFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == orgId, ct);
        var file = await db.OrganizationFiles.FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == orgId, ct);
        if (file is null) return NotFound();
        file.Description        = request.Description?.Trim();
        file.SortOrder          = request.SortOrder;
        file.DateUpdated        = DateTime.UtcNow;
        file.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(OrganizationFile), id, fileBefore!, file, userId.Value, AppSources.WebApi));
        var updated = await WithIncludes(db.OrganizationFiles).AsNoTracking().FirstAsync(f => f.Id == id, ct);
        return Ok(_mapper.Map<OrganizationFileRecord>(updated));
    }

    // DELETE /api/organizations/{orgId}/files/{id}
    // Writes immutable audit log BEFORE deleting the file and storage bytes.
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid id, CancellationToken ct)
    {
        var userId = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        if (!isSuperAdmin)
        {
            var ok = await _security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Delete, ct);
            if (!ok) return Forbid();
        }
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var file = await db.OrganizationFiles
            .Include(f => f.Organization)
            .Include(f => f.PublishedByAppUser)
            .FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == orgId, ct);
        if (file is null) return NotFound();

        var deleter = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId.Value, ct);

        // Write audit log first
        db.OrganizationFileDeleteLogs.Add(new OrganizationFileDeleteLog
        {
            Id                        = Guid.NewGuid(),
            OrganizationId            = orgId,
            OrganizationName          = file.Organization.Name,
            OriginalFileId            = file.Id,
            FileName                  = file.FileName,
            ContentType               = file.ContentType,
            FileSize                  = file.FileSize,
            StoragePath               = file.StoragePath,
            SourceUploadFileId        = file.SourceUploadFileId,
            WasPublic                 = file.IsPublic,
            WasPublishedByAppUserId   = file.PublishedByAppUserId,
            WasPublishedByDisplayName = file.PublishedByAppUser?.DisplayName,
            WasDatePublished          = file.DatePublished,
            DeletedByAppUserId        = userId.Value,
            DeletedByDisplayName      = deleter?.DisplayName ?? userId.Value.ToString(),
            DateDeleted               = DateTime.UtcNow,
        });

        if (!string.IsNullOrEmpty(file.StoragePath))
            await _storage.DeleteAsync(file.StoragePath, ct);

        db.OrganizationFiles.Remove(file);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(OrganizationFile), id, file, userId.Value, AppSources.WebApi));
        return NoContent();
    }

    private static async Task TryAuditAsync(Task auditTask)
    {
        try { await auditTask; }
        catch { /* audit failure must not surface to the caller */ }
    }
}

public sealed record OrgFileUploadRequest(IFormFile? File, Guid UploadFileTypeId, string? Description, bool IsPublic, int SortOrder);
public sealed record OrgFileUpdateRequest(string? Description, int SortOrder);
public sealed record CopyFromUserRequest(string? Description, bool PublishImmediately = false);

/// <summary>One candidate for the share-from-user picker (item 175). SharedWithOrganization
/// distinguishes "their owner offered it to this group" from "public to everyone" — the
/// picker's Source facet.</summary>
public sealed record ShareableUserFileRecord(
    Guid Id, string FileName, string ContentType, long FileSize, string? Description,
    string? OwnerDisplayName, DateTime DateCreated, bool SharedWithOrganization);
public sealed record OrgFileCopyResult(OrganizationFileRecord File, bool CanPublishImmediately, bool PublishedImmediately);
public sealed record PublishOrgFileRequest(bool IsPublic);
