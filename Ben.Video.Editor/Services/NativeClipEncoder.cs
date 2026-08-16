using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// Per-clip native offload for the real export pipeline — item #38 phase 124 (the "hybrid"
/// design: rather than replicating <see cref="ExportService"/>'s whole multi-stage pipeline
/// server-side, only the individually-CPU-heavy per-clip trim/encode step — the dominant cost for
/// long-form content — is optionally delegated to the sidecar. Everything downstream (concat,
/// transitions, overlays, audio mix, chapters, watermark, OPFS write) stays exactly the wasm code
/// it already is, completely unaware of which backend produced any given segment file, because
/// <c>ArgvFactory</c>'s <see cref="RenderPassKind.Export"/> path calls the *exact same*
/// <c>ExportArgBuilders.BuildTrimArgs</c>/<c>BuildImageSegmentArgs</c> <see cref="ExportService"/>
/// itself calls for a wasm-rendered clip.
///
/// Both <c>TryEncode*</c> methods return <c>null</c> — never throw — on any failure (sidecar not
/// connected, clip has no OPFS source, upload/job/poll/download failure): the caller's job is to
/// fall straight through to the existing wasm <c>ExecAsync</c> call for that one clip, exactly as
/// if this class didn't exist. A native failure never fails the export and never triggers a
/// whole-export rerun — only that one clip's trim quietly happens in the browser instead.
/// </summary>
public sealed class NativeClipEncoder(
    NativeSidecarService sidecar,
    SidecarSegmentClient segmentClient,
    ErrorLogService errorLog)
{
    public Task<byte[]?> TryEncodeVideoSegmentAsync(VideoClip clip, ExportSettings settings, CancellationToken ct)
    {
        if (clip.OpfsExt is not { } ext) return Task.FromResult<byte[]?>(null);

        var quality = ToExportQualityDto(settings);
        if (quality is null) return Task.FromResult<byte[]?>(null);

        var spec = new SegmentRenderSpec(
            Kind: SegmentKind.Video,
            ClipId: clip.Id,
            SourceExt: ext,
            Pass: RenderPassKind.Export,
            Duration: clip.Duration,
            StartTrim: clip.StartTrim,
            EndTrim: clip.EndTrim,
            Speed: clip.Speed,
            MuteAudio: clip.MuteAudio,
            Gain: clip.Volume,
            // Matches ExportService.TrimSegmentsAsync's own BuildTrimArgs call exactly: no
            // scale/pad at trim time for video clips (0 = ExportArgBuilders' "skip it" sentinel)
            // — the composite stage scales later, unchanged, still in wasm.
            OutputWidth: 0,
            OutputHeight: 0,
            Effects: SidecarDtoMapping.ToDto(clip.Effects),
            AppliedEffects: [.. clip.AppliedEffects.Select(SidecarDtoMapping.ToDto)],
            VolumeAutomation: [.. clip.VolumeAutomation.Select(k => new VolumeKeyframeDto(k.Position, k.Volume))],
            ExportQuality: quality);

        return TryRunAsync(clip.Id, ext, spec, ct);
    }

    public Task<byte[]?> TryEncodeImageSegmentAsync(ImageClip clip, ExportSettings settings, CancellationToken ct)
    {
        if (clip.OpfsExt is not { } ext) return Task.FromResult<byte[]?>(null);

        var quality = ToExportQualityDto(settings);
        if (quality is null) return Task.FromResult<byte[]?>(null);

        var (imgOutW, imgOutH) = ExportService.ParseResolution(settings.Resolution);

        var spec = new SegmentRenderSpec(
            Kind: SegmentKind.Image,
            ClipId: clip.Id,
            SourceExt: ext,
            Pass: RenderPassKind.Export,
            Duration: clip.Duration,
            StartTrim: 0,
            EndTrim: 0,
            Speed: 1,
            MuteAudio: false,
            Gain: 1,
            // Matches ExportService.RenderImageSegmentsAsync's own BuildImageSegmentArgs call
            // exactly — image clips ARE scaled at segment time in the wasm path, unlike video
            // clips above, and they scale to the PROJECT canvas. Item #9: this used to pass the
            // clip's own source dimensions, which made the resulting scale/pad a no-op; the two
            // paths must agree here or a sidecar-encoded image segment and a wasm-encoded one
            // would land on different canvases within the same export.
            OutputWidth: imgOutW,
            OutputHeight: imgOutH,
            Effects: SidecarDtoMapping.ToDto(clip.Effects),
            AppliedEffects: [.. clip.AppliedEffects.Select(SidecarDtoMapping.ToDto)],
            VolumeAutomation: []);

        return TryRunAsync(clip.Id, ext, spec, ct);
    }

    /// <summary>Item #70 phase 162 — the MEMFS name the caller wrote the last returned bytes to,
    /// paired with its retained remote id, so <c>ExportService</c> can register the mapping without
    /// this class needing to know how the caller names its files.</summary>
    public Guid? LastRetainedSegmentId { get; private set; }

    private async Task<byte[]?> TryRunAsync(Guid clipId, string ext, SegmentRenderSpec spec, CancellationToken ct)
    {
        LastRetainedSegmentId = null;
        var connection = await sidecar.GetConnectionAsync();
        if (connection is null) return null;
        var (port, token) = connection.Value;
        var baseUrl = $"http://127.0.0.1:{port}";

        try
        {
            // Item #70 phase 162 — retention is now requested (capability-gated), because the
            // export-assemble job consumes exactly these segments. Phase 160 deliberately left
            // this off while there was no consumer. The retained id is surfaced via
            // LastRetainedSegmentId for ExportService to map against its own MEMFS name.
            var retain = sidecar.HasCapability(SidecarCapabilities.ExportAssemble);
            var result = await segmentClient.RunAsync(
                baseUrl, token, clipId, ext, spec with { Retain = retain }, progress: null, ct);
            LastRetainedSegmentId = result.RetainedSegmentId;
            return result.Bytes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Same reasoning as NativeSidecarBackend's catch-all — see its doc comment.
            sidecar.ReportConnectionLost();
            errorLog.Log("NativeClipEncoder", ex);
            return null;
        }
    }

    /// <summary>Maps the free-form <see cref="ExportSettings"/> strings to the wire DTO's enums.
    /// Returns <c>null</c> (falls back to wasm) for any codec/preset this app doesn't offer in
    /// its own export UI — safer than guessing, and it can never actually happen for a real user
    /// selection since <see cref="ExportSettings"/>'s values are themselves drawn from a fixed
    /// dropdown list.</summary>
    internal static ExportQualityDto? ToExportQualityDto(ExportSettings s)
    {
        ExportVideoCodec videoCodec;
        switch (s.VideoCodec)
        {
            case "libx264": videoCodec = ExportVideoCodec.H264; break;
            case "libx265": videoCodec = ExportVideoCodec.H265; break;
            case "libvpx-vp9": videoCodec = ExportVideoCodec.Vp9; break;
            default: return null;
        }

        ExportAudioCodec audioCodec;
        switch (s.AudioCodec)
        {
            case "aac": audioCodec = ExportAudioCodec.Aac; break;
            case "libopus": audioCodec = ExportAudioCodec.Opus; break;
            default: return null;
        }

        ExportPresetKind preset;
        switch (s.Preset)
        {
            case "ultrafast": preset = ExportPresetKind.UltraFast; break;
            case "superfast": preset = ExportPresetKind.SuperFast; break;
            case "veryfast": preset = ExportPresetKind.VeryFast; break;
            case "faster": preset = ExportPresetKind.Faster; break;
            case "fast": preset = ExportPresetKind.Fast; break;
            case "medium": preset = ExportPresetKind.Medium; break;
            case "slow": preset = ExportPresetKind.Slow; break;
            case "slower": preset = ExportPresetKind.Slower; break;
            case "veryslow": preset = ExportPresetKind.VerySlow; break;
            default: return null;
        }

        return new ExportQualityDto(
            VideoCodec: videoCodec,
            AudioCodec: audioCodec,
            Bitrate: s.Bitrate,
            UseCrf: s.UseCrf,
            Crf: s.Crf,
            IncludeAudio: s.IncludeAudio,
            AudioBitrate: s.AudioBitrate,
            Preset: preset,
            Fps: s.Fps);
    }
}
