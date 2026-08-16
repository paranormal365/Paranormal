using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Services;
using Ben.Video.Sidecar.Storage;
using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>
/// Assembles a finished export body from retained segments — item #70 phase 162.
///
/// <para>Runs concat and (optionally) the audio mix as <b>one job producing one result</b>.
/// Splitting them into two jobs would mean downloading the large intermediate concat output and
/// re-uploading it, costing more than the offload saves — combining is the whole reason this job
/// type exists rather than reusing <see cref="ConcatJobRunner"/> twice.</para>
///
/// <para>Every argv comes from <see cref="ExportArgBuilders"/> — the same builders the browser
/// calls, shared via <c>InternalsVisibleTo</c>. The mix in particular uses
/// <see cref="ExportArgBuilders.BuildAmixArgs"/>, extracted from <c>ExportService</c> in this same
/// phase specifically so the two processes cannot produce different audio.</para>
/// </summary>
public sealed class ExportAssembleJobRunner(
    FfmpegRunner ffmpeg,
    SidecarPaths paths,
    RenderedSegmentStore segments,
    SourceCache sources,
    JobRegistry jobRegistry,
    SegmentJobStore store,
    JobConcurrencyLimiter concurrency,
    IOptions<SidecarOptions> options,
    ILogger<ExportAssembleJobRunner> logger)
{
    private readonly SidecarOptions _options = options.Value;

    public IReadOnlyList<Guid> FindMissingSegments(IReadOnlyList<Guid> segmentIds) =>
        [.. segmentIds.Where(id => !segments.Exists(id))];

    /// <summary>Audio clips whose source hasn't been uploaded. Reported up front so the client can
    /// upload them and retry rather than discovering it as a mid-job failure.</summary>
    public IReadOnlyList<Guid> FindMissingAudioSources(ExportAudioMixDto? audio) =>
        audio is null ? [] : [.. audio.Clips
            .Where(c => sources.GetPathIfExists(c.ClipId, c.SourceExt) is null)
            .Select(c => c.ClipId)];

    public Guid Start(ExportAssembleRequest request)
    {
        var record = store.Create();
        _ = RunAsync(record, request);
        return record.Id;
    }

    private async Task RunAsync(SegmentJobRecord record, ExportAssembleRequest request)
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

        // Pin every input for the whole job — same race as ConcatJobRunner: the LRU must not be
        // able to evict a segment between the pre-check and ffmpeg opening it.
        foreach (var id in request.SegmentIds) segments.MarkInUse(id);
        foreach (var clip in request.Audio?.Clips ?? []) sources.MarkInUse(clip.ClipId);
        try
        {
            Directory.CreateDirectory(workDir);
            var settings = ArgvFactory.ToExportSettings(request.Quality);

            // ── Step 1: concat the retained segments ────────────────────────
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

            var listPath = Path.Combine(workDir, "concat_list.txt");
            await File.WriteAllTextAsync(listPath, ExportArgBuilders.BuildConcatListContent(inputPaths), ct);

            const string concatName = "concat.mp4";
            if (!await RunStepAsync(record, workDir,
                    ExportArgBuilders.BuildConcatEncodeArgs(listPath, concatName, settings),
                    concatName, "concat", ct))
            {
                return;
            }
            record.ProgressPercent = request.Audio is null ? 100 : 50;

            var producedName = concatName;

            // ── Step 2 (optional): per-clip audio segments, then the mix ────
            if (request.Audio is { Clips.Count: > 0 })
            {
                var audioSegments = new List<string>(request.Audio.Clips.Count);
                var index = 0;
                foreach (var clip in request.Audio.Clips)
                {
                    var sourcePath = sources.GetPathIfExists(clip.ClipId, clip.SourceExt);
                    if (sourcePath is null)
                    {
                        record.State = JobState.Failed;
                        record.ErrorMessage = $"Audio source {clip.ClipId} is no longer available.";
                        return;
                    }

                    var segName = $"audio_seg_{index:D3}.mp4";
                    var args = ExportArgBuilders.BuildAudioClipTrimArgs(
                        sourcePath, segName, clip.Start, clip.End, clip.FilterChain, settings);

                    if (!await RunStepAsync(record, workDir, args, segName, $"audio segment {index}", ct))
                        return;

                    audioSegments.Add(segName);
                    index++;
                }

                const string mixedName = "mixed.mp4";
                if (!await RunStepAsync(record, workDir,
                        ExportArgBuilders.BuildAmixArgs(concatName, audioSegments, mixedName, settings),
                        mixedName, "audio mix", ct))
                {
                    return;
                }
                producedName = mixedName;
            }

            var outputPath = Path.Combine(workDir, producedName);
            record.ResultPath = outputPath;
            record.ResultSizeBytes = new FileInfo(outputPath).Length;
            record.ProgressPercent = 100;
            record.State = JobState.Succeeded;
        }
        catch (Exception ex)
        {
            record.State = JobState.Failed;
            record.ErrorMessage = ex.Message;
            logger.LogError(ex, "Export assemble job {JobId} threw", record.Id);
        }
        finally
        {
            foreach (var id in request.SegmentIds) segments.MarkNotInUse(id);
            foreach (var clip in request.Audio?.Clips ?? []) sources.MarkNotInUse(clip.ClipId);
            concurrency.Release();
            ScheduleRetentionCleanup(record.Id, workDir);
        }
    }

    /// <summary>Runs one ffmpeg step, failing the job with a step-named message. Returns false when
    /// the caller should stop — the step name matters because a multi-step job that just says
    /// "ffmpeg exit code 1" gives no clue whether the concat or the mix broke.</summary>
    private async Task<bool> RunStepAsync(
        SegmentJobRecord record, string workDir, IReadOnlyList<string> args,
        string expectedOutput, string stepName, CancellationToken ct)
    {
        var result = await ffmpeg.RunAsync(args, workDir, _options.JobTimeout, ct: ct);

        if (ct.IsCancellationRequested)
        {
            record.State = JobState.Failed;
            record.ErrorMessage = "Cancelled.";
            return false;
        }

        if (result.TimedOut || result.ExitCode != 0)
        {
            record.State = JobState.Failed;
            record.ErrorMessage = result.TimedOut
                ? $"Timed out after {_options.JobTimeout} during {stepName}."
                : $"ffmpeg exit code {result.ExitCode} during {stepName}.";
            logger.LogWarning("Export assemble {JobId} failed: {Error}", record.Id, record.ErrorMessage);
            return false;
        }

        if (!File.Exists(Path.Combine(workDir, expectedOutput)))
        {
            record.State = JobState.Failed;
            record.ErrorMessage = $"ffmpeg reported success but produced no output during {stepName}.";
            return false;
        }

        return true;
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
