using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Models;
using Ben.Video.RenderService;

namespace Ben.Video.Editor.Services;

/// <summary>
/// The real ffmpeg-outside-the-browser <see cref="IRenderBackend"/> — item #38 phase 123 (F).
/// Turns one <see cref="RenderJob"/> into a <see cref="SegmentRenderSpec"/> (never argv, never a
/// filter string — see <c>Ben.Video.Sidecar.Jobs.ArgvFactory</c> for where that spec becomes a
/// real command line, on the sidecar side of the wire) and hands it to
/// <see cref="SidecarSegmentClient"/> (upload/submit/poll/download/cleanup — shared with
/// <see cref="NativeClipEncoder"/>'s real-export use, item #38 phase 124), then lands the
/// finished segment in the main ffmpeg instance's MEMFS under the same <c>bgseg_</c>-prefixed
/// naming <c>RenderWorkerBackend</c> uses — see <c>VideoEditor.razor</c>'s stream-copy concat gate
/// for why that prefix is load-bearing, not cosmetic.
///
/// Requires the clip to have an OPFS-backed source (<see cref="Models.TrackItem.OpfsExt"/>) — the
/// only way to hand the sidecar a file at all is a real upload, unlike
/// <see cref="RenderWorkerBackend"/>'s in-browser MEMFS-copy fallback for non-OPFS clips. Item #38
/// phases A/B already made every import path OPFS-backed, so in practice this only declines a
/// clip in the rare case OPFS itself is unavailable in this browser — <see cref="FallbackRenderBackend"/>
/// doesn't retry a declined job against the wasm backend mid-job (see that class's doc comment),
/// so a persistently non-OPFS clip stays on rough quality until edited again in the same session.
/// Documented, not silently swallowed — a real, narrow scope cut for this phase.
/// </summary>
public sealed class NativeSidecarBackend(
    ClipStore clips,
    RenderStatusService status,
    FfmpegService mainFfmpeg,
    NativeSidecarService sidecar,
    SidecarSegmentClient segmentClient,
    RemoteSegmentIndex remoteSegments,
    SidecarTransport transport,
    ErrorLogService errorLog) : IRenderBackend
{
    public async Task<RenderJobResult> RenderAsync(RenderJob job, IProgress<int> progress, CancellationToken ct)
    {
        var connection = await sidecar.GetConnectionAsync();
        if (connection is null) return RenderJobResult.Failed("Sidecar not connected.");
        var (port, token) = connection.Value;
        var baseUrl = $"http://127.0.0.1:{port}";

        var videoClip = clips.PrimaryVideoTrack.VideoClips.FirstOrDefault(c => c.Id == job.ClipId);
        var imageClip = videoClip is null ? clips.PrimaryVideoTrack.ImageClips.FirstOrDefault(c => c.Id == job.ClipId) : null;
        if (videoClip is null && imageClip is null)
            return RenderJobResult.Failed("Clip no longer on the timeline.");

        var ext = videoClip?.OpfsExt ?? imageClip?.OpfsExt;
        if (ext is null)
            return RenderJobResult.Failed("Clip has no OPFS-backed source — the native sidecar can only render uploadable clips.");

        try
        {
            var (previewWidth, previewHeight) = status.PreviewDimensions();

            // Item #70 phase 160 — only ask for retention when the sidecar advertises "concat".
            // SidecarJsonOptions.Default is Disallow, so sending Retain to a sidecar that predates
            // the field would hard-400 the entire job rather than being ignored.
            var canRetain = sidecar.HasCapability(SidecarCapabilities.Concat);
            remoteSegments.SyncInstance(sidecar.InstanceId);

            var spec = BuildSpec(job, videoClip, imageClip, ext, previewWidth, previewHeight) with { Retain = canRetain };

            var result = await segmentClient.RunAsync(baseUrl, token, job.ClipId, ext, spec, progress, ct);
            var segmentName = $"bgseg_native_{job.ClipId:N}_{Guid.NewGuid():N}.mp4";

            // Dual residency: the MEMFS write is unconditional and unchanged, so the wasm path
            // keeps working even if the sidecar dies immediately after this. The remote id is
            // purely additive bookkeeping on top of it.
            await mainFfmpeg.WriteFileWhenReadyAsync(segmentName, result.Bytes, ct);
            if (result.RetainedSegmentId is { } remoteId) remoteSegments.Register(segmentName, remoteId);

            return RenderJobResult.Ok(segmentName, result.Bytes.LongLength);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Any failure here means the sidecar didn't answer correctly — a
            // SidecarTransportException (nothing listening, dropped connection, non-2xx) or a
            // raw JSException from one of the byte-moving interop calls. All of them mean the
            // same thing from here: don't trust this connection. Tell NativeSidecarService so the very next
            // queued job's primaryAvailable() check routes to the wasm fallback instead of
            // retrying a dead process (see NativeSidecarService.ReportConnectionLost's doc
            // comment) — deliberately unconditional rather than trying to enumerate every
            // exception type two different transports (HttpClient + JS fetch) can throw.
            sidecar.ReportConnectionLost();
            // A lost connection means the sidecar's retained segments are unreachable and, if it
            // restarts, gone entirely — a stale id would be worse than none (see
            // RemoteSegmentIndex's remarks), so drop the whole map rather than trying to salvage it.
            remoteSegments.Clear();
            errorLog.Log("NativeSidecarBackend", ex);
            return RenderJobResult.Failed(ex.Message);
        }
    }

    /// <summary>Segments from either backend land in the same main-instance MEMFS — deleting here
    /// is identical to <see cref="RenderWorkerBackend.DeleteSegmentAsync"/> and tolerates an
    /// already-deleted name, matching every <see cref="IRenderBackend"/> implementation's
    /// contract.</summary>
    public async Task DeleteSegmentAsync(string segmentName)
    {
        try { await mainFfmpeg.DeleteFileAsync(segmentName); } catch { /* best-effort */ }

        // Item #70 phase 160 — drop the sidecar's twin too. Best-effort on purpose: the store's
        // own LRU is the backstop for anything this misses (a closed tab, a dead connection), so a
        // failure here costs disk that gets reclaimed, never correctness.
        if (remoteSegments.Remove(segmentName) is not { } remoteId) return;
        try
        {
            var connection = await sidecar.GetConnectionAsync();
            if (connection is null) return;
            var (port, token) = connection.Value;

            await transport.SendAsync("DELETE", $"http://127.0.0.1:{port}/v1/segments/{remoteId:N}", token);
        }
        catch { /* best-effort — LRU reclaims it */ }
    }

    private static SegmentRenderSpec BuildSpec(
        RenderJob job, VideoClip? videoClip, ImageClip? imageClip, string ext, int width, int height)
    {
        var pass = job.Pass == RenderPass.Rough ? RenderPassKind.Rough : RenderPassKind.Fine;

        if (videoClip is not null)
        {
            return new SegmentRenderSpec(
                Kind: SegmentKind.Video,
                ClipId: videoClip.Id,
                SourceExt: ext,
                Pass: pass,
                Duration: videoClip.Duration,
                StartTrim: videoClip.StartTrim,
                EndTrim: videoClip.EndTrim,
                Speed: videoClip.Speed,
                MuteAudio: videoClip.MuteAudio,
                Gain: videoClip.Volume,
                OutputWidth: width,
                OutputHeight: height,
                Effects: SidecarDtoMapping.ToDto(videoClip.Effects),
                AppliedEffects: [.. videoClip.AppliedEffects.Select(SidecarDtoMapping.ToDto)],
                VolumeAutomation: [.. videoClip.VolumeAutomation.Select(k => new VolumeKeyframeDto(k.Position, k.Volume))]);
        }

        return new SegmentRenderSpec(
            Kind: SegmentKind.Image,
            ClipId: imageClip!.Id,
            SourceExt: ext,
            Pass: pass,
            Duration: imageClip.Duration,
            StartTrim: 0,
            EndTrim: 0,
            Speed: 1,
            MuteAudio: false,
            Gain: 1,
            OutputWidth: width,
            OutputHeight: height,
            Effects: SidecarDtoMapping.ToDto(imageClip.Effects),
            AppliedEffects: [.. imageClip.AppliedEffects.Select(SidecarDtoMapping.ToDto)],
            VolumeAutomation: []);
    }
}
