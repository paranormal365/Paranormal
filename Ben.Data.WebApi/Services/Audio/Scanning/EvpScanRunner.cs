using System.Text.Json;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Audio.Scanning;

/// <summary>
/// Runs one background scan job: each recording in turn, its progress written as it goes
/// (phase 6, 10/26/2026).
/// </summary>
/// <remarks>
/// <para><b>Progress</b> is written at most once a second, from a loop beside the scan rather than
/// from inside it, so a scan never waits on the database to report how far it is.</para>
/// <para><b>One recording failing does not stop the others.</b> Its sentence is kept on its
/// segment, and the job fails only when none of its recordings could be scanned. Anything other
/// than a recording that cannot be read stops the job and is logged.</para>
/// <para><b>Cancel</b> stops at the next piece of audio. Recordings already finished keep their
/// candidates; the one that was running keeps whatever was Pending before, because the old
/// candidates are only replaced once a scan of it completes.</para>
/// <para><b>A cancel somebody asked for is not a shutdown.</b> Both arrive as a canceled token. Only
/// the first (the job's <c>CancelRequestedUtc</c>) ends as Canceled; a shutdown leaves the job
/// running in the table, and the worker's tidy-up on the next start says the server restarted.</para>
/// </remarks>
public sealed class EvpScanRunner(
    IDbContextFactory<BenDataContext> dbContextFactory,
    EvpScanService scanService,
    ILogger<EvpScanRunner> logger)
{
    /// <summary>How often a running scan's progress is written.</summary>
    internal static TimeSpan ProgressInterval { get; set; } = TimeSpan.FromSeconds(1);

    public const string CanceledSentence = "Stopped. Recordings already finished keep their candidates.";
    public const string UnexpectedSentence = "The scan stopped unexpectedly. Start it again; if it happens twice, tell us.";

    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        // Claimed whatever the token says: a moment's work, and a job left half-claimed would
        // neither run nor be tidied up.
        await using var db = await dbContextFactory.CreateDbContextAsync(CancellationToken.None);
        var job = await db.EvpScanJobs.Include(j => j.Segments).FirstOrDefaultAsync(j => j.Id == jobId, CancellationToken.None);
        if (job is null || job.Status != EvpScanJobStatus.Queued) return;   // run, canceled or gone

        job.Status     = EvpScanJobStatus.Running;
        job.StartedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(CancellationToken.None);

        var options = JsonSerializer.Deserialize<EvpDetectionOptions>(job.OptionsJson, EvpScanRecorder.OptionsJson)
                      ?? EvpDetectionOptions.FromSensitivity(job.Sensitivity);
        var canceled = false;

        try
        {
            foreach (var segment in job.Segments.OrderBy(s => s.Order))
            {
                if (await CancelRequestedAsync(jobId))
                {
                    canceled = true;
                    break;
                }
                ct.ThrowIfCancellationRequested();   // shutting down: left running for the next start
                canceled = !await RunSegmentAsync(db, job, segment, options, ct);
                if (canceled) break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "EVP scan job {JobId} stopped unexpectedly", jobId);
            Finish(job, EvpScanJobStatus.Failed, UnexpectedSentence);
            await db.SaveChangesAsync(CancellationToken.None);
            return;
        }

        if (canceled)
            Finish(job, EvpScanJobStatus.Canceled, CanceledSentence);
        else if (job.Segments.Any(s => s.Status == EvpScanJobStatus.Done))
            Finish(job, EvpScanJobStatus.Done, error: null);
        else
            Finish(job, EvpScanJobStatus.Failed,
                job.Segments.Select(s => s.Error).FirstOrDefault(e => e is not null) ?? UnexpectedSentence);

        await db.SaveChangesAsync(CancellationToken.None);
    }

    /// <returns>False when the job was canceled during this recording.</returns>
    private async Task<bool> RunSegmentAsync(
        BenDataContext db, EvpScanJob job, EvpScanJobSegment segment, EvpDetectionOptions options, CancellationToken ct)
    {
        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == segment.UploadFileId, ct);
        if (file is null)
        {
            FinishSegment(segment, EvpScanJobStatus.Failed, "The recording was deleted before it could be scanned.");
            await db.SaveChangesAsync(CancellationToken.None);
            return true;
        }

        segment.Status     = EvpScanJobStatus.Running;
        segment.StartedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        var reporter = new LatestProgress();
        using var stopWriting = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var writing = WriteProgressAsync(job.Id, segment.Id, reporter, stopWriting.Token);

        try
        {
            var (outcome, problem) = await scanService.RunAsync(
                file, job.Sensitivity, options, job.RequestedByAppUserId, isSuperAdmin: false, reporter, ct);

            if (outcome is null)
                FinishSegment(segment, EvpScanJobStatus.Failed, problem);
            else
            {
                segment.CandidateCount   = outcome.Markers.Fresh.Count;
                segment.TopScore         = outcome.TopScore;
                segment.EvpScanId        = outcome.ScanId;
                segment.ProcessedSeconds = reporter.Latest?.ProcessedSeconds ?? segment.ProcessedSeconds;
                segment.SettledSeconds   = segment.ProcessedSeconds;
                segment.TotalSeconds   ??= segment.ProcessedSeconds;
                segment.Percent          = 100;
                FinishSegment(segment, EvpScanJobStatus.Done, error: null);
            }
        }
        catch (OperationCanceledException)
        {
            // Asked for, or the server shutting down? Only the first is this job's to record.
            if (!await CancelRequestedAsync(job.Id)) throw;
            FinishSegment(segment, EvpScanJobStatus.Canceled, CanceledSentence);
            return false;
        }
        catch (AudioDecoderUnavailableException ex)
        {
            FinishSegment(segment, EvpScanJobStatus.Failed, ex.Message);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException || AudioSourceReader.IsUndecodable(ex))
        {
            FinishSegment(segment, EvpScanJobStatus.Failed, $"Couldn't read that audio: {ex.Message}");
        }
        finally
        {
            await stopWriting.CancelAsync();
            try { await writing; } catch (OperationCanceledException) { }
            // The progress writer's context saved behind this one's back; take its numbers as they are.
            job.Percent = JobPercent(job);
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return true;
    }

    /// <summary>Writes the latest progress once a second, on its own context, until told to stop.</summary>
    private async Task WriteProgressAsync(Guid jobId, Guid segmentId, LatestProgress reporter, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            await Task.Delay(ProgressInterval, stop);
            if (reporter.Latest is not { } p) continue;

            try
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(stop);
                var job = await db.EvpScanJobs.Include(j => j.Segments).FirstOrDefaultAsync(j => j.Id == jobId, stop);
                var segment = job?.Segments.FirstOrDefault(s => s.Id == segmentId);
                if (job is null || segment is null || segment.FinishedUtc is not null) return;

                segment.ProcessedSeconds = p.ProcessedSeconds;
                segment.SettledSeconds   = p.SettledSeconds;
                segment.TotalSeconds   ??= p.TotalSeconds;
                segment.Percent          = segment.TotalSeconds is > 0 and var total
                    ? (int)Math.Clamp(Math.Floor(100 * p.ProcessedSeconds / total), 0, 99)
                    : 0;
                job.Percent = JobPercent(job);
                await db.SaveChangesAsync(stop);
            }
            catch (DbUpdateException ex)
            {
                logger.LogWarning(ex, "Progress for EVP scan job {JobId} was not written", jobId);
            }
        }
    }

    /// <summary>The whole job's percent, each recording weighted by its length when that is known.</summary>
    internal static int JobPercent(EvpScanJob job)
    {
        var segments = job.Segments.ToList();
        if (segments.Count == 0) return 0;
        double weight = 0, done = 0;
        foreach (var s in segments)
        {
            var w = s.TotalSeconds is > 0 and var t ? t : 1;
            weight += w;
            done   += w * (s.FinishedUtc is not null ? 100 : s.Percent);
        }
        return (int)Math.Clamp(Math.Floor(done / weight), 0, 100);
    }

    private async Task<bool> CancelRequestedAsync(Guid jobId)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        return await db.EvpScanJobs.AsNoTracking().AnyAsync(j => j.Id == jobId && j.CancelRequestedUtc != null);
    }

    private static void Finish(EvpScanJob job, EvpScanJobStatus status, string? error)
    {
        job.Status      = status;
        job.Error       = error is { Length: > 1000 } long_ ? long_[..1000] : error;
        job.FinishedUtc = DateTime.UtcNow;
        job.Percent     = status == EvpScanJobStatus.Done ? 100 : JobPercent(job);
        foreach (var s in job.Segments.Where(s => s.FinishedUtc is null))
            FinishSegment(s, status == EvpScanJobStatus.Canceled ? EvpScanJobStatus.Canceled : EvpScanJobStatus.Failed,
                          status == EvpScanJobStatus.Canceled ? CanceledSentence : error);
    }

    private static void FinishSegment(EvpScanJobSegment segment, EvpScanJobStatus status, string? error)
    {
        segment.Status      = status;
        segment.Error       = error is { Length: > 1000 } long_ ? long_[..1000] : error;
        segment.FinishedUtc = DateTime.UtcNow;
    }

    /// <summary>Keeps only the newest report; the writer reads it on its own schedule.</summary>
    private sealed class LatestProgress : IProgress<EvpScanProgress>
    {
        private readonly Lock _gate = new();
        private EvpScanProgress? _latest;
        public EvpScanProgress? Latest { get { lock (_gate) return _latest; } }
        public void Report(EvpScanProgress value) { lock (_gate) _latest = value; }
    }
}
