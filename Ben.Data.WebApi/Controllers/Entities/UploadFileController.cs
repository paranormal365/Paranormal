using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Helpers;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Manages user-uploaded files: upload (multipart/form-data), metadata update,
/// download, and delete. Files are stored on the configured filesystem path;
/// the database holds metadata only. The download endpoint falls back to the
/// legacy <c>FileData</c> blob for rows not yet migrated by FileMigrationService.
/// </summary>
[ApiController]
[Route("api/upload-files")]
[Authorize]
public sealed class UploadFileController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IFileStorageService _fileStorage;
    private readonly IAuditLogService _auditLog;
    private readonly FileMetadataExtractorService _metadataExtractor;
    private readonly ILogger<UploadFileController> _logger;

    public UploadFileController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMapper mapper,
        IFileStorageService fileStorage,
        IAuditLogService auditLog,
        FileMetadataExtractorService metadataExtractor,
        ILogger<UploadFileController> logger)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _fileStorage = fileStorage;
        _auditLog = auditLog;
        _metadataExtractor = metadataExtractor;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current user's own files — backs the personal "Upload Files" management page.
    /// Deliberately owner-only, not the broader audience union: this is the caller's own file
    /// cabinet (with Download/Share/Delete/Replace actions), not a browse-everything-I-can-see
    /// view — that's <see cref="MediaLibraryController.GetFiles"/>. Previously had no owner filter
    /// at all (returned every UploadFile row in the system to any authenticated caller) — fixed
    /// as a follow-up to item #6 phase 3.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<UploadFileRecord>>> GetAll(CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.UploadFiles.AsNoTracking()
            .Where(f => f.AppUserId == userId && f.ArchivedFromUploadFileId == null) // archived prior versions (item #6 phase 3) aren't real listings
            .OrderByDescending(f => f.DateCreated)
            .ToListAsync(cancellationToken);
        return Ok(_mapper.Map<IEnumerable<UploadFileRecord>>(entities));
    }

    /// <summary>
    /// Returns one file's metadata, gated the same way <see cref="Download"/> gates its bytes —
    /// see <see cref="FileAudienceAccess.CanViewFileAsync"/>. Previously had no visibility check at
    /// all; fixed as a follow-up to item #6 phase 3.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UploadFileRecord>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();
        if (!await FileAudienceAccess.CanViewFileAsync(db, id, userId, cancellationToken)) return NotFound();
        return Ok(_mapper.Map<UploadFileRecord>(entity));
    }

    /// <summary>
    /// Streams a file's bytes. Gated by <see cref="FileAudienceAccess.CanViewFileAsync"/> — the
    /// same owner/sharing/audience union every other read path in this app respects. Previously
    /// only checked <c>IsPublic</c>, so any authenticated user (or anonymous caller, for public
    /// files) could download any file by ID regardless of ownership or sharing; fixed as a
    /// follow-up to item #6 phase 3.
    /// </summary>
    [HttpGet("{id:guid}/download")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        var userId = isAuthenticated ? GetCurrentUserId() : Guid.Empty;
        if (!await FileAudienceAccess.CanViewFileAsync(db, id, userId, cancellationToken))
            return isAuthenticated ? Forbid() : Unauthorized();

        // Prefer disk; fall back to FileData for rows not yet migrated
        if (!string.IsNullOrEmpty(entity.StoragePath))
        {
            var stream = await _fileStorage.OpenReadAsync(entity.StoragePath, cancellationToken);
            return File(stream, entity.ContentType, entity.FileName);
        }

        if (entity.FileData is not null)
            return File(entity.FileData, entity.ContentType, entity.FileName);

        return NotFound("File data is unavailable.");
    }

    [HttpPost]
    // No practical upload size limit for now — a future limit belongs at app-settings / per-person /
    // per-org / per-case / per-investigation scope, not a blanket cap baked into the endpoint.
    [DisableRequestSizeLimit]
    public async Task<ActionResult<UploadFileRecord>> Upload(
        [FromForm] Guid uploadFileTypeId,
        [FromForm] Guid appUserId,
        [FromForm] string? description,
        [FromForm] bool isPublic,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            return BadRequest("File is empty.");

        var callerId = GetCurrentUserId();
        if (callerId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        // The owner comes from the caller's token, not the form. This used to be taken straight
        // from `appUserId`, which meant any authenticated user could create a file owned by
        // someone else: the row showed up in that person's listings and the bytes landed under
        // their storage path. Content-planting and attribution forgery, from an unauthenticated
        // value. (Same reasoning already applied to org sharing, which dropped its client-supplied
        // actor ids for this reason.)
        //
        // The field survives because one caller genuinely needs it: the SuperAdmin user-detail
        // page (/admin/users/{id}) uploads on behalf of the user being administered. That stays,
        // gated on the role and with the target checked to exist; for everyone else a mismatch is
        // refused rather than quietly rewritten, so misuse surfaces instead of hiding.
        var ownerId = callerId;
        if (appUserId != Guid.Empty && appUserId != callerId)
        {
            if (!User.IsInRole(RoleNames.SuperAdmin))
                return Forbid();
            if (!await db.Users.AnyAsync(u => u.Id == appUserId, cancellationToken))
                return BadRequest("Target user not found.");
            ownerId = appUserId;
        }

        // Validate file extension against the selected type's allowed patterns
        var fileType = await db.UploadFileTypes
            .Include(t => t.AllowedExtensions)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == uploadFileTypeId, cancellationToken);

        if (fileType is null)
            return BadRequest("Upload file type not found.");

        if (!fileType.AllowAllExtensions)
        {
            var ext = Path.GetExtension(file.FileName);
            var patterns = fileType.AllowedExtensions.Select(e => e.Pattern);
            if (!FileExtensionPatternMatcher.IsAllowedByPatterns(patterns, ext))
                return BadRequest($"File extension '{ext}' is not permitted for file type '{fileType.Name}'.");
        }

        var contentType  = file.ContentType;
        var isSvg        = contentType.Contains("svg", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(file.FileName).Equals(".svg", StringComparison.OrdinalIgnoreCase);

        // Only SVGs are read into memory: sanitising one means parsing and rewriting the whole
        // document, so there is nothing to stream. They are text and small. Everything else goes
        // straight from the request to storage — see FormFileStorageExtensions for why.
        byte[]? sanitizedSvg = null;
        if (isSvg)
        {
            // Normalise content type — some browsers omit or mis-report SVG MIME
            contentType = "image/svg+xml";

            using var svgBuffer = new MemoryStream();
            await file.CopyToAsync(svgBuffer, cancellationToken);
            try
            {
                sanitizedSvg = SvgSanitizer.Sanitize(svgBuffer.ToArray());
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest($"SVG rejected: {ex.Message}");
            }
        }

        var entity = new UploadFile
        {
            Id = Guid.NewGuid(),
            UploadFileTypeId = uploadFileTypeId,
            AppUserId = ownerId,
            FileName = file.FileName,
            StoredFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}",
            ContentType = contentType,
            // Sanitising rewrites the document, so the stored size is the sanitised length rather
            // than what the client sent.
            FileSize = sanitizedSvg?.Length ?? file.Length,
            FileData = null,   // not stored in DB — written to disk below
            Description = description,
            IsPublic = isPublic,
            SortOrder = 0,
            DateCreated = DateTime.UtcNow,
            // Owner and author are separate facts: on a SuperAdmin on-behalf-of upload the file
            // belongs to the target user but was created by the admin, and the audit trail should
            // say so rather than erase who acted. Identical for ordinary uploads, where they match.
            CreatedByAppUserId = callerId
        };

        // Write to disk first; if this throws the DB record is never committed
        var relativePath = _fileStorage.UserFilePath(ownerId, entity.StoredFileName);
        if (sanitizedSvg is not null)
            await _fileStorage.WriteBytesAsync(relativePath, sanitizedSvg, cancellationToken);
        else
            await _fileStorage.WriteFormFileAsync(relativePath, file, cancellationToken);
        entity.StoragePath = relativePath;

        db.UploadFiles.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFile), entity.Id, entity, GetCurrentUserId(), AppSources.WebApi, cancellationToken));

        // Extract and persist metadata — fire-and-forget so upload latency is unaffected.
        // Reads the file back off storage rather than capturing its bytes: holding the upload in
        // memory until this finishes would reintroduce exactly the cost streaming just removed.
        var metadataFileId = entity.Id;
        var metadataPath   = relativePath;
        _ = Task.Run(async () =>
        {
            try
            {
                await using var stored = await _fileStorage.OpenReadAsync(metadataPath, CancellationToken.None);
                var meta = _metadataExtractor.Extract(metadataFileId, contentType, stored);
                await using var dbMeta = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
                dbMeta.UploadFileMetadata.Add(meta);
                await dbMeta.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Extraction is best-effort — never surface this to the caller — but a silent
                // failure here previously meant a systemic breakage (e.g. a bad extractor
                // dependency) was invisible until someone noticed missing metadata.
                _logger.LogWarning(ex, "Metadata extraction failed for upload file {UploadFileId}", metadataFileId);
            }
        });

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<UploadFileRecord>(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UploadFileRecord>> Update(
        Guid id,
        [FromBody] UpdateUploadFileRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var before = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (before is null) return NotFound();

        var userId = GetCurrentUserId();
        if (before.AppUserId != userId && !User.IsInRole(RoleNames.SuperAdmin))
            return Forbid();

        var entity = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        entity.UploadFileTypeId = request.UploadFileTypeId;
        entity.Description = request.Description;
        entity.IsPublic = request.IsPublic;
        entity.SortOrder = request.SortOrder;
        entity.DateUpdated = DateTime.UtcNow;
        // Server-derived, never taken from the request: an editor who can name themselves can
        // name someone else. The request no longer carries the field at all.
        entity.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFile), id, before, entity, GetCurrentUserId(), AppSources.WebApi, cancellationToken));
        return Ok(_mapper.Map<UploadFileRecord>(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        db.UploadFiles.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(UploadFile), id, entity, GetCurrentUserId(), AppSources.WebApi, cancellationToken));

        // Delete from disk after the DB record is gone
        if (!string.IsNullOrEmpty(entity.StoragePath))
            await _fileStorage.DeleteAsync(entity.StoragePath, cancellationToken);

        return NoContent();
    }

    /// <summary>Returns all child clip files that were derived from this file via the region-clip workflow.</summary>
    [HttpGet("{id:guid}/clips")]
    public async Task<ActionResult<IEnumerable<UploadFileRecord>>> GetChildClips(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var clips = await db.UploadFiles.AsNoTracking()
            .Where(f => f.ParentFileId == id)
            .OrderBy(f => f.RegionStart)
            .ToListAsync(cancellationToken);
        return Ok(_mapper.Map<IEnumerable<UploadFileRecord>>(clips));
    }

    // PUT /api/upload-files/{id}/edit-state — persists the Fabric.js editor JSON snapshot
    [HttpPut("{id:guid}/edit-state")]
    public async Task<ActionResult<UploadFileRecord>> SaveEditState(
        Guid id, [FromBody] SaveEditStateRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrNull();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        entity.EditStateJson      = request.EditStateJson;
        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(_mapper.Map<UploadFileRecord>(entity));
    }

    // POST /api/upload-files/{id}/save-as-version — saves edited image bytes as a new UploadFile linked to original
    [HttpPost("{id:guid}/save-as-version")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<UploadFileRecord>> SaveAsVersion(
        Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0) return BadRequest("File is empty.");

        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var parent = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (parent is null) return NotFound();

        var storedName  = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var storagePath = _fileStorage.UserFilePath(userId, storedName);

        await _fileStorage.WriteFormFileAsync(storagePath, file, cancellationToken);

        var entity = new UploadFile
        {
            Id                 = Guid.NewGuid(),
            UploadFileTypeId   = parent.UploadFileTypeId,
            AppUserId          = userId,
            FileName           = Path.GetFileNameWithoutExtension(parent.FileName) + "-edited" + Path.GetExtension(file.FileName),
            StoredFileName     = storedName,
            StoragePath        = storagePath,
            ContentType        = file.ContentType,
            FileSize           = file.Length,
            IsPublic           = false,
            IsEditedVersion    = true,
            ParentFileId       = id,
            SortOrder          = 0,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, _mapper.Map<UploadFileRecord>(entity));
    }

    /// <summary>
    /// Replaces this file's bytes in place (item #6 phase 3) — same <see cref="UploadFile.Id"/>, so
    /// existing comments/votes/shares/case-links stay attached. The old bytes are archived, not
    /// discarded: a new row inherits the current <c>StoragePath</c> (no byte copy needed — the file
    /// on disk simply now belongs to the archive row) with <see cref="UploadFile.ArchivedFromUploadFileId"/>
    /// pointing back here. Every case copy (<c>CaseCopyOfUploadFileId == id</c>, see
    /// <see cref="CaseFileController.Link"/>) is overwritten in place too, at its own existing
    /// <c>StoragePath</c>, so each copy's <c>CaseFile</c> pointer and any comments/votes on it also
    /// survive untouched — only the source gets an archive row, not every copy.
    /// </summary>
    [HttpPost("{id:guid}/replace")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<UploadFileRecord>> Replace(
        Guid id, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0) return BadRequest("File is empty.");

        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var before = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (before is null) return NotFound();
        if (before.AppUserId != userId) return Forbid();

        // Same extension only — "replace" means a new version of the same thing, and it's what
        // makes overwriting each case copy at its existing StoragePath unambiguously safe (no
        // path that used to hold a .png ending up with JPEG bytes under it).
        var newExt = Path.GetExtension(file.FileName);
        var oldExt = Path.GetExtension(before.FileName);
        if (!string.Equals(newExt, oldExt, StringComparison.OrdinalIgnoreCase))
            return BadRequest($"Replacement file must have the same extension ('{oldExt}') as the file being replaced.");

        var fileType = await db.UploadFileTypes
            .Include(t => t.AllowedExtensions)
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == before.UploadFileTypeId, cancellationToken);
        if (fileType is not null && !fileType.AllowAllExtensions)
        {
            var patterns = fileType.AllowedExtensions.Select(e => e.Pattern);
            if (!FileExtensionPatternMatcher.IsAllowedByPatterns(patterns, newExt))
                return BadRequest($"File extension '{newExt}' is not permitted for file type '{fileType.Name}'.");
        }

        var contentType = file.ContentType;
        var isSvg = contentType.Contains("svg", StringComparison.OrdinalIgnoreCase)
                 || newExt.Equals(".svg", StringComparison.OrdinalIgnoreCase);

        // As in Upload: only SVG has to be resident, because sanitising rewrites the document.
        byte[]? sanitizedSvg = null;
        if (isSvg)
        {
            contentType = "image/svg+xml";
            using var svgBuffer = new MemoryStream();
            await file.CopyToAsync(svgBuffer, cancellationToken);
            try { sanitizedSvg = SvgSanitizer.Sanitize(svgBuffer.ToArray()); }
            catch (InvalidOperationException ex) { return BadRequest($"SVG rejected: {ex.Message}"); }
        }
        var newFileSize = sanitizedSvg?.Length ?? file.Length;

        var entity = await db.UploadFiles.FirstAsync(f => f.Id == id, cancellationToken);

        // Archive the old bytes before anything else moves.
        var archive = new UploadFile
        {
            Id = Guid.NewGuid(), UploadFileTypeId = entity.UploadFileTypeId, AppUserId = entity.AppUserId,
            FileName = entity.FileName, StoredFileName = entity.StoredFileName,
            ContentType = entity.ContentType, FileSize = entity.FileSize, StoragePath = entity.StoragePath,
            Description = entity.Description, IsPublic = false, // archives are never independently visible
            ArchivedFromUploadFileId = id,
            DateCreated = entity.DateCreated, // preserves the archived content's real vintage
            DateUpdated = DateTime.UtcNow,     // when it was archived
            CreatedByAppUserId = entity.CreatedByAppUserId, UpdatedByAppUserId = userId,
        };
        db.UploadFiles.Add(archive);

        // Rewrite the source in place at a fresh path — the old path now belongs to the archive row above.
        var newStoredName  = $"{Guid.NewGuid()}{newExt}";
        var newStoragePath = _fileStorage.UserFilePath(entity.AppUserId, newStoredName);
        if (sanitizedSvg is not null)
            await _fileStorage.WriteBytesAsync(newStoragePath, sanitizedSvg, cancellationToken);
        else
            await _fileStorage.WriteFormFileAsync(newStoragePath, file, cancellationToken);

        entity.StoredFileName = newStoredName;
        entity.StoragePath    = newStoragePath;
        entity.FileName       = file.FileName;
        entity.ContentType    = contentType;
        entity.FileSize       = newFileSize;
        entity.DateUpdated    = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;

        // Propagate to every case copy — overwrite bytes at each copy's OWN existing StoragePath
        // (LocalFileStorageService.WriteAsync opens FileMode.Create, so this truncates in place)
        // so its CaseFile pointer, comments, and votes all stay attached without any new rows.
        var copies = await db.UploadFiles
            .Where(f => f.CaseCopyOfUploadFileId == id)
            .ToListAsync(cancellationToken);
        foreach (var copy in copies)
        {
            if (string.IsNullOrEmpty(copy.StoragePath)) continue; // legacy FileData-blob row — nothing on disk to overwrite
            // Re-read the source we just wrote rather than the request: the request body has
            // already been consumed by the write above, and keeping the bytes around to fan out
            // here is the memory cost this whole change removes.
            await using var source = await _fileStorage.OpenReadAsync(newStoragePath, cancellationToken);
            await _fileStorage.WriteAsync(copy.StoragePath, source, cancellationToken);
            copy.FileName    = file.FileName;
            copy.ContentType = contentType;
            copy.FileSize    = newFileSize;
            copy.DateUpdated = DateTime.UtcNow;
            copy.UpdatedByAppUserId = userId;
        }

        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFile), id, before, entity, userId, AppSources.WebApi, cancellationToken));

        // Refresh extracted metadata for the source and every updated copy — UploadFileMetadata is
        // 1-to-1 with UploadFile and is normally only ever inserted once at upload time, so a
        // replace must delete-then-add or stale EXIF/GPS/dimensions stay attached to bytes they no
        // longer describe, which is actively misleading for evidence review. Fire-and-forget, same
        // as the initial-upload extraction, so replace latency is unaffected.
        var idsToRefresh = new List<Guid> { id };
        idsToRefresh.AddRange(copies.Select(c => c.Id));
        _ = Task.Run(async () =>
        {
            try
            {
                await using var dbMeta = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
                var stale = await dbMeta.UploadFileMetadata
                    .Where(m => idsToRefresh.Contains(m.UploadFileId))
                    .ToListAsync(CancellationToken.None);
                dbMeta.UploadFileMetadata.RemoveRange(stale);
                // Every id describes the same bytes, so one handle on the stored file serves them
                // all — Extract rewinds before each read.
                await using var stored = await _fileStorage.OpenReadAsync(newStoragePath, CancellationToken.None);
                foreach (var refreshId in idsToRefresh)
                    dbMeta.UploadFileMetadata.Add(_metadataExtractor.Extract(refreshId, contentType, stored));
                await dbMeta.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Metadata refresh failed for upload file {UploadFileId} and its copies", id);
            }
        });

        return Ok(_mapper.Map<UploadFileRecord>(entity));
    }

    /// <summary>
    /// Preview of what <see cref="Replace"/> will touch — every case that currently holds a
    /// byte-copy of this file, with its existing comment/vote counts, so the owner can see the
    /// blast radius before confirming a replace.
    /// </summary>
    [HttpGet("{id:guid}/replace-impact")]
    public async Task<ActionResult<ReplaceImpactRecord>> GetReplaceImpact(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (file is null) return NotFound();
        if (file.AppUserId != userId) return Forbid();

        var copyIds = await db.UploadFiles.AsNoTracking()
            .Where(f => f.CaseCopyOfUploadFileId == id)
            .Select(f => f.Id)
            .ToListAsync(cancellationToken);

        var caseFiles = await db.CaseFiles.AsNoTracking()
            .Where(cf => copyIds.Contains(cf.UploadFileId))
            .Include(cf => cf.Case).ThenInclude(c => c.Organization)
            .ToListAsync(cancellationToken);

        var commentCounts = await db.UploadFileComments.AsNoTracking()
            .Where(c => copyIds.Contains(c.UploadFileId))
            .GroupBy(c => c.UploadFileId)
            .Select(g => new { UploadFileId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UploadFileId, x => x.Count, cancellationToken);

        var voteCounts = await db.EvidenceVotes.AsNoTracking()
            .Where(v => copyIds.Contains(v.UploadFileId))
            .GroupBy(v => v.UploadFileId)
            .Select(g => new { UploadFileId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UploadFileId, x => x.Count, cancellationToken);

        var cases = caseFiles.Select(cf => new ReplaceImpactCaseRecord(
            cf.CaseId, cf.Case.Title, cf.Case.Organization.Name, cf.UploadFileId,
            commentCounts.GetValueOrDefault(cf.UploadFileId), voteCounts.GetValueOrDefault(cf.UploadFileId)
        )).ToList();

        return Ok(new ReplaceImpactRecord(id, file.FileName, cases));
    }
}

public sealed record UpdateUploadFileRequest(
    Guid UploadFileTypeId,
    string? Description,
    bool IsPublic,
    int SortOrder);

public sealed record SaveEditStateRequest(string? EditStateJson);
