using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services.Access;

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
    /// <summary>Ceiling on one scan's proposals — see <see cref="EvpScanRecorder.MaxCandidatesPerScan"/>.</summary>
    public const int MaxCandidatesPerScan = EvpScanRecorder.MaxCandidatesPerScan;

    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;
    private readonly EvpScanRecorder _scanRecorder;
    private readonly Ben.Data.WebApi.Services.Audio.Scanning.EvpScanService _scanService;

    public AudioMarkerController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMapper mapper,
        IAuditLogService auditLog,
        EvpScanRecorder scanRecorder,
        Ben.Data.WebApi.Services.Audio.Scanning.EvpScanService scanService)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
        _auditLog = auditLog;
        _scanRecorder = scanRecorder;
        _scanService = scanService;
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

        // The same check Review and Candidates already made. Create and Update did not, so an
        // inverted span, a negative start or a label past the column's 200 characters went straight
        // to SaveChanges — the last of which throws rather than answering (2026-09-06 audio walk,
        // finding 8).
        if (AudioRequestLimits.MarkerSpanProblem(request.TimeSeconds, request.EndSeconds, request.Label)
            is { } problem) return BadRequest(problem);

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
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(AudioMarker), entity.Id, entity, userId, AppSources.WebApi));
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

        if (AudioRequestLimits.MarkerSpanProblem(request.TimeSeconds, request.EndSeconds, request.Label)
            is { } problem) return BadRequest(problem);

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
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(AudioMarker), markerId, before, entity, userId, AppSources.WebApi));
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
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(AudioMarker), markerId, entity, userId, AppSources.WebApi));
        return NoContent();
    }

    /// <summary>
    /// Replaces this file's pending candidates with the results of a fresh scan.
    /// </summary>
    /// <remarks>
    /// <para>The rules — only Pending markers replaced, one transaction — live in
    /// <see cref="EvpScanRecorder.ReplacePendingAsync"/>, so a scan made here and a scan made
    /// anywhere else propose candidates the same way.</para>
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
        if (EvpScanRecorder.CandidateProblem(candidates) is { } problem) return BadRequest(problem);

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);
        if (!await db.UploadFiles.AnyAsync(f => f.Id == fileId, ct)) return NotFound("File not found.");
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();

        var replaced = await _scanRecorder.ReplacePendingAsync(db, fileId, candidates, userId, ct);
        AuditReplaced(fileId, replaced, userId);
        return Ok(_mapper.Map<IEnumerable<AudioMarkerRecord>>(replaced.Fresh));
    }

    /// <summary>
    /// Runs the detector over the stored audio and replaces this file's pending candidates with
    /// what it finds.
    /// </summary>
    /// <remarks>
    /// <para>Anything overlapping a marker the reviewer has already ruled on — confirmed or
    /// dismissed — is dropped before saving (<see cref="EvpScanRecorder.ProposeAsync"/>).</para>
    /// <para>Reads through <see cref="IAudioDecoder"/>, so the phone's .m4a scans as well as MP3
    /// and WAV. A host with no ffmpeg answers 400 with a <see cref="ScanRefusalRecord"/> naming
    /// that, rather than blaming the recording.</para>
    /// <para>Runs synchronously. Detection is a couple of passes over the samples, so a long
    /// recording costs seconds rather than minutes; if that stops being true this wants moving to
    /// the background-render path rather than a longer request timeout.</para>
    /// </remarks>
    /// <param name="fileId">The audio file to scan.</param>
    /// <param name="sensitivity">Preset starting point for the thresholds.</param>
    /// <param name="options">
    /// Optional per-scan overrides. Omitted values fall back to <paramref name="sensitivity"/>'s
    /// preset, so a caller can adjust just the tolerance and leave the rest alone.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("scan")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Ben.Data.WebApi.Services.RateLimiting.AudioProcessingPolicy)]
    public async Task<ActionResult<IEnumerable<AudioMarkerRecord>>> Scan(
        Guid fileId,
        [FromQuery] EvpSensitivity sensitivity,
        [FromBody] EvpDetectionOptions? options,
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!Enum.IsDefined(sensitivity)) return BadRequest("Unknown sensitivity.");

        var settings = options ?? EvpDetectionOptions.FromSensitivity(sensitivity);
        if (settings.Validate() is { } problem) return BadRequest(problem);

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var source = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (source is null) return NotFound("File not found.");
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();
        if (!(source.ContentType ?? "").StartsWith("audio", StringComparison.OrdinalIgnoreCase))
            return BadRequest("That file isn't audio.");

        Ben.Data.WebApi.Services.Audio.Scanning.EvpScanOutcome? proposed;
        string? refusal;
        try
        {
            (proposed, refusal) = await _scanService.RunAsync(
                source, sensitivity, settings, userId,
                User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin), progress: null, ct);
        }
        catch (AudioDecoderUnavailableException ex)
        {
            // This server's shortcoming, not the file's — said so, in a shape the editor can tell apart.
            return BadRequest(new ScanRefusalRecord(ScanRefusalRecord.DecoderUnavailable, ex.Message));
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or FormatException
                                   || AudioSourceReader.IsUndecodable(ex))
        {
            // A file the decoder can't open is a bad request about this file, not a server fault.
            return BadRequest($"Couldn't read that audio: {ex.Message}");
        }
        if (proposed is null) return BadRequest(refusal);

        AuditReplaced(fileId, proposed.Markers, userId);
        return Ok(_mapper.Map<IEnumerable<AudioMarkerRecord>>(proposed.Markers.Fresh));
    }

    private void AuditReplaced(Guid fileId, EvpScanRecorder.Replaced replaced, Guid userId)
        => _ = TryAuditAsync(_auditLog.LogCreateAsync(
            nameof(AudioMarker), fileId,
            new { Replaced = replaced.ReplacedCount, Created = replaced.Fresh.Count },
            userId, AppSources.WebApi));

    /// <summary>
    /// Keeps one Keep or Dismiss on a detected candidate (item 253). Like the scan's own record
    /// (<see cref="EvpScanRecorder"/>), it never fails the review it follows.
    /// </summary>
    private async Task TryRecordRulingAsync(
        AudioMarker before, AudioMarker after, bool? playedFirst, CancellationToken ct)
    {
        if (!before.IsAutoDetected) return;   // a hand-placed marker is not a ruling on the detector
        try
        {
            await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

            // Edges are moved through Update before the ruling, not by it, so compare with what
            // the detector proposed rather than with the marker a moment ago.
            var proposedSpan = await db.EvpScanCandidates.AsNoTracking()
                .Where(c => c.AudioMarkerId == after.Id)
                .Select(c => new { c.StartSeconds, c.EndSeconds })
                .FirstOrDefaultAsync(ct);
            var boundsAdjusted = proposedSpan is not null
                ? proposedSpan.StartSeconds != after.TimeSeconds || proposedSpan.EndSeconds != after.EndSeconds
                : before.TimeSeconds != after.TimeSeconds || before.EndSeconds != after.EndSeconds;

            db.EvpRulings.Add(new EvpRuling
            {
                Id             = Guid.NewGuid(),
                UploadFileId   = after.UploadFileId,
                AudioMarkerId  = after.Id,
                Ruling         = after.ReviewStatus,
                PlayedFirst    = playedFirst,
                BoundsAdjusted = boundsAdjusted,
                DetectionScore = after.DetectionScore,
                DateCreated    = DateTime.UtcNow,
            });
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLearningFailure(ex, "ruling", after.UploadFileId);
        }
    }

    private void LogLearningFailure(Exception ex, string what, Guid fileId)
        => HttpContext?.RequestServices?
            .GetService<ILogger<AudioMarkerController>>()?
            .LogError(ex, "EVP learning record ({What}) not written for file {FileId}", what, fileId);

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
        if (AudioRequestLimits.MarkerSpanProblem(start, end, request.Label) is { } spanProblem)
            return BadRequest(spanProblem);

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
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(AudioMarker), markerId, before, entity, userId, AppSources.WebApi));
        await TryRecordRulingAsync(before, entity, request.PlayedFirst, ct);
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
