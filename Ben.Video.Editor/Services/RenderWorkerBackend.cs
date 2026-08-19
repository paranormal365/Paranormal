using Ben.Video.Editor.Models;
using Ben.Video.RenderService;

namespace Ben.Video.Editor.Services;

/// <summary>
/// The real <see cref="IRenderBackend"/> — renders one <see cref="RenderJob"/> on the second
/// ffmpeg.wasm instance (<see cref="RenderWorkerService"/>), item #36 phase C. Owns the one piece
/// <see cref="BackgroundRenderService"/> (Blazor-free) can't: finding the actual clip data and
/// building its encode args.
/// </summary>
public sealed class RenderWorkerBackend : IRenderBackend
{
    private readonly ClipStore _clips;
    private readonly RenderWorkerService _worker;
    private readonly FfmpegService _mainFfmpeg;
    private readonly RenderStatusService _status;
    private readonly ClipEffectRegistry _effectRegistry;
    private readonly ErrorLogService _errorLog;

    public RenderWorkerBackend(
        ClipStore clips, RenderWorkerService worker, FfmpegService mainFfmpeg,
        RenderStatusService status, ClipEffectRegistry effectRegistry, ErrorLogService errorLog)
    {
        _clips          = clips;
        _worker         = worker;
        _mainFfmpeg     = mainFfmpeg;
        _status         = status;
        _effectRegistry = effectRegistry;
        _errorLog       = errorLog;
    }

    public async Task<RenderJobResult> RenderAsync(RenderJob job, IProgress<int> progress, CancellationToken ct)
    {
        await _worker.LoadAsync();

        var videoClip = _clips.PrimaryVideoTrack.VideoClips.FirstOrDefault(c => c.Id == job.ClipId);
        var imageClip = videoClip is null ? _clips.PrimaryVideoTrack.ImageClips.FirstOrDefault(c => c.Id == job.ClipId) : null;

        if (videoClip is null && imageClip is null)
            return RenderJobResult.Failed("Clip no longer on the timeline.");

        var (previewWidth, previewHeight) = _status.PreviewDimensions();

        // Rough pass (item #36 phase D): same dimensions/fps/pixel-format/audio layout as fine so
        // mixed rough/fine segments stay stream-copy-concat compatible — ONLY preset and CRF
        // differ. ultrafast/35 is typically 5-15x faster than medium/23 on x264, which is the
        // entire point of the rough pass: a playable (ugly) preview fast, sharpened afterward.
        var settings = job.Pass == RenderPass.Rough
            ? new ExportSettings { Preset = "ultrafast", Crf = 35 }
            : new ExportSettings();
        var passTag = job.Pass == RenderPass.Rough ? "rough" : "fine";
        var segmentName = $"bgrender_{passTag}_{job.ClipId:N}_{Guid.NewGuid():N}.mp4";
        var mountedClipId = (Guid?)null;
        string? copiedSourceName = null;

        try
        {
            string inputName;
            if (videoClip is not null)
            {
                var (resolved, isCopy) = await ResolveSourceAsync(videoClip.Id, videoClip.OpfsExt, videoClip.MemFsName, ct);
                inputName = resolved;
                if (isCopy) copiedSourceName = resolved;
                else if (videoClip.OpfsExt is not null) mountedClipId = videoClip.Id;

                var start = videoClip.StartTrim;
                var end   = videoClip.EndTrim > videoClip.StartTrim ? videoClip.EndTrim : videoClip.Duration;
                var effectiveDuration = videoClip.EffectiveDuration > 0 ? videoClip.EffectiveDuration : videoClip.Duration;
                var volumeFilter = ExportArgBuilders.BuildVolumeAutomationFilter(videoClip, effectiveDuration);
                var appliedVf = ExportArgBuilders.BuildAppliedEffectsFilter(
                    videoClip.AppliedEffects, _effectRegistry, effectiveDuration, videoClip.Speed);
                var args = ExportArgBuilders.BuildBackgroundRenderVideoArgs(
                    inputName, segmentName, start, end, videoClip.Speed, settings,
                    volumeFilter, videoClip.Effects, videoClip.MuteAudio,
                    extraVf: string.IsNullOrEmpty(appliedVf) ? null : appliedVf,
                    outputWidth: previewWidth, outputHeight: previewHeight,
                    sourceHasAudio: videoClip.HasAudio);

                var code = await _worker.ExecAsync(args);
                if (code != 0) return RenderJobResult.Failed($"ffmpeg exit code {code}");
            }
            else
            {
                var (resolved, isCopy) = await ResolveSourceAsync(imageClip!.Id, imageClip.OpfsExt, imageClip.MemFsName, ct);
                inputName = resolved;
                if (isCopy) copiedSourceName = resolved;
                else if (imageClip.OpfsExt is not null) mountedClipId = imageClip.Id;

                var duration = imageClip.Duration > 0 ? imageClip.Duration : 5.0;
                var appliedVf = ExportArgBuilders.BuildAppliedEffectsFilter(imageClip.AppliedEffects, _effectRegistry, duration);
                var args = ExportArgBuilders.BuildBackgroundRenderImageArgs(
                    inputName, segmentName, duration, settings,
                    outputWidth: previewWidth, outputHeight: previewHeight,
                    effects: imageClip.Effects,
                    extraVf: string.IsNullOrEmpty(appliedVf) ? null : appliedVf);

                var code = await _worker.ExecAsync(args);
                if (code != 0) return RenderJobResult.Failed($"ffmpeg exit code {code}");
            }

            var (mainSideName, sizeBytes) = await TransferToMainAsync(segmentName, ct);
            return RenderJobResult.Ok(mainSideName, sizeBytes);
        }
        catch (Exception ex)
        {
            _errorLog.Log("RenderWorkerBackend", ex);
            return RenderJobResult.Failed(ex.Message);
        }
        finally
        {
            if (mountedClipId.HasValue)
                await _worker.UnmountSourceAsync(mountedClipId.Value);
            // Item #38 phase C — closes a real, previously-known leak: the OPFS-unavailable
            // fallback path below copies the source into the render worker's own MEMFS, and
            // nothing ever deleted that copy (only WORKERFS mounts were cleaned up here).
            if (copiedSourceName is not null)
                await _worker.DeleteFileAsync(copiedSourceName);
        }
    }

