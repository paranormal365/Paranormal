using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;

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
    private readonly IMediaIngestService _mediaIngest;
    private readonly IAvMetadataStripper _avStripper;

    private readonly Services.Billing.SubscriptionLimitGuard _limits;

    public CaseFileController(IDbContextFactory<BenDataContext> db, IFileStorageService fileStorage,
        IAuditLogService auditLog, Services.Billing.SubscriptionLimitGuard limits,
        IMediaIngestService mediaIngest, IAvMetadataStripper avStripper,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    {
        _db = db;
        _fileStorage = fileStorage;
        _auditLog = auditLog;
        _limits = limits;
        _mediaIngest = mediaIngest;
        _avStripper  = avStripper;
        _security = security;
    }

    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

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

        // How long each one is, for the callers that lay files out on a timeline. One query for the
        // whole page rather than one per file — the mixer asks for every audio file on a case.
        var fileIds  = files.Select(f => f.UploadFileId).ToList();
        var durations = await db.UploadFileMetadata.AsNoTracking()
            .Where(m => fileIds.Contains(m.UploadFileId) && m.DurationSeconds != null)
            .ToDictionaryAsync(m => m.UploadFileId, m => m.DurationSeconds, ct);

        return Ok(files.Select(f => ToRecord(f, durations.GetValueOrDefault(f.UploadFileId))));
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<CaseFileRecord>> Upload(
        Guid orgId, Guid caseId, [FromForm] string? description, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("File is empty.");

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await _limits.WhyReadOnlyAsync(orgId, ct) is { } readOnly) return BadRequest(readOnly);
        if (!await MayAsync(orgId, Ben.Data.Common.Enums.OrganizationSecurityAction.Create, ct)) return Forbid();
        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct)) return NotFound();

        var storedName  = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var storagePath = _fileStorage.CaseFilePath(caseId, $"files/{storedName}");

        // Ben's rule (2026-08-24): EXIF comes off on ANY upload, and what came off is kept in the
        // metadata table beside the record. Case evidence is the most sensitive upload on the
        // site — a photo taken inside somebody's home — and until now it wrote raw bytes and
        // extracted nothing. The ORIGINAL is still stored untouched; the stripped derivative is
        // what every serve path returns (item 179).
        var uploadFileId = Guid.NewGuid();
        IngestedMedia ingested;
        try
        {
            ingested = await _mediaIngest.IngestAsync(file, storagePath, uploadFileId, ct,
                (await MediaStrippingPolicy.ForOrganizationAsync(db, _avStripper, orgId, ct)).Strips);
        }
        catch (UnreadableImageException ex)
        {
            return BadRequest(ex.Message);
        }

        var uploadFile = new UploadFile
        {
            Id = uploadFileId, UploadFileTypeId = CaseEvidenceFileTypeId, AppUserId = userId,
            FileName = file.FileName, StoredFileName = storedName,
            // The served copy's type and size belong on the row; the original's are recorded in
            // the metadata table beside its EXIF.
            ContentType = ingested.ServedContentType, FileSize = ingested.ServedFileSize,
            StoragePath = storagePath, IsPublic = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);
        db.UploadFileMetadata.Add(ingested.Metadata);

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
        if (!await MayAsync(orgId, Ben.Data.Common.Enums.OrganizationSecurityAction.Create, ct)) return Forbid();
        if (!await db.Cases.AnyAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct)) return NotFound();
        if (await _limits.WhyReadOnlyAsync(orgId, ct) is { } readOnly) return BadRequest(readOnly);

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

        var storedName  = $"{Guid.NewGuid()}{Path.GetExtension(sourceFile.FileName)}";
        var storagePath = _fileStorage.CaseFilePath(caseId, $"files/{storedName}");
        // Straight source-to-destination copy — no reason to land the whole file in memory between
        // two streams that are both already streams.
        await using (sourceStream)
            await _fileStorage.WriteAsync(storagePath, sourceStream, ct);

        var copy = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = CaseEvidenceFileTypeId, AppUserId = userId,
            FileName = sourceFile.FileName, StoredFileName = storedName,
            ContentType = sourceFile.ContentType, FileSize = sourceFile.FileSize,
            StoragePath = storagePath, IsPublic = false,
            CaseCopyOfUploadFileId = sourceFile.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(copy);

        // Copy-on-attach mints a NEW file row, so it needs its own metadata row — carried from
        // the source, since the bytes are identical and were captured in the same place. Without
        // this, attaching a photo to a case silently dropped where it was taken.
        if (await _mediaIngest.DeriveMetadataAsync(db, sourceFile.Id, copy.Id,
                MediaKindFor(sourceFile.ContentType), ct) is { } derived)
        {
            db.UploadFileMetadata.Add(derived);
        }

        var caseFile = new CaseFile
        {
            Id = Guid.NewGuid(), CaseId = caseId, UploadFileId = copy.Id,
            Description = string.IsNullOrWhiteSpace(request?.Description) ? null : request.Description.Trim(),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseFiles.Add(caseFile);

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFile), copy.Id, copy, userId, AppSources.WebApi));
        caseFile.UploadFile = copy;
        return Ok(ToRecord(caseFile));
    }

    /// <summary>The metadata table's coarse kind, from a content type.</summary>
    private static string MediaKindFor(string? contentType) => contentType switch
    {
        not null when contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "Image",
        not null when contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => "Audio",
        not null when contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => "Video",
        _ => "Unknown",
    };

    [HttpDelete("{caseFileId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid caseId, Guid caseFileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, Ben.Data.Common.Enums.OrganizationSecurityAction.Delete, ct)) return Forbid();

        var caseFile = await db.CaseFiles.FirstOrDefaultAsync(f => f.Id == caseFileId && f.CaseId == caseId, ct);
        if (caseFile is null) return NotFound();

        db.CaseFiles.Remove(caseFile);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // Item 156 Phase D: bare membership stopped being the rule here. Case surfaces answer to
    // HasAccessAsync(Case, Read) — which carries the SuperAdmin and owner/admin bypasses, the
    // tier area gate, and the grants (the grandfather bridge included), all in one place.
    /// <summary>
    /// May the caller take this action here?
    /// </summary>
    /// <remarks>
    /// Create, update and delete used to ask for Case.READ — through a helper named for
    /// membership, which is neither what it asked nor what it meant: anybody who could SEE a case
    /// could destroy the things hanging off it. Survivable while every member was auto-granted
    /// case read; not survivable now that Ben ended the grandfathering (2026-08-26) and a read
    /// grant is a deliberate act. Owners and administrators still pass above this.
    /// </remarks>
    private Task<bool> MayAsync(Guid orgId, Ben.Data.Common.Enums.OrganizationSecurityAction action, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
            ? Task.FromResult(true)
            : _security.MayAsync(GetCurrentUserId(), orgId,
                Ben.Data.Common.Enums.OrganizationPermissionArea.Cases, action, ct);

    private async Task<bool> IsOrgMember(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await _security.HasAccessAsync(userId, orgId,
               Ben.Data.Common.Enums.OrganizationSecurityTable.Case,
               Ben.Data.Common.Enums.OrganizationSecurityAction.Read, ct);

    private static CaseFileRecord ToRecord(CaseFile f, double? durationSeconds = null) => new()
    {
        DurationSeconds = durationSeconds,
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
