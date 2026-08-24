using AutoMapper;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.SeedData;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// User-owned video projects. Projects are personal by default; optionally linked to a case
/// via the optional <c>caseId</c> query parameter on POST.
/// POST and PUT bodies are raw <c>ProjectFile</c> JSON (as sent by the Ben.Video editor).
/// </summary>
[ApiController]
[Route("api/video-projects")]
[Authorize]
public sealed class VideoProjectController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;
    private readonly IFileStorageService _fileStorage;

    private readonly IMediaIngestService _mediaIngest;

    public VideoProjectController(IDbContextFactory<BenDataContext> db, IMapper mapper,
        IFileStorageService fileStorage, IMediaIngestService mediaIngest)
    {
        _db          = db;
        _mapper      = mapper;
        _fileStorage = fileStorage;
        _mediaIngest = mediaIngest;
    }

    // GET /api/video-projects[?caseId=...]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<VideoProjectRecord>>> GetAll(
        [FromQuery] Guid? caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var query = db.VideoProjects.AsNoTracking()
            .Where(p => p.CreatedByAppUserId == userId);

        if (caseId.HasValue)
            query = query.Where(p => p.CaseId == caseId.Value);

        var entities = await query.OrderByDescending(p => p.DateCreated).ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<VideoProjectRecord>>(entities));
    }

    // GET /api/video-projects/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VideoProjectRecord>> GetById(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.VideoProjects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id && p.CreatedByAppUserId == userId, ct);
        if (entity is null) return NotFound();
        return Ok(_mapper.Map<VideoProjectRecord>(entity));
    }

    // POST /api/video-projects[?caseId=...]
    // Body: raw ProjectFile JSON (projectName + tracks etc.) as sent by Ben.Video editor
    [HttpPost]
    public async Task<ActionResult<VideoProjectRecord>> Create(
        [FromQuery] Guid? caseId,
        [FromBody] JsonElement body,
        CancellationToken ct)
    {
        if (caseId.HasValue && !await CanAccessCaseAsync(caseId.Value, ct)) return Forbid();

        var userId = GetCurrentUserIdOrThrow();
        var name = body.TryGetProperty("projectName", out var n) ? n.GetString() ?? "Untitled Project" : "Untitled Project";
        var projectJson = body.GetRawText();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = new VideoProject
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            Name               = name,
            ProjectJson        = projectJson,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        db.VideoProjects.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id },
            _mapper.Map<VideoProjectRecord>(entity));
    }

    // PUT /api/video-projects/{id}
    // Body: raw ProjectFile JSON
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<VideoProjectRecord>> Update(
        Guid id, [FromBody] JsonElement body, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var entity = await db.VideoProjects
            .FirstOrDefaultAsync(p => p.Id == id && p.CreatedByAppUserId == userId, ct);
        if (entity is null) return NotFound();

        var name = body.TryGetProperty("projectName", out var n) ? n.GetString() : null;
        entity.Name               = name ?? entity.Name;
        entity.ProjectJson        = body.GetRawText();
        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<VideoProjectRecord>(entity));
    }

    // DELETE /api/video-projects/{id}
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.VideoProjects
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (entity is null) return NotFound();

        if (!isSuperAdmin && entity.CreatedByAppUserId != userId) return Forbid();

        db.VideoProjects.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // POST /api/video-projects/{id}/publish
    // Stores the rendered video as an UploadFile and links it to the project.
    [HttpPost("{id:guid}/publish")]
    [DisableRequestSizeLimit]
    public async Task<ActionResult<VideoProjectRecord>> Publish(
        Guid id, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("File is empty.");

        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var project = await db.VideoProjects
            .FirstOrDefaultAsync(p => p.Id == id && p.CreatedByAppUserId == userId, ct);
        if (project is null) return NotFound();

        var storedName   = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var storagePath  = project.CaseId.HasValue
            ? _fileStorage.CaseFilePath(project.CaseId.Value, storedName)
            : _fileStorage.UserFilePath(userId, storedName);

        // Ben's rule (2026-08-24): strip on ANY upload, keep what came off beside the record.
        var uploadFileId = Guid.NewGuid();
        IngestedMedia ingested;
        try
        {
            ingested = await _mediaIngest.IngestAsync(file, storagePath, uploadFileId, ct);
        }
        catch (UnreadableImageException ex)
        {
            return BadRequest(ex.Message);
        }

        var upload = new UploadFile
        {
            Id                 = uploadFileId,
            UploadFileTypeId   = UploadFileTypeSeeder.PublishedVideoFileTypeId,
            AppUserId          = userId,
            FileName           = file.FileName,
            StoredFileName     = storedName,
            StoragePath        = storagePath,
            ContentType        = ingested.ServedContentType,
            FileSize           = ingested.ServedFileSize,
            IsPublic           = false,
            SortOrder          = 0,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(upload);
        db.UploadFileMetadata.Add(ingested.Metadata);

        project.PublishedUploadFileId = upload.Id;
        project.DateUpdated           = DateTime.UtcNow;
        project.UpdatedByAppUserId    = userId;

        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<VideoProjectRecord>(project));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<bool> CanAccessCaseAsync(Guid caseId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserIdOrNull();
        if (userId is null) return false;

        await using var db = await _db.CreateDbContextAsync(ct);
        var orgId = await db.Cases.AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => (Guid?)c.OrganizationId)
            .FirstOrDefaultAsync(ct);
        if (orgId is null) return false;

        return await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId.Value, ct);
    }
}