    /// <summary>Deletes a superseded/orphaned segment. Segments live in the MAIN instance's MEMFS
    /// (see <see cref="TransferToMainAsync"/>), so deletion targets the main instance — its
    /// DeleteFileAsync tolerates already-deleted names, which matters because a Fine segment can
    /// also be registered in PreviewSegmentCache and get deleted a second time by its eviction.</summary>
    public async Task DeleteSegmentAsync(string segmentName)
    {
        try { await _mainFfmpeg.DeleteFileAsync(segmentName); } catch { }
    }

    /// <summary>
    /// Moves a finished segment out of the render worker's MEMFS into the MAIN instance's, and
    /// returns the main-side name — which is what <see cref="RenderJobResult.SegmentName"/> (and
    /// therefore <see cref="RenderRegion.SegmentName"/>) carries from here on. Live-discovered
    /// necessity, not an optimization: ffmpeg.wasm serializes every API call through its worker's
    /// message queue, so reading a segment out of the render worker WHILE it's encoding the next
    /// job blocks until that encode finishes — consuming "the rough segment while the fine pass
    /// runs" (the whole point of the rough pass) deadlocks-in-practice if Preview has to touch
    /// the worker for it. Transferring at job completion — the one moment the worker is
    /// guaranteed idle — makes consumption a pure main-MEMFS read with no worker interaction at
    /// all. The worker-side copy is deleted immediately, so storage stays single-copy.
    /// </summary>
    private async Task<(string MainName, long SizeBytes)> TransferToMainAsync(string workerSegmentName, CancellationToken ct)
    {
        var bytes = await _worker.ReadFileAsync(workerSegmentName);

        // The main instance's write guard requires Ready — it may be mid-Preview-concat (short)
        // or mid-Export (long, though pause-on-export means only an already-in-flight job lands
        // here). WriteFileWhenReadyAsync waits rather than fails: this runs on the background
        // loop, which has nothing better to do, and failing instead would back off a perfectly
        // good render's signature forever.
        var mainName = $"bgseg_{workerSegmentName}";
        await _mainFfmpeg.WriteFileWhenReadyAsync(mainName, bytes, ct);

        await _worker.DeleteFileAsync(workerSegmentName);
        return (mainName, bytes.LongLength);
    }

    /// <summary>Zero-copy WORKERFS-mounts the clip's OPFS source when it has one. Before item #38
    /// phases A+B, clips imported via the Server/media-library tab never touched OPFS at all —
    /// bytes went straight into the main ffmpeg instance's MEMFS — so this fallback path (reading
    /// the bytes back out of the main instance and copying them into the render worker's own
    /// MEMFS) was the common case for those clips, not just an edge case. It's now rare (only a
    /// clip with no OPFS copy at all reaches it — e.g. OPFS unavailable in this browser), but is
    /// kept as a correctness fallback for every import path regardless. <see cref="RenderAsync"/>'s
    /// caller uses <c>IsCopy</c> to know whether to clean up the copy itself once the render is
    /// done (item #38 phase C — this used to leak; only WORKERFS mounts were ever unmounted).
    /// </summary>
    private async Task<(string Name, bool IsCopy)> ResolveSourceAsync(Guid clipId, string? opfsExt, string? mainMemFsName, CancellationToken ct)
    {
        if (opfsExt is not null)
        {
            var mounted = await _worker.MountSourceAsync(clipId, opfsExt);
            if (mounted is not null) return (mounted, false);
        }

        if (mainMemFsName is null)
            throw new InvalidOperationException("Clip has neither an OPFS source nor a MEMFS name.");

        // Waits rather than throws if the main instance is mid-Preview/mid-Export — same
        // race WriteFileWhenReadyAsync already handles on the write side (TransferToMainAsync
        // below); this read used to call ReadFileAsync directly and let the resulting
        // InvalidOperationException ("FfmpegService is not ready...") surface straight into
        // the user-visible error log for what's actually expected background-render contention.
        var bytes = await _mainFfmpeg.ReadFileWhenReadyAsync(mainMemFsName, ct);
        var copyName = $"src_copy_{clipId:N}_{mainMemFsName}";
        await _worker.WriteBytesAsync(copyName, bytes);
        return (copyName, true);
    }
}
