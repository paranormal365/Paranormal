using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
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
using Ben.Data.WebApi.Services.Access;

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
    // Serving a file is not an API call in the sense the limiter was built for.
    //
    // The global limit (600/min per client) exists to stop somebody hammering expensive or
    // sensitive endpoints. A page of media legitimately asks for dozens of files at once, and the
    // website fetches them on the viewer's behalf — so every visitor's images land in the SAME
    // partition, the site's own address, and a single media library page could exhaust the
    // allowance for everybody. The result was 429s rendered as broken files.
    //
    // These two routes are read-only, already gated by FileAudienceAccess, and serve bytes that
    // are meant to be served. The limiter still covers everything else.
    [Microsoft.AspNetCore.RateLimiting.DisableRateLimiting]
    [HttpGet("{id:guid}/download")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(
        [FromServices] IMediaIngestService mediaIngest, Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        var userId = isAuthenticated ? GetCurrentUserId() : Guid.Empty;
        if (!await FileAudienceAccess.CanViewFileAsync(db, id, userId, cancellationToken))
            return isAuthenticated ? Forbid() : Unauthorized();

        // Prefer the SANITIZED copy, then disk, then FileData for rows not yet migrated. The
        // stripped derivative is what every serve path is supposed to return (see
        // MediaSanitizationService) — this one did not, so a public file handed out its EXIF.
        // ServingPathFor returns the original when no derivative exists, so nothing changes for
        // files that were never sanitized.
        if (!string.IsNullOrEmpty(entity.StoragePath))
        {
            var servingPath = mediaIngest.ServingPathFor(entity.StoragePath);

            // A row whose bytes are gone is NOT FOUND, not a server error. Storage throws
            // FileNotFoundException, which reached the exception handler as a 500 — and a page
            // full of avatars turned one missing file into a wall of them. It happens whenever
            // the database outlives the blobs: a restored database, a cleared dev .uploads, a
            // half-finished migration. The honest answer is 404, and the caller's own
            // broken-image fallback then does its job.
            if (!_fileStorage.Exists(servingPath))
                return NotFound("File data is unavailable.");

            var stream = await _fileStorage.OpenReadAsync(servingPath, cancellationToken);
            // The row's ContentType already describes the SERVED copy — ingest records the
            // derivative's type, not the original's — so it is right for a cleaned JPEG, a
            // remuxed MP4 (item 181) and an unsanitized original alike.
            return File(stream, entity.ContentType, entity.FileName);
        }

        if (entity.FileData is not null)
            return File(entity.FileData, entity.ContentType, entity.FileName);

        return NotFound("File data is unavailable.");
    }

    /// <summary>
    /// A small copy of an image file, for anywhere a picture is shown rather than downloaded.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists.</b> Every <c>&lt;img&gt;</c> on the site pointed at
    /// <c>/download</c>, which serves the original bytes. A group's logo rendered in a 40px avatar
    /// on the browse page pulled the whole upload down the wire — however large it was — and the
    /// browser then threw nearly all of it away. On a page listing twenty groups that is twenty
    /// full-size images to draw twenty thumbnails.</para>
    ///
    /// <para><b>The access check is the same call as <see cref="Download"/>'s</b>, deliberately
    /// not a looser one. A thumbnail is still the picture; making it easier to fetch than the file
    /// it shrinks would be a way around the audience rules, and the two would drift. Same
    /// reasoning as the equipment photo thumbnail route.</para>
    ///
    /// <para><b>Non-images fall through to the real file.</b> The sanitiser returns nothing for a
    /// PDF or an audio file, and a caller asking for a thumbnail of one should get something
    /// usable rather than a 404 — the same behaviour the equipment route settled on.</para>
    ///
    /// <para>Generated on first request when the sibling file is missing, so files uploaded before
    /// the pipeline existed need no backfill.</para>
    /// </remarks>
    [Microsoft.AspNetCore.RateLimiting.DisableRateLimiting]
    [HttpGet("{id:guid}/thumbnail")]
    [AllowAnonymous]
    public async Task<IActionResult> Thumbnail(
        [FromServices] IMediaIngestService mediaIngest, Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;
        var userId = isAuthenticated ? GetCurrentUserId() : Guid.Empty;
        if (!await FileAudienceAccess.CanViewFileAsync(db, id, userId, cancellationToken))
            return isAuthenticated ? Forbid() : Unauthorized();

        // Only disk-backed files can be shrunk; a row still carrying its bytes in FileData has no
        // storage path for the sibling thumbnail to sit beside.
        if (!string.IsNullOrEmpty(entity.StoragePath))
        {
            var thumb = await mediaIngest.OpenThumbnailAsync(entity.StoragePath, cancellationToken);
            if (thumb is not null) return File(thumb, "image/jpeg");
        }

        return await Download(mediaIngest, id, cancellationToken);
    }

    [HttpPost]
    // The framework's own ceiling stays off; the limit that applies is the app-settings one
    // (SiteSettingKeys.UploadMaxFileBytes) enforced below, where the refusal can be a sentence
    // that names the number instead of a bare 413.
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

        // The same configurable limit the chunked path enforces — one number governs both doors.
        var limits = await UploadLimitsReader.ReadAsync(db, cancellationToken);
        if (file.Length > limits.MaxFileBytes)
            return BadRequest($"That file is {file.Length:N0} bytes; the largest allowed upload is {limits.MaxFileBytes:N0} bytes.");

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

        // ── Is it actually the picture it says it is? ────────────────────────
        //
        // The extension check above trusts the NAME, and the content type is whatever the browser
        // guessed from that same name. Neither is evidence. An iPhone photo keeps its HEIC bytes
        // while picking up a .JPG name, passes both checks, is stored, and is later served back
        // as image/jpeg — bytes no browser can decode. The upload reports success and the profile
        // shows "Photo unavailable" with nothing anywhere saying why (Ben, 2026-08-27, uploading
        // IMG_3702.JPG; reproduced exactly with HEIC bytes named .JPG).
        //
        // Checked by SIGNATURE rather than by trying to decode. Decoding sounds stricter and is
        // worse: Skia refuses some perfectly displayable images — a valid 8x8 RGBA PNG among them,
        // found while testing this — so "the decoder disliked it" would reject files browsers
        // render happily. What actually matters is narrower and decidable from the first few
        // bytes: is this one of the raster formats a browser can draw?
        if (!isSvg && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            var signature = new byte[12];
            await using (var head = file.OpenReadStream())
            {
                var read = await head.ReadAtLeastAsync(signature, signature.Length, throwOnEndOfStream: false, cancellationToken);
                if (read < signature.Length) Array.Resize(ref signature, read);
            }

            if (!ImageSignature.IsBrowserDisplayable(signature))
                return BadRequest(
                    $"'{file.FileName}' isn't one of the image formats browsers can show, even "
                    + "though it is named like one. iPhone photos are often HEIC underneath — "
                    + "export or convert it to JPEG and try again.");
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
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFile), entity.Id, entity, GetCurrentUserId(), AppSources.WebApi));

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
        if (!await FileAudienceAccess.CanManageFileAsync(db, before, userId, User.IsInRole(RoleNames.SuperAdmin), cancellationToken))
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
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFile), id, before, entity, GetCurrentUserId(), AppSources.WebApi));
        return Ok(_mapper.Map<UploadFileRecord>(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        // Owner or SuperAdmin, the same gate Update has always had. Until 2026-08-23 this
        // endpoint had NO ownership check at all: any authenticated user could hard-delete
        // anyone's file and its blob — the destructive sibling of the GetAll/Download gaps
        // previously found in this controller family, and strictly worse, because a read leaks
        // and a delete destroys.
        // Owner or SuperAdmin, the same gate Update has always had. Until 2026-08-23 this
        // endpoint had NO ownership check at all: any authenticated user could hard-delete
        // anyone's file and its blob — the destructive sibling of the GetAll/Download gaps
        // previously found in this controller family, and strictly worse, because a read leaks
        // and a delete destroys. Ben's rule, stated the same day: only a file's owner deletes
        // it; an organization excludes a file from ITS collection (unlinking a CaseFile,
        // removing its own OrganizationFile copy) but never reaches the person's original.
        // SuperAdmin stays for moderation — somebody has to be able to remove abuse.
        var userId = GetCurrentUserId();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        if (!await FileAudienceAccess.CanManageFileAsync(db, entity, userId, isSuperAdmin, cancellationToken))
            return Forbid();

        // Item 180 Phase B. A file a group is using is not deleted by this door: the person is
        // asked first whether to pull it back from everywhere (DeleteEverywhere) or hand it over
        // (Reassign). Refusing with the usage rather than silently deleting is the whole point —
        // before this, the delete had no usage check at all and the group's case copies were
        // left pointing at a source that no longer existed. SuperAdmin keeps the plain door for
        // moderation; a group-owned file is deleted by the group through this same door.
        if (!isSuperAdmin && entity.OwnerOrganizationId is null)
        {
            var usage = await BuildUsageAsync(db, entity, cancellationToken);
            if (usage.InUseByAnOrganization)
                return Conflict(usage);
        }

        if (!isSuperAdmin && await IsHeldByAFieldSessionAsync(db, id, cancellationToken))
            return Conflict(HeldBySessionMessage);

        return await HardDeleteAsync(db, entity, cancellationToken);
    }

    /// <summary>
    /// Where this file is in use beyond the owner's own library — what the delete questions are
    /// about. Owner (or the owning group's administrators, or SuperAdmin) only.
    /// </summary>
    [HttpGet("{id:guid}/usage")]
    public async Task<ActionResult<FileUsageRecord>> GetUsage(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (file is null) return NotFound();
        if (!await FileAudienceAccess.CanManageFileAsync(db, file, userId, User.IsInRole(RoleNames.SuperAdmin), cancellationToken))
            return Forbid();

        return Ok(await BuildUsageAsync(db, file, cancellationToken));
    }

    /// <summary>
    /// The first answer: yes, remove it everywhere it is shared — then delete it (item 180 Phase B).
    /// </summary>
    /// <remarks>
    /// <para>Ben's rule: "ask whether they want it removed everywhere it is shared. If yes,
    /// honour it." So every share is ended, every case copy the groups took is removed with its
    /// comments and votes, every copy a group made into its own Files goes, and every place the
    /// original itself is referenced is unlinked. Then the file and its bytes are destroyed.</para>
    ///
    /// <para>References this door does not know how to unlink — a published video, a field
    /// session's recording, an audio marker — make the final delete fail, and the answer is a
    /// 409 saying so rather than a half-done job: everything above has already been removed, the
    /// file itself stays, and the person is told what still holds it.</para>
    /// </remarks>
    [HttpPost("{id:guid}/delete-everywhere")]
    public async Task<ActionResult<DeleteEverywhereResult>> DeleteEverywhere(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();
        if (!await FileAudienceAccess.CanManageFileAsync(db, entity, userId, User.IsInRole(RoleNames.SuperAdmin), cancellationToken))
            return Forbid();

        // Checked FIRST: a refusal after the shares and copies were already gone would be a
        // half-done job dressed as a refusal.
        if (await IsHeldByAFieldSessionAsync(db, id, cancellationToken))
            return Conflict(HeldBySessionMessage);

        var now = DateTime.UtcNow;

        // Shares — both tables, deactivated the way the share doors deactivate them, so the
        // record of who had it survives.
        var shares = await db.UploadFileShares.Where(s => s.UploadFileId == id && s.IsActive).ToListAsync(cancellationToken);
        foreach (var s in shares) { s.IsActive = false; s.RemovedByAppUserId = userId; s.RemovalDate = now; }
        var orgShares = await db.UploadFileOrganizationShares.Where(s => s.UploadFileId == id && s.IsActive).ToListAsync(cancellationToken);
        foreach (var s in orgShares) { s.IsActive = false; s.RemovedByAppUserId = userId; s.RemovalDate = now; }

        // Case copies — their own rows, bytes, comments and votes. The CaseFile rows that point
        // at them are removed explicitly rather than trusted to a cascade, because the in-memory
        // provider the tests run on does not cascade untracked dependents.
        var copies = await db.UploadFiles.Where(f => f.CaseCopyOfUploadFileId == id).ToListAsync(cancellationToken);
        var copyIds = copies.Select(c => c.Id).ToList();
        var copyPaths = copies.Where(c => !string.IsNullOrEmpty(c.StoragePath)).Select(c => c.StoragePath!).ToList();
        db.CaseFiles.RemoveRange(await db.CaseFiles.Where(cf => copyIds.Contains(cf.UploadFileId)).ToListAsync(cancellationToken));
        db.UploadFileComments.RemoveRange(await db.UploadFileComments.Where(c => copyIds.Contains(c.UploadFileId)).ToListAsync(cancellationToken));
        db.EvidenceVotes.RemoveRange(await db.EvidenceVotes.Where(v => copyIds.Contains(v.UploadFileId)).ToListAsync(cancellationToken));
        db.UploadFileMetadata.RemoveRange(await db.UploadFileMetadata.Where(m => copyIds.Contains(m.UploadFileId)).ToListAsync(cancellationToken));
        db.UploadFiles.RemoveRange(copies);

        // Copies a group made into its own Files.
        var groupCopies = await db.OrganizationFiles.Where(f => f.SourceUploadFileId == id).ToListAsync(cancellationToken);
        var groupCopyPaths = groupCopies.Where(c => !string.IsNullOrEmpty(c.StoragePath)).Select(c => c.StoragePath!).ToList();
        db.OrganizationFiles.RemoveRange(groupCopies);

        // Direct references to the original itself.
        var directLinks = 0;
        var caseFiles = await db.CaseFiles.Where(cf => cf.UploadFileId == id).ToListAsync(cancellationToken);
        directLinks += caseFiles.Count; db.CaseFiles.RemoveRange(caseFiles);
        var timeline = await db.CaseTimelineEntryFiles.Where(x => x.UploadFileId == id).ToListAsync(cancellationToken);
        directLinks += timeline.Count; db.CaseTimelineEntryFiles.RemoveRange(timeline);
        var report = await db.CaseReportSectionFiles.Where(x => x.UploadFileId == id).ToListAsync(cancellationToken);
        directLinks += report.Count; db.CaseReportSectionFiles.RemoveRange(report);
        var logos = await db.OrganizationLogos.Where(x => x.UploadFileId == id).ToListAsync(cancellationToken);
        directLinks += logos.Count; db.OrganizationLogos.RemoveRange(logos);
        var ads = await db.OrganizationAds.Where(x => x.ImageUploadFileId == id).ToListAsync(cancellationToken);
        directLinks += ads.Count; foreach (var ad in ads) ad.ImageUploadFileId = null;
        var evidence = await db.EventEvidenceSubmissions.Where(x => x.UploadFileId == id).ToListAsync(cancellationToken);
        directLinks += evidence.Count; db.EventEvidenceSubmissions.RemoveRange(evidence);
        var equipmentPhotos = await db.EquipmentItemPhotos.Where(x => x.UploadFileId == id).ToListAsync(cancellationToken);
        directLinks += equipmentPhotos.Count; db.EquipmentItemPhotos.RemoveRange(equipmentPhotos);
        var requestFiles = await db.ClientRequestFiles.Where(x => x.UploadFileId == id).ToListAsync(cancellationToken);
        directLinks += requestFiles.Count; db.ClientRequestFiles.RemoveRange(requestFiles);

        await db.SaveChangesAsync(cancellationToken);
        foreach (var path in copyPaths.Concat(groupCopyPaths))
        {
            try { await _fileStorage.DeleteAsync(path, cancellationToken); }
            catch (Exception ex) { _logger.LogWarning(ex, "Removed a copy of {FileId} but could not delete {Path}.", id, path); }
        }

        var deleted = await HardDeleteAsync(db, entity, cancellationToken);
        if (deleted is not NoContentResult)
            return Conflict("Everything shared was removed, but something else still holds this file "
                          + "— a published video, a session recording or a marker — so the file itself stays.");

        return Ok(new DeleteEverywhereResult(shares.Count + orgShares.Count, copies.Count, groupCopies.Count, directLinks));
    }

    /// <summary>
    /// The second answer: no, do not pull it back — but I still want it out of my files. The file
    /// and its metadata are handed to the group using it rather than destroyed (item 180 Phase B).
    /// </summary>
    /// <remarks>
    /// <para>Ben's rule: "the file and its EXIF record are reassigned to the organization using
    /// it rather than destroyed — ownership moves to the org, the person stops being the owner,
    /// it leaves their personal files, and it appears only to those with the right permission in
    /// that organization."</para>
    ///
    /// <para>The row keeps its id, so every share, case copy and link the group has keeps
    /// working, and the metadata row (keyed by that id) comes with it untouched. The person's
    /// claim is cleared — <c>AppUserId</c> null, <c>OwnerOrganizationId</c> set — which is what
    /// takes it out of their library and out of every owner gate at once. The group also gets a
    /// copy in its own Files, the way copy-from-user makes one, so the file has a place in the
    /// group's own screens and not only inside whatever case it was attached to.</para>
    ///
    /// <para>The group must actually be using it. Handing a file to a group that has never seen
    /// it is not what this door is for, and would let a person plant a file in a group's library.</para>
    /// </remarks>
    [HttpPost("{id:guid}/reassign")]
    public async Task<ActionResult<UploadFileRecord>> Reassign(Guid id, [FromBody] ReassignUploadFileRequest request, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();
        if (entity.OwnerOrganizationId is not null)
            return Conflict("This file already belongs to a group.");
        if (entity.AppUserId != userId && !User.IsInRole(RoleNames.SuperAdmin))
            return Forbid();

        var usage = await BuildUsageAsync(db, entity, cancellationToken);
        var target = usage.Organizations.FirstOrDefault(o => o.OrganizationId == request.OrganizationId);
        if (target is null)
            return BadRequest("That group is not using this file. A file can only be handed to a group that has it in use.");

        var before = new { entity.AppUserId, entity.OwnerOrganizationId };
        entity.AppUserId = null;
        entity.OwnerOrganizationId = request.OrganizationId;
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;

        // The group's own copy, so the file has a home in its Files rather than only inside a
        // case. Same shape as copy-from-user; unpublished until somebody with the permission
        // publishes it.
        if (!await db.OrganizationFiles.AnyAsync(f => f.OrganizationId == request.OrganizationId && f.SourceUploadFileId == id, cancellationToken))
        {
            Stream? source = null;
            if (!string.IsNullOrEmpty(entity.StoragePath) && _fileStorage.Exists(entity.StoragePath))
                source = await _fileStorage.OpenReadAsync(entity.StoragePath, cancellationToken);
            else if (entity.FileData is { Length: > 0 })
                source = new MemoryStream(entity.FileData);

            if (source is not null)
            {
                var storedName  = $"{Guid.NewGuid():N}{Path.GetExtension(entity.FileName)}";
                var storagePath = _fileStorage.OrgFilePath(request.OrganizationId, storedName);
                await using (source) await _fileStorage.WriteAsync(storagePath, source, cancellationToken);
                db.OrganizationFiles.Add(new OrganizationFile
                {
                    Id = Guid.NewGuid(), OrganizationId = request.OrganizationId,
                    UploadFileTypeId = entity.UploadFileTypeId, FileName = entity.FileName,
                    StoredFileName = storedName, ContentType = entity.ContentType, FileSize = entity.FileSize,
                    StoragePath = storagePath, Description = entity.Description, IsPublic = false,
                    SortOrder = 0, SourceUploadFileId = entity.Id,
                    DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFile), id, before,
            new { entity.AppUserId, entity.OwnerOrganizationId }, userId, AppSources.WebApi));

        return Ok(_mapper.Map<UploadFileRecord>(entity));
    }

    /// <summary>
    /// A Field Kit recording belongs to its session (item 180 Phase B, Ben's question of
    /// 2026-09-04: "This will include FieldKit uploads?"). The session's document names the file
    /// and the readings point into it, so destroying the file alone would leave a session whose
    /// evidence is a hole. There is no door yet for a person to delete a whole session — that is
    /// item 218 — so until there is, the file stays and the person is told why, BEFORE anything
    /// else is removed.
    /// </summary>
    private static async Task<bool> IsHeldByAFieldSessionAsync(BenDataContext db, Guid fileId, CancellationToken ct)
        => await db.FieldSessionUploadFiles.AsNoTracking().AnyAsync(f => f.UploadFileId == fileId, ct)
        || await db.FieldSessionUploads.AsNoTracking().AnyAsync(s => s.DocumentUploadFileId == fileId, ct);

    private const string HeldBySessionMessage =
        "This recording is part of a field session, which holds it. It can be handed to a group, "
        + "but it cannot be destroyed while the session exists.";

    /// <summary>Removes the row, then its bytes — the delete every door ends in.</summary>
    private async Task<IActionResult> HardDeleteAsync(BenDataContext db, UploadFile entity, CancellationToken cancellationToken)
    {
        db.UploadFiles.Remove(entity);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Something with a NoAction key still points here. Say so; the row stays.
            _logger.LogInformation(ex, "Delete of {FileId} refused by the database: still referenced.", entity.Id);
            db.Entry(entity).State = EntityState.Unchanged;
            return Conflict("This file is still held by something that has to let go of it first.");
        }
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(UploadFile), entity.Id, entity, GetCurrentUserId(), AppSources.WebApi));

        // Delete from disk after the DB record is gone
        if (!string.IsNullOrEmpty(entity.StoragePath))
            await _fileStorage.DeleteAsync(entity.StoragePath, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Every group's claim on a file, one row per group, counted rather than listed — see
    /// <see cref="FileUsageOrganizationRecord"/> for why counts.
    /// </summary>
    private static async Task<FileUsageRecord> BuildUsageAsync(BenDataContext db, UploadFile file, CancellationToken ct)
    {
        var id = file.Id;
        var claims = new Dictionary<Guid, (int Shares, int CaseCopies, int GroupCopies, int Direct)>();
        void Add(Guid org, int shares = 0, int caseCopies = 0, int groupCopies = 0, int direct = 0)
        {
            var c = claims.GetValueOrDefault(org);
            claims[org] = (c.Shares + shares, c.CaseCopies + caseCopies, c.GroupCopies + groupCopies, c.Direct + direct);
        }

        foreach (var org in await db.UploadFileShares.AsNoTracking()
                     .Where(s => s.UploadFileId == id && s.IsActive && s.TargetType == ShareTargetType.Organization && s.TargetOrganizationId != null)
                     .Select(s => s.TargetOrganizationId!.Value).ToListAsync(ct))
            Add(org, shares: 1);
        foreach (var org in await db.UploadFileOrganizationShares.AsNoTracking()
                     .Where(s => s.UploadFileId == id && s.IsActive).Select(s => s.OrganizationId).ToListAsync(ct))
            Add(org, shares: 1);

        var copyIds = await db.UploadFiles.AsNoTracking().Where(f => f.CaseCopyOfUploadFileId == id).Select(f => f.Id).ToListAsync(ct);
        foreach (var org in await db.CaseFiles.AsNoTracking()
                     .Where(cf => copyIds.Contains(cf.UploadFileId)).Select(cf => cf.Case.OrganizationId).ToListAsync(ct))
            Add(org, caseCopies: 1);

        foreach (var org in await db.OrganizationFiles.AsNoTracking()
                     .Where(f => f.SourceUploadFileId == id).Select(f => f.OrganizationId).ToListAsync(ct))
            Add(org, groupCopies: 1);

        foreach (var org in await db.CaseFiles.AsNoTracking().Where(cf => cf.UploadFileId == id).Select(cf => cf.Case.OrganizationId).ToListAsync(ct)) Add(org, direct: 1);
        foreach (var org in await db.CaseTimelineEntryFiles.AsNoTracking().Where(x => x.UploadFileId == id).Select(x => x.CaseTimelineEntry.Case.OrganizationId).ToListAsync(ct)) Add(org, direct: 1);
        // No navigation from the section file to its section, so the path to the group is joined by hand.
        foreach (var org in await db.CaseReportSectionFiles.AsNoTracking().Where(x => x.UploadFileId == id)
                     .Join(db.CaseReportSections.AsNoTracking(), f => f.CaseReportSectionId, sec => sec.Id, (f, sec) => sec.CaseReportId)
                     .Join(db.CaseReports.AsNoTracking(), reportId => reportId, r => r.Id, (reportId, r) => r.CaseId)
                     .Join(db.Cases.AsNoTracking(), caseId => caseId, c => c.Id, (caseId, c) => c.OrganizationId)
                     .ToListAsync(ct)) Add(org, direct: 1);
        foreach (var org in await db.OrganizationLogos.AsNoTracking().Where(x => x.UploadFileId == id).Select(x => x.OrganizationId).ToListAsync(ct)) Add(org, direct: 1);
        foreach (var org in await db.OrganizationAds.AsNoTracking().Where(x => x.ImageUploadFileId == id).Select(x => x.OrganizationId).ToListAsync(ct)) Add(org, direct: 1);
        foreach (var org in await db.EventEvidenceSubmissions.AsNoTracking().Where(x => x.UploadFileId == id).Select(x => x.OrgCalendarEvent.OrganizationId).ToListAsync(ct)) Add(org, direct: 1);
        foreach (var org in await db.EquipmentItemPhotos.AsNoTracking()
                     .Where(x => x.UploadFileId == id && x.EquipmentItem.OwningOrganizationId != null)
                     .Select(x => x.EquipmentItem.OwningOrganizationId!.Value).ToListAsync(ct)) Add(org, direct: 1);

        // A Field Kit session attached to a group's investigation: the group is using every
        // recording in it, and the session document itself.
        var sessionIds = await db.FieldSessionUploadFiles.AsNoTracking().Where(f => f.UploadFileId == id)
            .Select(f => f.FieldSessionUploadId).ToListAsync(ct);
        foreach (var org in await db.FieldSessionUploads.AsNoTracking()
                     .Where(s => (sessionIds.Contains(s.Id) || s.DocumentUploadFileId == id) && s.InvestigationId != null)
                     .Join(db.Investigations.AsNoTracking(), s => s.InvestigationId!.Value, i => i.Id, (s, i) => i.OrganizationId)
                     .ToListAsync(ct)) Add(org, direct: 1);

        var orgIds = claims.Keys.ToList();
        var names = await db.Organizations.AsNoTracking().Where(o => orgIds.Contains(o.Id))
            .Select(o => new { o.Id, o.Name }).ToDictionaryAsync(o => o.Id, o => o.Name, ct);

        var personShares = await db.UploadFileShares.AsNoTracking()
            .CountAsync(s => s.UploadFileId == id && s.IsActive && s.TargetType == ShareTargetType.Person, ct);
        var isPublic = file.IsPublic || await db.UploadFileShares.AsNoTracking()
            .AnyAsync(s => s.UploadFileId == id && s.IsActive && s.TargetType == ShareTargetType.Public, ct);

        var organizations = claims
            .Select(kv => new FileUsageOrganizationRecord(kv.Key, names.GetValueOrDefault(kv.Key, "A group"),
                kv.Value.Shares, kv.Value.CaseCopies, kv.Value.GroupCopies, kv.Value.Direct))
            .OrderByDescending(o => o.Total).ThenBy(o => o.OrganizationName)
            .ToList();

        return new FileUsageRecord(id, file.FileName, personShares, isPublic, organizations);
    }

    /// <summary>Returns all child clip files that were derived from this file via the region-clip workflow.</summary>
    /// <remarks>
    /// Gated on the PARENT, which had no check at all: any authenticated caller could hand over any
    /// file id and read back the names, descriptions and ids of every clip cut from it, whoever cut
    /// them (2026-09-06 audio walk, finding 16). The children are not gated individually on purpose
    /// — a clip is audio taken out of a recording the caller can already hear, so the parent's
    /// visibility is the honest boundary, and checking each child would be a per-clip round trip
    /// down a list the Saved Clips panel loads on every open.
    /// </remarks>
    [HttpGet("{id:guid}/clips")]
    public async Task<ActionResult<IEnumerable<UploadFileRecord>>> GetChildClips(Guid id, CancellationToken cancellationToken)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.UploadFiles.AnyAsync(f => f.Id == id, cancellationToken)) return NotFound();
        if (!await FileAudienceAccess.CanViewFileAsync(db, id, userId, cancellationToken)) return Forbid();

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
        if (!await FileAudienceAccess.CanManageFileAsync(db, before, userId, User.IsInRole(RoleNames.SuperAdmin), cancellationToken))
            return Forbid();

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
        var newStoragePath = entity.OwnerOrganizationId is { } ownerOrg
            ? _fileStorage.OrgFilePath(ownerOrg, newStoredName)
            : _fileStorage.UserFilePath(entity.AppUserId ?? entity.CreatedByAppUserId, newStoredName);
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
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFile), id, before, entity, userId, AppSources.WebApi));

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
        if (!await FileAudienceAccess.CanManageFileAsync(db, file, userId, User.IsInRole(RoleNames.SuperAdmin), cancellationToken))
            return Forbid();

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
