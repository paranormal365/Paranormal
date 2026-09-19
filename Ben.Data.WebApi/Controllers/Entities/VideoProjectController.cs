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
    private readonly ILogger<VideoProjectController>? _logger;

    public VideoProjectController(IDbContextFactory<BenDataContext> db, IMapper mapper,
        IFileStorageService fileStorage, IMediaIngestService mediaIngest,
        ILogger<VideoProjectController>? logger = null)
    {
        _db          = db;
        _mapper      = mapper;
        _fileStorage = fileStorage;
        _mediaIngest = mediaIngest;
        _logger      = logger;
    }

    // GET /api/video-projects[?caseId=...]
    /// <summary>
    /// The caller's own projects, or — with a case — everything on that case.
    /// </summary>
    /// <remarks>
    /// <para>A case project used to be visible only to whoever made it, so the case's own Video
    /// tab showed each member a different list and nobody could pick up anybody else's edit. Help
    /// describes the case tab as shared work (2026-09-05 audit, persistence-14 and site-7).</para>
    ///
    /// <para>Reading is shared; writing is not. Update and Delete stay with the person who made
    /// the project, because a shared list is a way to see and continue somebody's work, not a
    /// licence to overwrite it. Each record carries who made it so the list can say.</para>
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<VideoProjectRecord>>> GetAll(
        [FromQuery] Guid? caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();

        if (caseId.HasValue && !await CanAccessCaseAsync(caseId.Value, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);

        var query = caseId.HasValue
            ? db.VideoProjects.AsNoTracking().Where(p => p.CaseId == caseId.Value)
            : db.VideoProjects.AsNoTracking().Where(p => p.CreatedByAppUserId == userId);

        var entities = await query.OrderByDescending(p => p.DateCreated).ToListAsync(ct);
        var records  = _mapper.Map<IEnumerable<VideoProjectRecord>>(entities).ToList();

        // Names only where the list can hold more than one person's work. One query for all of
        // them rather than one each, because a case with a dozen projects is ordinary.
        if (caseId.HasValue && records.Count > 0)
        {
            var authorIds = records.Select(r => r.CreatedByAppUserId).Distinct().ToList();
            var names = await db.AppUsers.AsNoTracking()
                .Where(u => authorIds.Contains(u.Id))
                .Select(u => new { u.Id, u.DisplayName })
                .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

            records = records
                .Select(r => r with
                {
                    CreatedByName = names.GetValueOrDefault(r.CreatedByAppUserId) ?? "Member",
                })
                .ToList();
        }

        return Ok(records);
    }

    // GET /api/video-projects/{id}
    /// <summary>Opens a project the caller made, or one on a case the caller can reach.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<VideoProjectRecord>> GetById(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.VideoProjects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

        // Not found rather than forbidden for a project that is neither the caller's nor on a case
        // they can reach: the answer to "does this project exist" is not theirs to have.
        if (entity is null) return NotFound();

        if (entity.CreatedByAppUserId != userId
            && !(entity.CaseId is { } onCase && await CanAccessCaseAsync(onCase, ct)))
            return NotFound();

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

        // The published video goes with the project. Deleting a project used to leave its render
        // behind — a file nothing referenced any more, still on disk and still counted against the
        // account's storage, with no way to reach it (2026-09-05 audit, persistence-15).
        await RemovePublishedVideoAsync(db, entity.PublishedUploadFileId, ct);

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

        // A render published to a case now appears on the case's own Files tab. It used to exist
        // only as a column on the project row: the file was written under the case's folder, and
        // then nothing on the case linked to it, so the finished video was invisible to everybody
        // who was not the person who made it (2026-09-05 audit, site-6).
        if (project.CaseId is { } publishedToCase)
        {
            db.CaseFiles.Add(new CaseFile
            {
                Id                 = Guid.NewGuid(),
                CaseId             = publishedToCase,
                UploadFileId       = upload.Id,
                Description        = $"Rendered video from \"{project.Name}\".",
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            });
        }

        // The render this replaces. Publishing twice used to leave the first one orphaned: nothing
        // pointed at it any more, it was unreachable from the interface, and it still took up the
        // account's storage. Somebody iterating on an export could leave a dozen behind
        // (2026-09-05 audit, persistence-15).
        var previousUploadId = project.PublishedUploadFileId;

        project.PublishedUploadFileId = upload.Id;
        project.DateUpdated           = DateTime.UtcNow;
        project.UpdatedByAppUserId    = userId;

        await db.SaveChangesAsync(ct);

        // After the save, so a failure here leaves the new video in place and only the old one
        // behind — the opposite order can delete the previous render and then fail to record the
        // new one, which loses both.
        await RemovePublishedVideoAsync(db, previousUploadId, ct);
        return Ok(_mapper.Map<VideoProjectRecord>(project));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes a published render, from the database and from disk.
    /// </summary>
    /// <remarks>
    /// <para>Best-effort on the file itself: a row removed without its bytes costs disk space,
    /// while bytes removed without the row would leave a record pointing at nothing. The
    /// recoverable failure is the one to prefer.</para>
    ///
    /// <para>Only ever called for a render this project itself published, so there is nothing to
    /// check about who owns it — the caller has already established that.</para>
    /// </remarks>
    private async Task RemovePublishedVideoAsync(
        BenDataContext db, Guid? uploadFileId, CancellationToken ct)
    {
        if (uploadFileId is not { } id) return;

        var upload = await db.UploadFiles.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (upload is null) return;

        var metadata = await db.UploadFileMetadata
            .Where(m => m.UploadFileId == id)
            .ToListAsync(ct);

        // The case link goes with it. A CaseFile row pointing at a deleted upload is a file on the
        // case's Files tab that cannot be opened.
        var caseLinks = await db.CaseFiles.Where(f => f.UploadFileId == id).ToListAsync(ct);

        db.CaseFiles.RemoveRange(caseLinks);
        db.UploadFileMetadata.RemoveRange(metadata);
        db.UploadFiles.Remove(upload);
        await db.SaveChangesAsync(ct);

        if (string.IsNullOrWhiteSpace(upload.StoragePath)) return;

        try { await _fileStorage.DeleteAsync(upload.StoragePath, ct); }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "Removed the record for published video {UploadId} but could not delete {Path}.",
                id, upload.StoragePath);
        }
    }


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
