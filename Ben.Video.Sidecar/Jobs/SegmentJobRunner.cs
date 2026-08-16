using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>
/// Runs one <see cref="SegmentRenderSpec"/> to completion against the real ffmpeg binary — item
/// #38 phase 123 (F). Bounded to <see cref="SidecarOptions.MaxConcurrentJobs"/> concurrent
/// encodes via a singleton semaphore (threat T6: this is a companion to the browser, not a render
/// farm). <see cref="Start"/> returns immediately with a job id; the actual encode runs on a
/// detached background task, polled via <see cref="SegmentJobStore"/>/<c>JobEndpoints</c>.
/// </summary>
public sealed class SegmentJobRunner(
    FfmpegRunner ffmpeg,
    Storage.SidecarPaths paths,
    Storage.SourceCache sources,
    ClipEffectRegistry effectRegistry,
    JobRegistry jobRegistry,
    SegmentJobStore store,
    JobConcurrencyLimiter concurrency,
    Storage.RenderedSegmentStore retainedSegments,
    IOptions<SidecarOptions> options,
    ILogger<SegmentJobRunner> logger)
{
    // Item #70 phase 159 — the encode budget moved out to a shared singleton so every job kind
    // (segment, thumbnails, and phases 160/162's concat/assemble) draws from ONE
    // MaxConcurrentJobs ceiling instead of each runner getting its own.
    private readonly SidecarOptions _options = options.Value;

    public Guid Start(SegmentRenderSpec spec)
    {
        var record = store.Create();
        _ = RunAsync(record, spec); // fire-and-forget — tracked entirely through the store from here
        return record.Id;
    }

    private async Task RunAsync(SegmentJobRecord record, SegmentRenderSpec spec)
    {
        var ct = record.Cts.Token;
        var workDir = System.IO.Path.Combine(paths.JobsDir, $"{record.Id:N}");

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
        sources.MarkInUse(spec.ClipId);
        try
        {
            Directory.CreateDirectory(workDir);

            var inputPath = sources.GetPathIfExists(spec.ClipId, spec.SourceExt);
            if (inputPath is null)
            {
                record.State = JobState.Failed;
                record.ErrorMessage = "Source clip not uploaded — PUT /v1/sources/{clipId} first.";
                return;
            }

            const string outputName = "output.mp4";
            var outputPath = System.IO.Path.Combine(workDir, outputName);

            // -progress pipe:1 -nostats: redirects ffmpeg's machine-readable progress to stdout
            // (what ProgressParser expects) and suppresses the noisy human stats line — prepended
            // here rather than baked into ArgvFactory/ExportArgBuilders, which stay the exact same
            // pure builders RenderWorkerBackend calls and shouldn't grow a sidecar-only concern.
            var builtArgs = ArgvFactory.Build(spec, inputPath, outputName, effectRegistry);
            var args = new List<string>(builtArgs.Length + 3) { "-progress", "pipe:1", "-nostats" };
            args.AddRange(builtArgs);

            var renderedDuration = ComputeRenderedDuration(spec);
            var result = await ffmpeg.RunAsync(
                args, workDir, _options.JobTimeout,
                onStdOutLine: line =>
                {
                    var pct = ProgressParser.TryParsePercent(line, renderedDuration);
                    if (pct is { } p) record.ProgressPercent = p;
                },
                ct: ct);

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
                logger.LogWarning(
                    "Segment job {JobId} failed: {Error}", record.Id, record.ErrorMessage);
                return;
            }

            var info = new FileInfo(outputPath);
            if (!info.Exists)
            {
                record.State = JobState.Failed;
                record.ErrorMessage = "ffmpeg reported success but produced no output file.";
                return;
            }

            record.ResultSizeBytes = info.Length;

            if (spec.Retain)
            {
                // Dual residency (item #70 phase 160): the browser still downloads its own copy
                // from /result, and the sidecar additionally keeps one for later concat/assemble
                // inputs. Retain() MOVES the file out of the soon-to-be-swept job workspace, so
                // ResultPath is repointed at the retained location rather than left dangling.
                try
                {
                    var segmentId = retainedSegments.Retain(outputPath);
                    record.RetainedSegmentId = segmentId;
                    record.ResultPath = retainedSegments.GetPathIfExists(segmentId) ?? outputPath;
                }
                catch (Exception ex)
                {
                    // Retention is an optimization: if it fails, the job still succeeded and the
                    // browser still gets its bytes. Log it and carry on without a retained id, and
                    // the client simply won't have a remote input available later.
                    logger.LogWarning(ex, "Segment job {JobId} produced output but retention failed", record.Id);
                    record.ResultPath = outputPath;
                }
            }
            else
            {
                record.ResultPath = outputPath;
            }

            record.ProgressPercent = 100;
            record.State = JobState.Succeeded;
        }
        catch (Exception ex)
        {
            record.State = JobState.Failed;
            record.ErrorMessage = ex.Message;
            logger.LogError(ex, "Segment job {JobId} threw", record.Id);
        }
        finally
        {
            sources.MarkNotInUse(spec.ClipId);
            concurrency.Release();
            ScheduleRetentionCleanup(record.Id, workDir);
        }
    }

    /// <summary>Post-trim, post-speed wall-clock length of the segment being rendered — the
    /// denominator <see cref="ProgressParser"/> needs to turn an elapsed "time=" into a
    /// percentage. Mirrors the same math <see cref="ArgvFactory"/> uses internally.</summary>
    private static double ComputeRenderedDuration(SegmentRenderSpec spec)
    {
        if (spec.Kind == SegmentKind.Image)
            return spec.Duration > 0 ? spec.Duration : 5.0;

        var end = spec.EndTrim > spec.StartTrim ? spec.EndTrim : spec.Duration;
        var speed = spec.Speed <= 0 ? 1.0 : spec.Speed;
        var trimmed = end - spec.StartTrim;
        return speed > 0 ? trimmed / speed : trimmed;
    }

    /// <summary>Deletes a finished job's workspace (and drops it from <see cref="SegmentJobStore"/>)
    /// after <see cref="SidecarOptions.JobRetention"/> if the browser never calls
    /// <c>DELETE /v1/jobs/{id}</c> itself — otherwise a browser tab that closes mid-job leaks disk
    /// forever. Best-effort: a failed cleanup just waits for the next sidecar restart.</summary>
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
