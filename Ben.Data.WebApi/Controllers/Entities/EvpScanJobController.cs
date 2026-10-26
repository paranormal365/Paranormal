using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Audio;
using Ben.Data.WebApi.Services.Audio.Scanning;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Background EVP scans: start one, watch it, stop it (phase 6, 10/26/2026).
/// </summary>
/// <remarks>
/// <para><b>Only the person who started a scan sees it.</b> Anybody else asking about it gets a
/// 404, so a job's existence says nothing about whose recording it is.</para>
/// <para><b>Watching is polling, with an ETag.</b> The editor asks every second and a half; an
/// unchanged answer is a 304 with no body. There is no push channel between the API and the site's
/// host, and a poll also survives a restart of either.</para>
/// <para>The quick scan (<c>AudioMarkerController.Scan</c>) stays for other callers. Both run
/// <see cref="EvpScanService"/>.</para>
/// </remarks>
[ApiController]
[Authorize]
public sealed class EvpScanJobController(
    IDbContextFactory<BenDataContext> dbContextFactory, IAudioDecoder decoder, EvpScanQueue queue) : BenControllerBase
{
    /// <summary>How many unfinished scans one person may have at once.</summary>
    public const int MaxLivePerPerson = 3;

    public const string AlreadyRunningSentence = "A scan of this recording is already running. Wait for it, or stop it first.";

    [HttpPost("api/upload-files/{fileId:guid}/scan-jobs")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Ben.Data.WebApi.Services.RateLimiting.AudioProcessingPolicy)]
    public async Task<ActionResult<EvpScanJobRecord>> Start(Guid fileId, [FromBody] StartScanJobRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!Enum.IsDefined(request.Sensitivity)) return BadRequest("Unknown sensitivity.");

        var settings = request.Options ?? EvpDetectionOptions.FromSensitivity(request.Sensitivity);
        if (settings.Validate() is { } problem) return BadRequest(problem);

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId, ct);
        if (file is null) return NotFound("File not found.");
        if (!await FileAudienceAccess.CanViewFileAsync(db, fileId, userId, ct)) return Forbid();
        if (!(file.ContentType ?? "").StartsWith("audio", StringComparison.OrdinalIgnoreCase))
            return BadRequest("That file isn't audio.");

        // Refused now, in the same shape the quick scan uses, rather than queued to fail.
        if (!decoder.CanDecode(file.ContentType, file.FileName))
            return BadRequest(new ScanRefusalRecord(ScanRefusalRecord.DecoderUnavailable,
                CompositeAudioDecoder.UnavailableFor(file.ContentType)));

        if (await db.EvpScanJobSegments.AnyAsync(s => s.UploadFileId == fileId && s.FinishedUtc == null, ct))
            return Conflict(AlreadyRunningSentence);
        if (await db.EvpScanJobs.CountAsync(j => j.RequestedByAppUserId == userId && j.FinishedUtc == null, ct) >= MaxLivePerPerson)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                $"You have {MaxLivePerPerson} scans waiting or running. Start another when one of them finishes.");

        var job = new EvpScanJob
        {
            Id = Guid.NewGuid(), RequestedByAppUserId = userId, Sensitivity = request.Sensitivity,
            OptionsJson = JsonSerializer.Serialize(settings, EvpScanRecorder.OptionsJson),
            Status = EvpScanJobStatus.Queued, CreatedUtc = DateTime.UtcNow,
        };
        job.Segments.Add(new EvpScanJobSegment
        {
            Id = Guid.NewGuid(), UploadFileId = fileId, Order = 0, Status = EvpScanJobStatus.Queued,
        });
        db.EvpScanJobs.Add(job);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // Two starts at once: the database's one-live-scan-per-recording index refused the second.
            return Conflict(AlreadyRunningSentence);
        }

        queue.Signal(job.Id);
        return Accepted($"/api/scan-jobs/{job.Id}", ToRecord(job));
    }

    [HttpGet("api/scan-jobs/{id:guid}")]
    public async Task<ActionResult<EvpScanJobRecord>> Get(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var job = await db.EvpScanJobs.AsNoTracking().Include(j => j.Segments)
            .FirstOrDefaultAsync(j => j.Id == id && j.RequestedByAppUserId == userId, ct);
        if (job is null) return NotFound("That scan isn't yours, or it finished more than a week ago.");

        var record = ToRecord(job);
        var etag   = ETagFor(record);
        if (Request.Headers.IfNoneMatch.Any(v => v == etag)) return StatusCode(StatusCodes.Status304NotModified);
        Response.Headers.ETag = etag;
        return Ok(record);
    }

    /// <summary>The caller's unfinished scan of this recording, so a reopened editor picks it up; 204 when there is none.</summary>
    [HttpGet("api/upload-files/{fileId:guid}/scan-jobs/live")]
    public async Task<ActionResult<EvpScanJobRecord>> Live(Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var job = await db.EvpScanJobs.AsNoTracking().Include(j => j.Segments)
            .Where(j => j.RequestedByAppUserId == userId && j.FinishedUtc == null
                     && j.Segments.Any(s => s.UploadFileId == fileId))
            .OrderByDescending(j => j.CreatedUtc)
            .FirstOrDefaultAsync(ct);
        return job is null ? NoContent() : Ok(ToRecord(job));
    }

    [HttpDelete("api/scan-jobs/{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var job = await db.EvpScanJobs.Include(j => j.Segments)
            .FirstOrDefaultAsync(j => j.Id == id && j.RequestedByAppUserId == userId, ct);
        if (job is null) return NotFound("That scan isn't yours, or it finished more than a week ago.");
        if (job.FinishedUtc is not null) return NoContent();

        job.CancelRequestedUtc = DateTime.UtcNow;
        if (job.Status == EvpScanJobStatus.Queued)
        {
            // Nothing has started, so it stops now rather than when the worker reaches it.
            var now = DateTime.UtcNow;
            job.Status = EvpScanJobStatus.Canceled; job.Error = EvpScanRunner.CanceledSentence; job.FinishedUtc = now;
            foreach (var s in job.Segments.Where(s => s.FinishedUtc is null))
            {
                s.Status = EvpScanJobStatus.Canceled; s.FinishedUtc = now;
            }
        }
        await db.SaveChangesAsync(ct);
        queue.Cancel(id);
        return NoContent();
    }

    internal static EvpScanJobRecord ToRecord(EvpScanJob job) => new(
        job.Id, job.Status, job.Percent, job.Error,
        job.Segments.OrderBy(s => s.Order).Select(s => new EvpScanSegmentRecord(
            s.UploadFileId, s.Status, s.Percent, s.ProcessedSeconds, s.SettledSeconds, s.TotalSeconds,
            s.CandidateCount, s.TopScore, s.Error)).ToList(),
        job.CreatedUtc, job.StartedUtc, job.FinishedUtc);

    /// <summary>Changes whenever anything the editor draws changes.</summary>
    internal static string ETagFor(EvpScanJobRecord record)
    {
        var text = new StringBuilder($"{record.Status}|{record.Percent}|{record.Error}");
        foreach (var s in record.Segments)
            text.Append($"|{s.Status}|{s.Percent}|{s.ProcessedSeconds:0.##}|{s.SettledSeconds:0.##}|{s.CandidateCount}");
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return $"\"{Convert.ToHexString(hash, 0, 8)}\"";
    }
}
