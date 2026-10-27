using System.Text.Json;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services.Audio.Scanning;
using Ben.Data.WebApi.Services.FieldSessions;
using Ben.Service.Models.Entities;
using Ben.Service.Models.FieldSessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Scanning a field session's recordings for EVPs (phase 9 of the audio plan, 10/27/2026).
/// </summary>
/// <remarks>
/// <para><b>The session is never changed.</b> A scan works on the scanner's own copy of each
/// recording (<see cref="FieldSessionAnalysisCopies"/>), or their group's, and every marker, scan
/// and saved EVP belongs to that copy.</para>
///
/// <para><b>Who may</b> is <see cref="FieldSessionAnalysisAccess"/>: narrower than who may read.
/// Somebody who may not even read the session is told it isn't here, as every other route does,
/// so a scan door says nothing about whether a private session exists.</para>
/// </remarks>
public sealed partial class FieldSessionUploadController
{
    /// <summary>The one call every entrance makes: may this person scan, whose copy, and what is there.</summary>
    [HttpGet("{sessionId:guid}/analysis")]
    public async Task<ActionResult<FieldSessionAnalysisView>> GetAnalysis(
        Guid sessionId, [FromQuery] AnalysisScope? scope, [FromQuery] Guid? organizationId,
        [FromServices] IAudioDecoder decoder, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking().Include(s => s.Files)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var decision = await FieldSessionAnalysisAccess.MayScanAsync(db, session, userId, scope, organizationId, ct);
        if (session is null
            || (decision.Refusal == ScanRefusal.NotYours && !await MayReadAsync(db, session, userId, ct)))
            return NotFound();

        var decoderAvailable = decoder.CanDecode("audio/mp4", "audio.m4a");
        var scopes = await FieldSessionAnalysisAccess.ScopesAsync(db, session, userId, ct);
        var scopeRecords = scopes.Select(s => new AnalysisScopeRecord(s.Scope, s.OrganizationId, s.Label)).ToList();
        var recordings = AudioRecordings(session);

        if (!decision.Allowed)
            return Ok(new FieldSessionAnalysisView(false, decision.Refusal, decision.Message, scopeRecords,
                recordings.Select(r => new AnalysisClipRecord(r.Id, r.RelativePath, r.FileSize, null, 0, 0, null)).ToList(),
                null, decoderAvailable));
        if (recordings.Count == 0)
            return Ok(new FieldSessionAnalysisView(false, ScanRefusal.NoAudio,
                FieldSessionAnalysisAccess.Describe(ScanRefusal.NoAudio), scopeRecords, [], null, decoderAvailable));

        var chosen = decision.Scope!;
        var clips = new List<AnalysisClipRecord>();
        foreach (var recording in recordings)
        {
            var copy = await FieldSessionAnalysisCopies.FindAsync(db, recording.Id, chosen, userId, ct);
            if (copy is null)
            {
                clips.Add(new AnalysisClipRecord(recording.Id, recording.RelativePath, recording.FileSize, null, 0, 0, null));
                continue;
            }
            var markers = db.AudioMarkers.AsNoTracking().Where(m => m.UploadFileId == copy.Id);
            clips.Add(new AnalysisClipRecord(
                recording.Id, recording.RelativePath, recording.FileSize, copy.Id,
                await markers.CountAsync(m => m.ReviewStatus == EvpReviewStatus.Pending, ct),
                await markers.CountAsync(m => m.ReviewStatus == EvpReviewStatus.Confirmed, ct),
                await db.EvpScans.AsNoTracking().Where(s => s.UploadFileId == copy.Id)
                    .MaxAsync(s => (DateTime?)s.DateCreated, ct)));
        }

        var live = await db.EvpScanJobs.AsNoTracking().Include(j => j.Segments)
            .Where(j => j.FieldSessionUploadId == sessionId && j.RequestedByAppUserId == userId && j.FinishedUtc == null)
            .OrderByDescending(j => j.CreatedUtc)
            .FirstOrDefaultAsync(ct);

        // Ordered with the chosen copy first, so a page that shows one label shows the right one.
        scopeRecords = scopeRecords
            .OrderBy(s => s.Scope == chosen.Scope && s.OrganizationId == chosen.OrganizationId ? 0 : 1).ToList();
        return Ok(new FieldSessionAnalysisView(true, ScanRefusal.None, null, scopeRecords, clips,
            live is null ? null : EvpScanJobController.ToRecord(live), decoderAvailable));
    }

