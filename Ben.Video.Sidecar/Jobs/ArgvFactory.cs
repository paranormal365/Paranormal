using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;
using Ben.Video.Editor.Services;

namespace Ben.Video.Sidecar.Jobs;

/// <summary>
/// Turns a validated <see cref="SegmentRenderSpec"/> into real ffmpeg argv — the one place a
/// typed job spec becomes a command line. Reconstructs a minimal <see cref="VideoClip"/>/
/// <see cref="ImageClip"/> from the spec's fields and calls the exact same builder methods the
/// browser calls for the equivalent case (both internal in <c>Ben.Video.Core</c>, exposed here
/// via <c>InternalsVisibleTo</c>):
/// <list type="bullet">
///   <item><see cref="RenderPassKind.Rough"/>/<see cref="RenderPassKind.Fine"/> (item #38 phase F,
///   background preview render) call <see cref="ExportArgBuilders.BuildBackgroundRenderVideoArgs"/>/
///   <c>BuildBackgroundRenderImageArgs</c> — the always-present-audio variants
///   <c>RenderWorkerBackend</c> also calls, guaranteeing a native segment and a wasm preview
///   segment share the exact codec/dimension/fps/audio-layout contract the <c>bgseg_</c>-prefixed
///   stream-copy concat gate in <c>VideoEditor.razor</c> relies on.</item>
///   <item><see cref="RenderPassKind.Export"/> (item #38 phase 124, real export) calls
///   <see cref="ExportArgBuilders.BuildTrimArgs"/>/<c>BuildImageSegmentArgs</c> — the exact same
///   builders <c>ExportService.TrimSegmentsAsync</c>/<c>RenderImageSegmentsAsync</c> call for a
///   wasm-rendered clip, so a native-rendered export segment is structurally indistinguishable
///   from a wasm-rendered one to every downstream export stage (concat, transitions, audio mix,
///   chapters, watermark) — none of which need to know or care which backend produced any given
///   segment.</item>
/// </list>
/// Every value here already passed <see cref="Validation.SpecValidator"/>'s range/allowlist
/// checks — this class only ever shapes already-trusted numbers into argv, never re-validates.
/// </summary>
public static class ArgvFactory
{
    public static string[] Build(
        SegmentRenderSpec spec, string inputPath, string outputName, ClipEffectRegistry registry)
    {
        var settings = ResolveSettings(spec);

        var appliedEffects = spec.AppliedEffects
            .Select(a => new AppliedEffect { EffectId = a.EffectId, Parameters = new Dictionary<string, double>(a.Parameters) })
            .ToList();
        var effects = ToClipEffects(spec.Effects);

        if (spec.Kind == SegmentKind.Video)
        {
            var clip = new VideoClip
            {
                Duration = spec.Duration,
                StartTrim = spec.StartTrim,
                EndTrim = spec.EndTrim,
                Speed = spec.Speed <= 0 ? 1.0 : spec.Speed,
                MuteAudio = spec.MuteAudio,
                Volume = spec.Gain,
                VolumeAutomation = spec.VolumeAutomation
                    .Select(k => new VolumeKeyframe { Position = k.Position, Volume = k.Volume })
                    .ToList(),
                Effects = effects,
                AppliedEffects = appliedEffects,
            };

            var start = clip.StartTrim;
            var end = clip.EndTrim > clip.StartTrim ? clip.EndTrim : clip.Duration;
            var effectiveDuration = clip.EffectiveDuration > 0 ? clip.EffectiveDuration : clip.Duration;
            var volumeFilter = ExportArgBuilders.BuildVolumeAutomationFilter(clip, effectiveDuration);
            var appliedVf = ExportArgBuilders.BuildAppliedEffectsFilter(clip.AppliedEffects, registry, effectiveDuration, clip.Speed);

            return spec.Pass == RenderPassKind.Export
                ? ExportArgBuilders.BuildTrimArgs(
                    inputPath, outputName, start, end, clip.Speed, settings,
                    volumeFilter, clip.Effects, clip.MuteAudio,
                    extraVf: string.IsNullOrEmpty(appliedVf) ? null : appliedVf,
                    outputWidth: spec.OutputWidth, outputHeight: spec.OutputHeight)
                : ExportArgBuilders.BuildBackgroundRenderVideoArgs(
                    inputPath, outputName, start, end, clip.Speed, settings,
                    volumeFilter, clip.Effects, clip.MuteAudio,
                    extraVf: string.IsNullOrEmpty(appliedVf) ? null : appliedVf,
                    outputWidth: spec.OutputWidth, outputHeight: spec.OutputHeight);
        }
        else
        {
            var clip = new ImageClip
            {
                Duration = spec.Duration,
                Effects = effects,
                AppliedEffects = appliedEffects,
            };

            var duration = clip.Duration > 0 ? clip.Duration : 5.0;
            var appliedVf = ExportArgBuilders.BuildAppliedEffectsFilter(clip.AppliedEffects, registry, duration);

            return spec.Pass == RenderPassKind.Export
                ? ExportArgBuilders.BuildImageSegmentArgs(
                    inputPath, outputName, duration, settings,
                    outputWidth: spec.OutputWidth, outputHeight: spec.OutputHeight,
                    effects: clip.Effects,
                    extraVf: string.IsNullOrEmpty(appliedVf) ? null : appliedVf)
                : ExportArgBuilders.BuildBackgroundRenderImageArgs(
                    inputPath, outputName, duration, settings,
                    outputWidth: spec.OutputWidth, outputHeight: spec.OutputHeight,
                    effects: clip.Effects,
                    extraVf: string.IsNullOrEmpty(appliedVf) ? null : appliedVf);
        }
    }

