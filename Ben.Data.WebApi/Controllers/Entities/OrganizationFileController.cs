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

    public OrganizationFileController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        IFileStorageService storage)
    {
        _dbFactory = dbFactory;
        _mapper    = mapper;
        _security  = security;
        _storage   = storage;
    }

    private Guid? CurrentUserId()
    {
        var appUserIdClaim = User.FindFirst("app_user_id")?.Value;
        if (appUserIdClaim is not null && Guid.TryParse(appUserIdClaim, out var id1)) return id1;
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return sub is not null && Guid.TryParse(sub, out var id2) ? id2 : null;
    }

    // ── GET /api/organizations/{orgId}/files ─────────────────────────────────
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationFileRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        var userId       = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canRead = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Read, ct);
            if (!canRead) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var files = await db.OrganizationFiles
            .Include(f => f.UploadFileType)
            .Include(f => f.CreatedByAppUser)
            .Where(f => f.OrganizationId == orgId)
            .OrderBy(f => f.SortOrder).ThenBy(f => f.FileName)
            .AsNoTracking()
            .ToListAsync(ct);

        return Ok(_mapper.Map<List<OrganizationFileRecord>>(files));
    }

    // ── GET /api/organizations/{orgId}/files/{id}/download ──────────────────
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid orgId, Guid id, CancellationToken ct)
    {
        var userId       = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canRead = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Read, ct);
            if (!canRead) return Forbid();
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

    // ── POST /api/organizations/{orgId}/files ───────────────────────────────
    [HttpPost]
    [RequestSizeLimit(200 * 1024 * 1024)]
    public async Task<ActionResult<OrganizationFileRecord>> Upload(
        Guid orgId, [FromForm] OrgFileUploadRequest request, CancellationToken ct)
    {
        var userId       = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canCreate = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Create, ct);
            if (!canCreate) return Forbid();
        }

        if (request.File is null || request.File.Length == 0) return BadRequest("No file provided.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return NotFound("Organization not found.");

        var fileType = await db.UploadFileTypes.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.UploadFileTypeId, ct);
        if (fileType is null) return BadRequest("Invalid file type.");

        var storedName  = $"{Guid.NewGuid():N}{Path.GetExtension(request.File.FileName)}";
        var storagePath = _storage.OrgFilePath(orgId, storedName);

        await using var stream = request.File.OpenReadStream();
        await _storage.WriteAsync(storagePath, stream, ct);

        var orgFile = new OrganizationFile
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = orgId,
            UploadFileTypeId   = request.UploadFileTypeId,
            FileName           = request.File.FileName,
            StoredFileName     = storedName,
            ContentType        = request.File.ContentType,
            FileSize           = request.File.Length,
            StoragePath        = storagePath,
            Description        = request.Description?.Trim(),
            IsPublic           = request.IsPublic,
            SortOrder          = request.SortOrder,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value,
        };

        db.OrganizationFiles.Add(orgFile);
        await db.SaveChangesAsync(ct);

        var created = await db.OrganizationFiles
            .Include(f => f.UploadFileType)
            .Include(f => f.CreatedByAppUser)
            .AsNoTracking()
            .FirstAsync(f => f.Id == orgFile.Id, ct);

        return CreatedAtAction(nameof(GetAll), new { orgId }, _mapper.Map<OrganizationFileRecord>(created));
    }

    // ── POST /api/organizations/{orgId}/files/copy-from-user/{uploadFileId} ──
    /// <summary>
    /// Copies a user's public or organization-shared UploadFile into this organization's file library.
    /// The original user file is never modified. The member must have OrganizationFiles-Create permission.
    /// </summary>
    [HttpPost("copy-from-user/{uploadFileId:guid}")]
    public async Task<ActionResult<OrganizationFileRecord>> CopyFromUser(
        Guid orgId, Guid uploadFileId, [FromBody] CopyFromUserRequest request, CancellationToken ct)
    {
        var userId       = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canCreate = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Create, ct);
            if (!canCreate) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        // Verify the source file is accessible (public OR shared with this org)
        var source = await db.UploadFiles
            .Include(f => f.UploadFileType)
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == uploadFileId, ct);
        if (source is null) return NotFound("Source file not found.");

        bool canAccess = source.IsPublic
            || await db.UploadFileOrganizationShares.AnyAsync(
                s => s.UploadFileId == uploadFileId && s.OrganizationId == orgId && s.IsActive, ct);

        if (!canAccess && !isSuperAdmin)
            return Forbid("The source file is not public or shared with this organization.");

        // Copy the file bytes to org storage
        Stream? sourceStream = null;
        if (!string.IsNullOrEmpty(source.StoragePath) && _storage.Exists(source.StoragePath))
            sourceStream = await _storage.OpenReadAsync(source.StoragePath, ct);
        else if (source.FileData is { Length: > 0 })
            sourceStream = new MemoryStream(source.FileData);

        if (sourceStream is null)
            return UnprocessableEntity("Source file has no accessible data in storage.");

        var storedName  = $"{Guid.NewGuid():N}{Path.GetExtension(source.FileName)}";
        var storagePath = _storage.OrgFilePath(orgId, storedName);

        await using (sourceStream)
            await _storage.WriteAsync(storagePath, sourceStream, ct);

        var orgFile = new OrganizationFile
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = orgId,
            UploadFileTypeId   = source.UploadFileTypeId,
            FileName           = source.FileName,
            StoredFileName     = storedName,
            ContentType        = source.ContentType,
            FileSize           = source.FileSize,
            StoragePath        = storagePath,
            Description        = request.Description?.Trim() ?? source.Description,
            IsPublic           = request.IsPublic,
            SortOrder          = 0,
            SourceUploadFileId = source.Id,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value,
        };

        db.OrganizationFiles.Add(orgFile);
        await db.SaveChangesAsync(ct);

        var created = await db.OrganizationFiles
            .Include(f => f.UploadFileType)
            .Include(f => f.CreatedByAppUser)
            .AsNoTracking()
            .FirstAsync(f => f.Id == orgFile.Id, ct);

        return CreatedAtAction(nameof(GetAll), new { orgId }, _mapper.Map<OrganizationFileRecord>(created));
    }

    // ── PUT /api/organizations/{orgId}/files/{id} ────────────────────────────
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrganizationFileRecord>> Update(
        Guid orgId, Guid id, [FromBody] OrgFileUpdateRequest request, CancellationToken ct)
    {
        var userId       = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canUpdate = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Update, ct);
            if (!canUpdate) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var file = await db.OrganizationFiles
            .FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == orgId, ct);
        if (file is null) return NotFound();

        file.Description        = request.Description?.Trim();
        file.IsPublic           = request.IsPublic;
        file.SortOrder          = request.SortOrder;
        file.DateUpdated        = DateTime.UtcNow;
        file.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);

        var updated = await db.OrganizationFiles
            .Include(f => f.UploadFileType)
            .Include(f => f.CreatedByAppUser)
            .AsNoTracking()
            .FirstAsync(f => f.Id == id, ct);

        return Ok(_mapper.Map<OrganizationFileRecord>(updated));
    }

    // ── DELETE /api/organizations/{orgId}/files/{id} ─────────────────────────
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid id, CancellationToken ct)
    {
        var userId       = CurrentUserId();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canDelete = await _security.HasAccessAsync(userId.Value, orgId,
                OrganizationSecurityTable.OrganizationFiles, OrganizationSecurityAction.Delete, ct);
            if (!canDelete) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var file = await db.OrganizationFiles
            .FirstOrDefaultAsync(f => f.Id == id && f.OrganizationId == orgId, ct);
        if (file is null) return NotFound();

        if (!string.IsNullOrEmpty(file.StoragePath))
            await _storage.DeleteAsync(file.StoragePath, ct);

        db.OrganizationFiles.Remove(file);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public sealed record OrgFileUploadRequest(
    IFormFile? File,
    Guid UploadFileTypeId,
    string? Description,
    bool IsPublic,
    int SortOrder);

public sealed record OrgFileUpdateRequest(
    string? Description,
    bool IsPublic,
    int SortOrder);

public sealed record CopyFromUserRequest(
    string? Description,
    bool IsPublic);