    /// <summary>The person's (or group's) copy of one recording, made now if there is none yet.</summary>
    [HttpPost("{sessionId:guid}/clips/{fileId:guid}/analysis-copy")]
    public async Task<ActionResult<AnalysisCopyRecord>> EnsureAnalysisCopy(
        Guid sessionId, Guid fileId, [FromBody] AnalysisCopyRequest? request,
        [FromServices] FieldSessionAnalysisCopies copies, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking().Include(s => s.Files)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var (decision, refused) = await DecideAsync(db, session, userId, request?.Scope, request?.OrganizationId, ct);
        if (refused is not null) return refused;

        var recording = AudioRecordings(session!).FirstOrDefault(f => f.Id == fileId);
        if (recording is null) return NotFound("That recording isn't in this session, or it isn't sound.");

        var outcome = await copies.EnsureAsync(db, session!, recording, decision!.Scope!, userId, ct);
        if (outcome.Copy is not { } copy) return StatusCode(outcome.Status, outcome.Problem);

        var record = new AnalysisCopyRecord(copy.Id, copy.FileName, copy.ContentType, copy.FileSize,
            decision.Scope!.Label, recording.RelativePath, session!.StartedAt, session.LocationLabel);
        return outcome.Created ? StatusCode(StatusCodes.Status201Created, record) : Ok(record);
    }

