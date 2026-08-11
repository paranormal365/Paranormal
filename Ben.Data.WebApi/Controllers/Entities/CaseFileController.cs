using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A case's general Files/Evidence tab — any content type, listed and uploaded here.
/// Deleting a row un-links the file from the case; the underlying UploadFile is preserved
/// (chain-of-custody — matches the non-destructive principle used throughout the media editor).
/// </summary>
[ApiController]
[Route("api/orgs/{orgId:guid}/cases/{caseId:guid}/files")]
[Authorize]
public sealed class CaseFileController : BenControllerBase
{
    // Fixed "Case Evidence" UploadFileType — same one used by MyCaseController/CaseResearchController.
    private static readonly Guid CaseEvidenceFileTypeId = new("20000000-0000-0000-0000-000000000001");

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogService _auditLog;

    public CaseFileController(IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage, IAuditLogService auditLog)
    {
        _db = db;
        _fileStorage = fileStorage;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CaseFileRecord>>> GetAll(Guid orgId, Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();

        var files = await db.CaseFiles.AsNoTracking()
            .Include(f => f.UploadFile)
            .Where(f => f.CaseId == caseId)
            .OrderByDescending(f => f.DateCreated)
            .ToListAsync(ct);

        return Ok(files.Select(ToRecord));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<CaseFileRecord>> Upload(
        Guid orgId, Guid caseId, [FromForm] string? description, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("File is empty.");

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct)) return NotFound();

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var fileBytes = ms.ToArray();

        var storedName  = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var storagePath = _fileStorage.CaseFilePath(caseId, $"files/{storedName}");
        using (var ws = new MemoryStream(fileBytes))
            await _fileStorage.WriteAsync(storagePath, ws, ct);

        var uploadFile = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = CaseEvidenceFileTypeId, AppUserId = userId,
            FileName = file.FileName, StoredFileName = storedName,
            ContentType = file.ContentType, FileSize = fileBytes.Length,
            StoragePath = storagePath, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);

        var caseFile = new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = uploadFile.Id,
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseFiles.Add(caseFile);

        await db.SaveChangesAsync(ct);
        caseFile.UploadFile = uploadFile;
        return Ok(ToRecord(caseFile));
    }

    /// <summary>
    /// Links an existing UploadFile (e.g. picked from the universal media library) to this case's
    /// Files tab by making a real, independent byte-copy of it (copy-on-attach, item #6 phase 2) —
    /// the case's copy survives even if the source's owner later deletes or replaces their
    /// personal file. The new <see cref="CaseFile"/> points at the copy, not the source; the copy's
    /// <see cref="UploadFile.CaseCopyOfUploadFileId"/> records where it came from.
    /// </summary>
    [HttpPost("link/{uploadFileId:guid}")]
    public async Task<ActionResult<CaseFileRecord>> Link(
        Guid orgId, Guid caseId, Guid uploadFileId, [FromBody] LinkCaseFileRequest? request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct)) return NotFound();

        var sourceFile = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == uploadFileId, ct);
        if (sourceFile is null) return NotFound("File not found.");

        // Closes a pre-existing hole: Link previously did no visibility check on the source file at
        // all — any org member of the target case could reference (and, now, durably byte-copy) an
        // arbitrary UploadFileId by guessing its GUID.
        if (!await FileAudienceAccess.CanViewFileAsync(db, uploadFileId, userId, ct)) return Forbid();

        // "Already linked" now means the case already holds a copy of this source, not that a
        // CaseFile happens to reference this exact UploadFileId (every Link mints a fresh copy).
        var alreadyLinked = await db.CaseFiles.AsNoTracking()
            .Where(f => f.CaseId == caseId)
            .Join(db.UploadFiles.AsNoTracking(), f => f.UploadFileId, uf => uf.Id, (f, uf) => uf.CaseCopyOfUploadFileId)
            .AnyAsync(sourceId => sourceId == uploadFileId, ct);
        if (alreadyLinked) return Conflict("This file is already linked to this case.");

        Stream sourceStream;
        if (!string.IsNullOrEmpty(sourceFile.StoragePath))
            sourceStream = await _fileStorage.OpenReadAsync(sourceFile.StoragePath, ct);
        else if (sourceFile.FileData is not null)
            sourceStream = new MemoryStream(sourceFile.FileData);
        else
            return UnprocessableEntity("Source file has no stored content to copy — it needs to be re-saved first.");

        using var ms = new MemoryStream();
        await using (sourceStream)
            await sourceStream.CopyToAsync(ms, ct);
        var fileBytes = ms.ToArray();

        var storedName  = $"{Guid.NewGuid()}{Path.GetExtension(sourceFile.FileName)}";
        var storagePath = _fileStorage.CaseFilePath(caseId, $"files/{storedName}");
        using (var ws = new MemoryStream(fileBytes))
            await _fileStorage.WriteAsync(storagePath, ws, ct);

        var copy = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = CaseEvidenceFileTypeId, AppUserId = userId,
            FileName = sourceFile.FileName, StoredFileName = storedName,
            ContentType = sourceFile.ContentType, FileSize = fileBytes.Length,
            StoragePath = storagePath, IsPublic = false,
            CaseCopyOfUploadFileId = sourceFile.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(copy);

        var caseFile = new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = copy.Id,
            Description = string.IsNullOrWhiteSpace(request?.Description) ? null : request.Description.Trim(),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseFiles.Add(caseFile);

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFile), copy.Id, copy, userId, AppSources.WebApi, ct));
        caseFile.UploadFile = copy;
        return Ok(ToRecord(caseFile));
    }

    [HttpDelete("{caseFileId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid caseId, Guid caseFileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();

        var caseFile = await db.CaseFiles.FirstOrDefaultAsync(f => f.Id == caseFileId && f.CaseId == caseId, ct);
        if (caseFile is null) return NotFound();

        db.CaseFiles.Remove(caseFile);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static async Task<bool> IsOrgMember(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);

    private static CaseFileRecord ToRecord(CaseFile f) => new()
    {
        Id = f.Id,
        CaseId = f.CaseId,
        UploadFileId = f.UploadFileId,
        FileName = f.UploadFile.FileName,
        ContentType = f.UploadFile.ContentType,
        FileSize = f.UploadFile.FileSize,
        Description = f.Description,
        DateCreated = f.DateCreated,
        CreatedByAppUserId = f.CreatedByAppUserId,
    };
}