    private static ExportSettings ResolveSettings(SegmentRenderSpec spec) => spec.Pass switch
    {
        RenderPassKind.Rough => new ExportSettings { Preset = "ultrafast", Crf = 35 },
        RenderPassKind.Fine => new ExportSettings(),
        RenderPassKind.Export => ToExportSettings(
            spec.ExportQuality ?? throw new InvalidOperationException("Export pass requires ExportQuality.")),
        _ => throw new InvalidOperationException($"Unknown pass '{spec.Pass}'."),
    };

    /// <summary>Maps the wire DTO's enums back to the free-form strings <c>ExportArgBuilders</c>
    /// expects — the one place an <see cref="ExportVideoCodec"/>/<see cref="ExportAudioCodec"/>/
    /// <see cref="ExportPresetKind"/> value becomes a real ffmpeg codec/preset name.
    /// <see cref="ExportSettings.PixelFormat"/> is deliberately left at its default
    /// (<c>yuv420p</c>) rather than trusted from the wire — see <see cref="ExportQualityDto"/>'s
    /// doc comment.</summary>
    internal static ExportSettings ToExportSettings(ExportQualityDto q) => new()
    {
        VideoCodec = q.VideoCodec switch
        {
            ExportVideoCodec.H264 => "libx264",
            ExportVideoCodec.H265 => "libx265",
            ExportVideoCodec.Vp9 => "libvpx-vp9",
            _ => throw new InvalidOperationException($"Unknown video codec '{q.VideoCodec}'."),
        },
        AudioCodec = q.AudioCodec switch
        {
            ExportAudioCodec.Aac => "aac",
            ExportAudioCodec.Opus => "libopus",
            _ => throw new InvalidOperationException($"Unknown audio codec '{q.AudioCodec}'."),
        },
        Bitrate = q.Bitrate,
        UseCrf = q.UseCrf,
        Crf = q.Crf,
        IncludeAudio = q.IncludeAudio,
        AudioBitrate = q.AudioBitrate,
        Preset = q.Preset switch
        {
            ExportPresetKind.UltraFast => "ultrafast",
            ExportPresetKind.SuperFast => "superfast",
            ExportPresetKind.VeryFast => "veryfast",
            ExportPresetKind.Faster => "faster",
            ExportPresetKind.Fast => "fast",
            ExportPresetKind.Medium => "medium",
            ExportPresetKind.Slow => "slow",
            ExportPresetKind.Slower => "slower",
            ExportPresetKind.VerySlow => "veryslow",
            _ => throw new InvalidOperationException($"Unknown preset '{q.Preset}'."),
        },
        Fps = q.Fps,
    };

    private static ClipEffects ToClipEffects(ClipEffectsDto? dto) => dto is null
        ? new ClipEffects()
        : new ClipEffects
        {
            Brightness = dto.Brightness,
            Contrast = dto.Contrast,
            Saturation = dto.Saturation,
            FadeInSeconds = dto.FadeInSeconds,
            FadeOutSeconds = dto.FadeOutSeconds,
        };
}
