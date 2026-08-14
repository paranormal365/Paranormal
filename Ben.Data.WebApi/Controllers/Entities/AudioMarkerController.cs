using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>CRUD for EVP (Electronic Voice Phenomena) markers attached to an UploadFile.</summary>
/// <remarks>
/// Every action requires <see cref="FileAudienceAccess.CanViewFileAsync"/> on the parent file —
/// markers quote timestamps out of the recording, so reading them leaks the content of a private
/// file and writing them defaces someone else's evidence. Mutating an existing marker additionally
/// requires being its author or the file's owner, matching
/// <see cref="UploadFileCommentController"/>'s author-or-owner moderation rule.
/// </remarks>
[ApiController]
[Route("api/upload-files/{fileId:guid}/audio-markers")]
[Authorize]
public sealed class AudioMarkerController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    public AudioMarkerController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper, IAuditLogService auditLog)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AudioMarkerRecord>>> GetAll(
        Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await db.UploadFiles.AnyAsync(f => f.Id == fileId, ct)) return NotFound("File not found.");
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        var markers = await db.AudioMarkers
            .AsNoTracking()
            .Where(m => m.UploadFileId == fileId)
            .OrderBy(m => m.TimeSeconds)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<AudioMarkerRecord>>(markers));
    }

    [HttpGet("{markerId:guid}")]
    public async Task<ActionResult<AudioMarkerRecord>> GetById(
        Guid fileId, Guid markerId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await db.UploadFiles.AnyAsync(f => f.Id == fileId, ct)) return NotFound("File not found.");
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        var marker = await db.AudioMarkers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == markerId && m.UploadFileId == fileId, ct);
        if (marker is null) return NotFound();
        return Ok(_mapper.Map<AudioMarkerRecord>(marker));
    }

    [HttpPost]
    public async Task<ActionResult<AudioMarkerRecord>> Create(
        Guid fileId,
        [FromBody] CreateAudioMarkerRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await db.UploadFiles.AnyAsync(f => f.Id == fileId, ct))
            return NotFound("File not found.");
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        var entity = new AudioMarker
        {
            Id                 = Guid.NewGuid(),
            UploadFileId       = fileId,
            TimeSeconds        = request.TimeSeconds,
            Label              = request.Label,
            ConfidenceLevel    = request.ConfidenceLevel,
            Note               = request.Note,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.AudioMarkers.Add(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(AudioMarker), entity.Id, entity, userId, AppSources.WebApi, ct));
        return CreatedAtAction(nameof(GetById), new { fileId, markerId = entity.Id },
            _mapper.Map<AudioMarkerRecord>(entity));
    }

    [HttpPut("{markerId:guid}")]
    public async Task<ActionResult<AudioMarkerRecord>> Update(
        Guid fileId, Guid markerId,
        [FromBody] UpdateAudioMarkerRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        var before = await db.AudioMarkers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == markerId && m.UploadFileId == fileId, ct);
        if (before is null) return NotFound();
        if (!await CanModifyMarkerAsync(db, before, userId, ct)) return Forbid();

        var entity = await db.AudioMarkers
            .FirstOrDefaultAsync(m => m.Id == markerId && m.UploadFileId == fileId, ct);

        entity!.TimeSeconds        = request.TimeSeconds;
        entity.Label              = request.Label;
        entity.ConfidenceLevel    = request.ConfidenceLevel;
        entity.Note               = request.Note;
        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(AudioMarker), markerId, before, entity, userId, AppSources.WebApi, ct));
        return Ok(_mapper.Map<AudioMarkerRecord>(entity));
    }

    [HttpDelete("{markerId:guid}")]
    public async Task<IActionResult> Delete(Guid fileId, Guid markerId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        var entity = await db.AudioMarkers
            .FirstOrDefaultAsync(m => m.Id == markerId && m.UploadFileId == fileId, ct);
        if (entity is null) return NotFound();
        if (!await CanModifyMarkerAsync(db, entity, userId, ct)) return Forbid();

        db.AudioMarkers.Remove(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(AudioMarker), markerId, entity, userId, AppSources.WebApi, ct));
        return NoContent();
    }

    /// <summary>
    /// True when <paramref name="userId"/> may edit or remove <paramref name="marker"/>: its author,
    /// or the owner of the file it annotates (moderation). Seeing a shared file is enough to *add*
    /// your own markers, but not to rewrite someone else's.
    /// </summary>
    private static async Task<bool> CanModifyMarkerAsync(
        BenDataContext db, AudioMarker marker, Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return false;
        if (marker.CreatedByAppUserId == userId) return true;
        return await db.UploadFiles.AsNoTracking()
            .AnyAsync(f => f.Id == marker.UploadFileId && f.AppUserId == userId, ct);
    }
}
