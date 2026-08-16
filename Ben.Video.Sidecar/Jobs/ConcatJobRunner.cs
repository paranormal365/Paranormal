using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Services;
using Ben.Video.Sidecar.Storage;
using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>
/// Stream-copy concatenation of retained segments — item #70 phase 160.
///
/// <para>Uses <see cref="ExportArgBuilders.BuildConcatCopyArgs"/>, the <b>same</b> builder the
/// browser's own concat path calls (shared via <c>InternalsVisibleTo</c>), so the two can't drift.
/// No fixture test needed here, unlike the thumbnail argv that only exists in JS.</para>
///
/// <para><b>Pinning is the correctness-critical part.</b> Every input is marked in-use for the
/// whole job, so the LRU can't evict a segment between the existence check and ffmpeg opening it —
/// which would otherwise be a genuine race under quota pressure, and one that would only show up
/// on large timelines.</para>
/// </summary>
public sealed class ConcatJobRunner(
    FfmpegRunner ffmpeg,
    SidecarPaths paths,
    RenderedSegmentStore segments,
    JobRegistry jobRegistry,
    SegmentJobStore store,
    JobConcurrencyLimiter concurrency,
    IOptions<SidecarOptions> options,
    ILogger<ConcatJobRunner> logger)
{
    private readonly SidecarOptions _options = options.Value;

    /// <summary>Ids the caller asked for that aren't retained right now. Checked before the job is
    /// created so the caller gets a synchronous 409 with the list, not an async job failure.</summary>
    public IReadOnlyList<Guid> FindMissing(IReadOnlyList<Guid> segmentIds) =>
        [.. segmentIds.Where(id => !segments.Exists(id))];

    public Guid Start(ConcatJobRequest request)
    {
        var record = store.Create();
        _ = RunAsync(record, request);
        return record.Id;
    }

    private async Task RunAsync(SegmentJobRecord record, ConcatJobRequest request)
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

        // Pin every input up front — see the class remarks for why this can't wait until the
        // ffmpeg process actually opens them.
        foreach (var id in request.SegmentIds) segments.MarkInUse(id);
        try
        {
            Directory.CreateDirectory(workDir);

            // Re-resolve after pinning: an id could have been evicted between the endpoint's
            // pre-check and this point, and a missing input must fail the job cleanly rather than
            // produce a silently short output.
            var inputPaths = new List<string>(request.SegmentIds.Count);
            foreach (var id in request.SegmentIds)
            {
                var path = segments.GetPathIfExists(id);
                if (path is null)
                {
                    record.State = JobState.Failed;
                    record.ErrorMessage = $"Retained segment {id} is no longer available.";
                    return;
                }
                inputPaths.Add(path);
            }

            const string outputName = "output.mp4";
            var outputPath = Path.Combine(workDir, outputName);
            var listPath = Path.Combine(workDir, "concat_list.txt");

            // Absolute paths in the list file (with -safe 0) because the segments live in the
            // shared segments dir, not this job's workspace.
            await File.WriteAllTextAsync(listPath, ExportArgBuilders.BuildConcatListContent(inputPaths), ct);

            var args = new List<string> { "-progress", "pipe:1", "-nostats" };
            args.AddRange(ExportArgBuilders.BuildConcatCopyArgs(listPath, outputName));

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
                logger.LogWarning("Concat job {JobId} failed: {Error}", record.Id, record.ErrorMessage);
                return;
            }

            var info = new FileInfo(outputPath);
            if (!info.Exists)
            {
                record.State = JobState.Failed;
                record.ErrorMessage = "ffmpeg reported success but produced no output file.";
                return;
            }

            record.ResultPath = outputPath;
            record.ResultSizeBytes = info.Length;
            record.ProgressPercent = 100;
            record.State = JobState.Succeeded;
        }
        catch (Exception ex)
        {
            record.State = JobState.Failed;
            record.ErrorMessage = ex.Message;
            logger.LogError(ex, "Concat job {JobId} threw", record.Id);
        }
        finally
        {
            foreach (var id in request.SegmentIds) segments.MarkNotInUse(id);
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
