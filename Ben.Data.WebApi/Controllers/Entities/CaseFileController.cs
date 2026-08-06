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

    public CaseFileController(IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage)
    {
        _db = db;
        _fileStorage = fileStorage;
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
    /// Files tab, without copying bytes. Copy-on-attach semantics are a separate future phase —
    /// this is a reference, matching how every other CaseFile row already behaves.
    /// </summary>
    [HttpPost("link/{uploadFileId:guid}")]
    public async Task<ActionResult<CaseFileRecord>> Link(
        Guid orgId, Guid caseId, Guid uploadFileId, [FromBody] LinkCaseFileRequest? request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct)) return NotFound();

        var uploadFile = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == uploadFileId, ct);
        if (uploadFile is null) return NotFound("File not found.");
        if (await db.CaseFiles.AnyAsync(f => f.CaseId == caseId && f.UploadFileId == uploadFileId, ct))
            return Conflict("This file is already linked to this case.");

        var caseFile = new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = uploadFileId,
            Description = string.IsNullOrWhiteSpace(request?.Description) ? null : request.Description.Trim(),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseFiles.Add(caseFile);
        await db.SaveChangesAsync(ct);
        caseFile.UploadFile = uploadFile;
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
