using Ben.Video.Core.SidecarContracts;
using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>
/// Runs one thumbnail-strip extraction to completion — item #70 phase 159.
///
/// <para><b>Shares <see cref="SegmentJobRunner"/>'s concurrency budget deliberately.</b> This is a
/// real ffmpeg encode (decode + N webp writes), so it belongs under the same
/// <see cref="SidecarOptions.MaxConcurrentJobs"/> ceiling that exists to stop this companion
/// process behaving like a render farm (threat T6). The <i>probe</i> endpoint is the opposite case
/// and deliberately does NOT share it — see <c>ProbeEndpoints</c>.</para>
///
/// <para>Same fire-and-forget/poll lifecycle as segment jobs: <see cref="Start"/> returns a job id
/// immediately, the work runs detached, and the browser polls <c>GET /v1/jobs/{id}</c>. Unlike a
/// segment job it produces N files rather than one, recorded in
/// <see cref="SegmentJobRecord.ResultFileNames"/> for the manifest + per-file result endpoints.</para>
/// </summary>
public sealed class ThumbnailJobRunner(
    FfmpegRunner ffmpeg,
    Storage.SidecarPaths paths,
    Storage.SourceCache sources,
    JobRegistry jobRegistry,
    SegmentJobStore store,
    JobConcurrencyLimiter concurrency,
    IOptions<SidecarOptions> options,
    ILogger<ThumbnailJobRunner> logger)
{
    private readonly SidecarOptions _options = options.Value;

    public Guid Start(ThumbnailJobRequest request)
    {
        var record = store.Create();
        _ = RunAsync(record, request);
        return record.Id;
    }

    private async Task RunAsync(SegmentJobRecord record, ThumbnailJobRequest request)
    {
        var ct = record.Cts.Token;
        var workDir = Path.Combine(paths.JobsDir, $"{record.Id:N}");

        try
        {
            await concurrency.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            record.State = JobState.Failed;
            record.ErrorMessage = "Cancelled before starting.";
            return;
        }

        using var jobScope = jobRegistry.EnterJob();
        sources.MarkInUse(request.ClipId);
        try
        {
            Directory.CreateDirectory(workDir);

            var inputPath = sources.GetPathIfExists(request.ClipId, request.SourceExt);
            if (inputPath is null)
            {
                record.State = JobState.Failed;
                record.ErrorMessage = "Source clip not uploaded — PUT /v1/sources/{clipId} first.";
                return;
            }

            var args = ThumbnailArgvFactory.Build(inputPath, request.Count, request.Duration);
            var result = await ffmpeg.RunAsync(args, workDir, _options.JobTimeout, ct: ct);

            if (ct.IsCancellationRequested)
            {
                record.State = JobState.Failed;
                record.ErrorMessage = "Cancelled.";
                return;
            }

            if (result.TimedOut || result.ExitCode != 0)
            {
                record.State = JobState.Failed;
                record.ErrorMessage = result.TimedOut
                    ? $"Timed out after {_options.JobTimeout}."
                    : $"ffmpeg exit code {result.ExitCode}.";
                logger.LogWarning("Thumbnail job {JobId} failed: {Error}", record.Id, record.ErrorMessage);
                return;
            }

            // A frame very close to end-of-stream can legitimately not get written — the browser's
            // own extractThumbnails skips those rather than failing the batch, and so does this.
            // Only a strip with NOTHING in it is a real failure.
            var produced = new List<string>(request.Count);
            for (var i = 1; i <= request.Count; i++)
            {
                var name = ThumbnailArgvFactory.OutputName(i);
                if (File.Exists(Path.Combine(workDir, name))) produced.Add(name);
            }

            if (produced.Count == 0)
            {
                record.State = JobState.Failed;
                record.ErrorMessage = "ffmpeg reported success but produced no thumbnail files.";
                return;
            }

            record.ResultFileNames = produced;
            record.ResultDirectory = workDir;
            record.ResultSizeBytes = produced.Sum(n => new FileInfo(Path.Combine(workDir, n)).Length);
            record.ProgressPercent = 100;
            record.State = JobState.Succeeded;
        }
        catch (Exception ex)
        {
            record.State = JobState.Failed;
            record.ErrorMessage = ex.Message;
            logger.LogError(ex, "Thumbnail job {JobId} threw", record.Id);
        }
        finally
        {
            sources.MarkNotInUse(request.ClipId);
            concurrency.Release();
            ScheduleRetentionCleanup(record.Id, workDir);
        }
    }

    private void ScheduleRetentionCleanup(Guid jobId, string workDir)
    {
        _ = Task.Delay(_options.JobRetention).ContinueWith(_ =>
        {
            store.Remove(jobId);
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, recursive: true); }
            catch { /* best-effort */ }
        }, TaskScheduler.Default);
    }
}
