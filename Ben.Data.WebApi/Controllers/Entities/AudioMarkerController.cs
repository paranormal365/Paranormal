using AutoMapper;
using Ben.Data.Common.Enums;
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
    /// <summary>
    /// Ceiling on one scan's proposals. A detector set too sensitive on a noisy recording can
    /// produce thousands of runs, and nobody reviews a queue that long — better to reject the scan
    /// than to bury the file's real markers under it.
    /// </summary>
    public const int MaxCandidatesPerScan = 500;

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
            EndSeconds         = request.EndSeconds,
            Label              = request.Label,
            ConfidenceLevel    = request.ConfidenceLevel,
            Note               = request.Note,
            ReviewStatus       = EvpReviewStatus.Confirmed,   // a person placed it
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
        entity.EndSeconds         = request.EndSeconds;
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
    /// Replaces this file's pending candidates with the results of a fresh scan.
    /// </summary>
    /// <remarks>
    /// <para><b>Only Pending markers are replaced.</b> Confirmed markers are findings someone stands
    /// behind and Dismissed ones are the record of what a reviewer already rejected — wiping either
    /// would make a re-scan resurrect everything the reviewer had worked through. Deleting and
    /// inserting run in one transaction so a failure can't leave the file showing half of two
    /// different scans.</para>
    /// <para>Candidates are capped at <see cref="MaxCandidatesPerScan"/>. A detector tuned too
    /// sensitive can propose thousands of runs on a noisy recording, and a review queue that long is
    /// not reviewable — the client is expected to keep the highest-scoring ones.</para>
    /// </remarks>
    [HttpPost("candidates")]
    public async Task<ActionResult<IEnumerable<AudioMarkerRecord>>> ReplaceCandidates(
        Guid fileId,
        [FromBody] BulkCreateAudioCandidatesRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var candidates = request.Candidates ?? [];
        if (candidates.Count > MaxCandidatesPerScan)
            return BadRequest($"A scan may propose at most {MaxCandidatesPerScan} candidates; got {candidates.Count}.");
        if (candidates.Any(c => c.EndSeconds <= c.StartSeconds))
            return BadRequest("Every candidate must end after it starts.");
        if (candidates.Any(c => c.StartSeconds < 0))
            return BadRequest("A candidate cannot start before the recording does.");
        if (candidates.Any(c => c.Score is < 0 or > 100 or float.NaN))
            return BadRequest("Candidate scores must be between 0 and 100.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await db.UploadFiles.AnyAsync(f => f.Id == fileId, ct)) return NotFound("File not found.");
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        var now = DateTime.UtcNow;
        var stale = await db.AudioMarkers
            .Where(m => m.UploadFileId == fileId && m.ReviewStatus == EvpReviewStatus.Pending)
            .ToListAsync(ct);

        var fresh = candidates
            .OrderBy(c => c.StartSeconds)
            .Select(c => new AudioMarker
            {
                Id                 = Guid.NewGuid(),
                UploadFileId       = fileId,
                TimeSeconds        = c.StartSeconds,
                EndSeconds         = c.EndSeconds,
                // Deliberately not "Possible EVP": the detector found signal standing out from the
                // noise floor, which is a different claim from a voice being present.
                Label              = "Detected signal",
                ConfidenceLevel    = EvpConfidenceLevel.Possible,
                IsAutoDetected     = true,
                DetectionScore     = c.Score,
                ReviewStatus       = EvpReviewStatus.Pending,
                DateCreated        = now,
                CreatedByAppUserId = userId,
            })
            .ToList();

        // The in-memory provider used by tests doesn't support transactions, so skip it there
        // rather than fail every test — same accommodation as CaseController.
        var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await using (transaction)
        {
            db.AudioMarkers.RemoveRange(stale);
            db.AudioMarkers.AddRange(fresh);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
        }

        _ = TryAuditAsync(_auditLog.LogCreateAsync(
            nameof(AudioMarker), fileId,
            new { Replaced = stale.Count, Created = fresh.Count },
            userId, AppSources.WebApi, ct));

        return Ok(_mapper.Map<IEnumerable<AudioMarkerRecord>>(fresh));
    }

    /// <summary>
    /// Records a person's decision on a candidate: keep it (optionally relabelled and with its
    /// bounds nudged) or reject it.
    /// </summary>
    /// <remarks>
    /// Confirming is a field update on the candidate rather than a copy into a new marker, so the
    /// detector's score and span survive alongside the reviewer's label — you can still see what
    /// the machine thought of something a person signed off on. Dismissed rows are kept, not
    /// deleted, so the next scan has something to dedupe against.
    /// </remarks>
    [HttpPut("{markerId:guid}/review")]
    public async Task<ActionResult<AudioMarkerRecord>> Review(
        Guid fileId, Guid markerId,
        [FromBody] ReviewAudioMarkerRequest request,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (request.ReviewStatus is not (EvpReviewStatus.Confirmed or EvpReviewStatus.Dismissed))
            return BadRequest("A review must confirm or dismiss; Pending is not a decision.");

        var start = request.StartSeconds;
        var end   = request.EndSeconds;
        if (start is not null && start < 0)
            return BadRequest("A marker cannot start before the recording does.");
        if (start is not null && end is not null && end <= start)
            return BadRequest("A span must end after it starts.");

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        var before = await db.AudioMarkers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == markerId && m.UploadFileId == fileId, ct);
        if (before is null) return NotFound();
        if (!await CanModifyMarkerAsync(db, before, userId, ct)) return Forbid();

        var entity = await db.AudioMarkers
            .FirstAsync(m => m.Id == markerId && m.UploadFileId == fileId, ct);

        entity.ReviewStatus = request.ReviewStatus;
        if (start is not null) entity.TimeSeconds = start.Value;
        if (end   is not null) entity.EndSeconds  = end;

        if (request.ReviewStatus == EvpReviewStatus.Confirmed)
        {
            if (!string.IsNullOrWhiteSpace(request.Label)) entity.Label = request.Label.Trim();
            if (request.ConfidenceLevel is { } confidence) entity.ConfidenceLevel = confidence;
            if (request.Note is not null) entity.Note = request.Note;
        }

        entity.DateUpdated        = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(AudioMarker), markerId, before, entity, userId, AppSources.WebApi, ct));
        return Ok(_mapper.Map<AudioMarkerRecord>(entity));
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