    /// <summary>Scans the session's recordings in the background: every one, or those named.</summary>
    [HttpPost("{sessionId:guid}/scan-jobs")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Ben.Data.WebApi.Services.RateLimiting.AudioProcessingPolicy)]
    public async Task<ActionResult<EvpScanJobRecord>> StartSessionScan(
        Guid sessionId, [FromBody] StartSessionScanRequest request,
        [FromServices] FieldSessionAnalysisCopies copies, [FromServices] IAudioDecoder decoder,
        [FromServices] EvpScanQueue queue, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!Enum.IsDefined(request.Sensitivity)) return BadRequest("Unknown sensitivity.");
        var settings = request.Options ?? EvpDetectionOptions.FromSensitivity(request.Sensitivity);
        if (settings.Validate() is { } problem) return BadRequest(problem);

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking().Include(s => s.Files)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        var (decision, refused) = await DecideAsync(db, session, userId, request.Scope, request.OrganizationId, ct);
        if (refused is not null) return refused;

        var recordings = AudioRecordings(session!);
        if (request.ClipFileIds is { Count: > 0 } named)
            recordings = recordings.Where(r => named.Contains(r.Id)).ToList();
        if (recordings.Count == 0)
            return BadRequest(new ScanRefusalRecord(nameof(ScanRefusal.NoAudio), FieldSessionAnalysisAccess.Describe(ScanRefusal.NoAudio)));

        // Refused now, before any copy is made, rather than copied and queued to fail.
        var unreadable = recordings.FirstOrDefault(r => !decoder.CanDecode(ContentTypeOf(r), r.RelativePath));
        if (unreadable is not null)
            return BadRequest(new ScanRefusalRecord(ScanRefusalRecord.DecoderUnavailable,
                CompositeAudioDecoder.UnavailableFor(ContentTypeOf(unreadable))));

        if (await db.EvpScanJobs.CountAsync(j => j.RequestedByAppUserId == userId && j.FinishedUtc == null, ct)
            >= EvpScanJobController.MaxLivePerPerson)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                $"You have {EvpScanJobController.MaxLivePerPerson} scans waiting or running. Start another when one of them finishes.");

        var copyIds = new List<Guid>();
        foreach (var recording in recordings)
        {
            var outcome = await copies.EnsureAsync(db, session!, recording, decision!.Scope!, userId, ct);
            if (outcome.Copy is not { } copy) return StatusCode(outcome.Status, outcome.Problem);
            copyIds.Add(copy.Id);
        }

        if (await db.EvpScanJobSegments.AnyAsync(s => copyIds.Contains(s.UploadFileId) && s.FinishedUtc == null, ct))
            return Conflict("A scan of this session's recordings is already running. Wait for it, or stop it first.");

        var job = new EvpScanJob
        {
            Id = Guid.NewGuid(), RequestedByAppUserId = userId, FieldSessionUploadId = sessionId,
            Sensitivity = request.Sensitivity,
            OptionsJson = JsonSerializer.Serialize(settings, EvpScanRecorder.OptionsJson),
            Status = EvpScanJobStatus.Queued, CreatedUtc = DateTime.UtcNow,
        };
        for (var i = 0; i < copyIds.Count; i++)
            job.Segments.Add(new EvpScanJobSegment
            {
                Id = Guid.NewGuid(), UploadFileId = copyIds[i], Order = i, Status = EvpScanJobStatus.Queued,
            });
        db.EvpScanJobs.Add(job);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            return Conflict("A scan of this session's recordings is already running. Wait for it, or stop it first.");
        }

        queue.Signal(job.Id);
        return Accepted($"/api/scan-jobs/{job.Id}", EvpScanJobController.ToRecord(job));
    }

    /// <summary>
    /// EVPs saved from this session's recordings that this person can open.
    /// </summary>
    /// <remarks>
    /// Only those they can open. Each scanner's copy is their own (Ben, 10/26/2026), so another
    /// person's saved EVP is theirs to share, not this list's to announce.
    /// </remarks>
    [HttpGet("{sessionId:guid}/evps")]
    public async Task<ActionResult<IReadOnlyList<SessionEvpRecord>>> GetSessionEvps(Guid sessionId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return NotFound();
        var scopes = await FieldSessionAnalysisAccess.ScopesAsync(db, session, userId, ct);
        if (scopes.Count == 0) return NotFound();

        var clips = await db.UploadFiles.AsNoTracking()
            .Where(f => f.SourceFieldSessionUploadId == sessionId && f.ParentFileId != null && f.ArchivedFromUploadFileId == null)
            .OrderBy(f => f.SourceClipPath).ThenBy(f => f.RegionStart)
            .Select(f => new
            {
                f.Id, f.FileName, f.ContentType, f.SourceClipPath, f.RegionStart, f.RegionEnd, f.DateCreated,
                MadeBy = f.CreatedByAppUser.DisplayName,
            })
            .ToListAsync(ct);

        var visible = new List<SessionEvpRecord>();
        foreach (var c in clips)
            if (await FileAudienceAccess.CanViewFileAsync(db, c.Id, userId, ct))
                visible.Add(new SessionEvpRecord(c.Id, c.FileName, c.ContentType, c.SourceClipPath,
                    c.RegionStart, c.RegionEnd, c.MadeBy ?? "Somebody", c.DateCreated));
        return Ok(visible);
    }

    /// <summary>The scan decision, or the response refusing it.</summary>
    /// <remarks>Somebody who may not read the session is told it isn't here, as every other route says.</remarks>
    private static async Task<(ScanDecision? Decision, ActionResult? Refused)> DecideAsync(
        BenDataContext db, FieldSessionUpload? session, Guid userId, AnalysisScope? scope, Guid? organizationId,
        CancellationToken ct)
    {
        var decision = await FieldSessionAnalysisAccess.MayScanAsync(db, session, userId, scope, organizationId, ct);
        if (decision.Allowed) return (decision, null);
        if (session is null || decision.Refusal == ScanRefusal.NoSuchSession
            || (decision.Refusal == ScanRefusal.NotYours && !await MayReadAsync(db, session, userId, ct)))
            return (null, new NotFoundResult());
        return (null, new ObjectResult(new ScanRefusalRecord(decision.Refusal.ToString(), decision.Message ?? ""))
            { StatusCode = StatusCodes.Status403Forbidden });
    }

    /// <summary>The session's sound recordings that are on the server, in the order they were recorded.</summary>
    private static List<FieldSessionUploadFile> AudioRecordings(FieldSessionUpload session) => session.Files
        .Where(f => (f.UploadFileId is not null || f.BundleEntryPath is { Length: > 0 })
                 && ContentTypeOf(f).StartsWith("audio/", StringComparison.OrdinalIgnoreCase))
        .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
        .ToList();

    private static string ContentTypeOf(FieldSessionUploadFile f)
        => f.ContentType ?? FieldSessionFileGuard.ContentTypeFor(f.RelativePath);
}
