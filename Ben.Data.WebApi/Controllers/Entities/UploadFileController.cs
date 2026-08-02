using AutoMapper;
using Ben.Data.Common.Helpers;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    public UploadFileController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMapper mapper,
        IFileStorageService fileStorage,
        IAuditLogService auditLog,
        FileMetadataExtractorService metadataExtractor)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _fileStorage = fileStorage;
        _auditLog = auditLog;
        _metadataExtractor = metadataExtractor;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<UploadFileRecord>>> GetAll(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.UploadFiles.AsNoTracking()
            .OrderByDescending(f => f.DateCreated)
            .ToListAsync(cancellationToken);
        return Ok(_mapper.Map<IEnumerable<UploadFileRecord>>(entities));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UploadFileRecord>> GetById(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();
        return Ok(_mapper.Map<UploadFileRecord>(entity));
    }

    [HttpGet("{id:guid}/download")]
    [AllowAnonymous]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.UploadFiles.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (entity is null) return NotFound();

        // Public files are served to anyone; private files require authentication.
        if (!entity.IsPublic && !(User.Identity?.IsAuthenticated ?? false))
            return Unauthorized();

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

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

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

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, cancellationToken);
        var fileBytes    = ms.ToArray();
        var contentType  = file.ContentType;
        var isSvg        = contentType.Contains("svg", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(file.FileName).Equals(".svg", StringComparison.OrdinalIgnoreCase);

        if (isSvg)
        {
            // Normalise content type — some browsers omit or mis-report SVG MIME
            contentType = "image/svg+xml";

            try
            {
                fileBytes = SvgSanitizer.Sanitize(fileBytes);
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
            AppUserId = appUserId,
            FileName = file.FileName,
            StoredFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}",
            ContentType = contentType,
            FileSize = fileBytes.Length,
            FileData = null,   // not stored in DB — written to disk below
            Description = description,
            IsPublic = isPublic,
            SortOrder = 0,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = appUserId
        };

        // Write to disk first; if this throws the DB record is never committed
        var relativePath = _fileStorage.UserFilePath(appUserId, entity.StoredFileName);
        using (var writeStream = new MemoryStream(fileBytes))
            await _fileStorage.WriteAsync(relativePath, writeStream, cancellationToken);
        entity.StoragePath = relativePath;

        db.UploadFiles.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(UploadFile), entity.Id, entity, GetCurrentUserId(), AppSources.WebApi, cancellationToken));

        // Extract and persist metadata — fire-and-forget so upload latency is unaffected
        _ = Task.Run(async () =>
        {
            try
            {
                var meta = _metadataExtractor.Extract(entity.Id, contentType, fileBytes);
                await using var dbMeta = await _dbContextFactory.CreateDbContextAsync(CancellationToken.None);
                dbMeta.UploadFileMetadata.Add(meta);
                await dbMeta.SaveChangesAsync(CancellationToken.None);
            }
            catch { /* extraction is best-effort */ }
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
        var entity = await db.UploadFiles.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        entity!.UploadFileTypeId = request.UploadFileTypeId;
        entity.Description = request.Description;
        entity.IsPublic = request.IsPublic;
        entity.SortOrder = request.SortOrder;
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = request.UpdatedByAppUserId;

        await db.SaveChangesAsync(cancellationToken);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(UploadFile), id, before, entity!, GetCurrentUserId(), AppSources.WebApi, cancellationToken));
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
}

public sealed record UpdateUploadFileRequest(
    Guid UploadFileTypeId,
    string? Description,
    bool IsPublic,
    int SortOrder,
    Guid? UpdatedByAppUserId);
