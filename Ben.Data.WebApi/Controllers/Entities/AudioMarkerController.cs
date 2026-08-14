using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>CRUD for EVP (Electronic Voice Phenomena) markers attached to an UploadFile.</summary>
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
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
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
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
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
        var before = await db.AudioMarkers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == markerId && m.UploadFileId == fileId, ct);
        if (before is null) return NotFound();
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
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        var entity = await db.AudioMarkers
            .FirstOrDefaultAsync(m => m.Id == markerId && m.UploadFileId == fileId, ct);
        if (entity is null) return NotFound();
        db.AudioMarkers.Remove(entity);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(AudioMarker), markerId, entity, GetCurrentUserId(), AppSources.WebApi, ct));
        return NoContent();
    }
}
