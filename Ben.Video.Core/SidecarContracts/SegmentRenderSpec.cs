namespace Ben.Video.Core.SidecarContracts;

/// <summary>Which clip type a <see cref="SegmentRenderSpec"/> describes — mirrors the
/// video/image split <c>RenderWorkerBackend</c> already makes.</summary>
public enum SegmentKind { Video, Image }

/// <summary>Rough (fast/ugly) vs. Fine (real quality) vs. Export — the first two mean the same
/// thing as <c>Ben.Video.RenderService.RenderPass</c> (a separate enum here, not a shared
/// reference, because this one is a wire contract with
/// <see cref="Ben.Video.Core.SidecarContracts.SidecarJsonOptions"/>'s strict string-enum
/// serialization — <c>RenderPass</c> stays an internal implementation detail of the preview render
/// queue). <see cref="Export"/> is item #38 phase 124: a real-export-quality single-clip render
/// requested by <c>ExportService</c>'s per-clip trim step, not the background preview queue — its
/// quality comes from <see cref="SegmentRenderSpec.ExportQuality"/>, explicit on the wire, rather
/// than the hardcoded Rough/Fine presets.</summary>
public enum RenderPassKind { Rough, Fine, Export }

/// <summary>Real-export-quality settings for a <see cref="RenderPassKind.Export"/> segment —
/// required when <see cref="SegmentRenderSpec.Pass"/> is <see cref="RenderPassKind.Export"/>,
/// ignored otherwise. Mirrors the subset of <c>ExportSettings</c> a single clip's trim/encode
/// pass actually needs; deliberately all enums (never a free-form codec/preset string) so an
/// unrecognized value is a parse failure, not a string that could reach argv unexamined.
/// <c>PixelFormat</c> is NOT included — fixed sidecar-side (always <c>yuv420p</c>, matching this
/// app's only-ever-used value) rather than trusted from the wire.</summary>
public sealed record ExportQualityDto(
    ExportVideoCodec VideoCodec,
    ExportAudioCodec AudioCodec,
    int Bitrate,
    bool UseCrf,
    int Crf,
    bool IncludeAudio,
    int AudioBitrate,
    ExportPresetKind Preset,
    int Fps);

/// <summary>Mirrors <c>ExportSettings.VideoCodec</c>'s three supported string values.</summary>
public enum ExportVideoCodec { H264, H265, Vp9 }

/// <summary>Mirrors <c>ExportSettings.AudioCodec</c>'s two supported string values.</summary>
public enum ExportAudioCodec { Aac, Opus }

/// <summary>Mirrors the ffmpeg libx264/libx265 preset names <c>ExportSettings.Preset</c> accepts.
/// Ignored by the sidecar when <see cref="ExportVideoCodec.Vp9"/> is selected, matching
/// <c>ExportArgBuilders.QualityArgs</c>'s existing "-preset only for libx264/libx265" rule.</summary>
public enum ExportPresetKind { UltraFast, SuperFast, VeryFast, Faster, Fast, Medium, Slow, Slower, VerySlow }

/// <summary>One effect instance, resolved against the sidecar's own <c>ClipEffectRegistry</c>
/// (built from the same <c>DefaultEffectRegistry.CreateDefault()</c> the browser uses) —
/// never a filter string. <see cref="EffectId"/> must match a registered effect; unknown ids are
/// rejected by <c>SpecValidator</c> before any argv is built.</summary>
public sealed record AppliedEffectDto(string EffectId, Dictionary<string, double> Parameters);

/// <summary>One volume-automation keyframe — see <c>VolumeKeyframe</c> for the position/volume
/// semantics this mirrors.</summary>
public sealed record VolumeKeyframeDto(double Position, double Volume);

/// <summary>Colour-grading + fade settings — see <c>ClipEffects</c> for the semantics this
/// mirrors. Optional on the wire (<c>null</c> = neutral/no filter).</summary>
public sealed record ClipEffectsDto(
    double Brightness,
    double Contrast,
    double Saturation,
    double FadeInSeconds,
    double FadeOutSeconds);

/// <summary>
/// The one job type the sidecar accepts (phase 123, item #38 phase F) — a typed, structured
/// description of a single background-render segment. Deliberately contains no filter string, no
/// argv, and no output filename: the sidecar reconstructs a minimal <c>VideoClip</c>/<c>ImageClip</c>
/// from these fields and calls the exact same <c>ExportArgBuilders.BuildBackgroundRenderVideoArgs</c>/
/// <c>BuildBackgroundRenderImageArgs</c> the browser's own <c>RenderWorkerBackend</c> calls, so the
/// two backends produce byte-for-byte-comparable segment structure (same codec/dimensions/fps/
/// always-present-audio layout) — required for <c>VideoEditor.razor</c>'s <c>bgseg_</c>-prefixed
/// stream-copy concat gate to treat native and wasm segments interchangeably.
/// </summary>
public sealed record SegmentRenderSpec(
    SegmentKind Kind,
    Guid ClipId,
    string SourceExt,
    RenderPassKind Pass,
    /// <summary>The clip's <c>TrackItem.Duration</c> — the image display length for
    /// <see cref="SegmentKind.Image"/>, or the fallback trimmed length for
    /// <see cref="SegmentKind.Video"/> when <see cref="EndTrim"/> hasn't been set.</summary>
    double Duration,
    double StartTrim,
    double EndTrim,
    double Speed,
    bool MuteAudio,
    double Gain,
    int OutputWidth,
    int OutputHeight,
    ClipEffectsDto? Effects,
    IReadOnlyList<AppliedEffectDto> AppliedEffects,
    IReadOnlyList<VolumeKeyframeDto> VolumeAutomation,
    /// <summary>Required when <see cref="Pass"/> is <see cref="RenderPassKind.Export"/>; ignored
    /// (should be <c>null</c>) for <see cref="RenderPassKind.Rough"/>/<see cref="RenderPassKind.Fine"/>,
    /// which derive their own quality from the pass itself — see <c>ArgvFactory</c>.</summary>
    ExportQualityDto? ExportQuality = null,
    /// <summary>
    /// Item #70 phase 160 — ask the sidecar to <b>also</b> keep its own copy of the finished
    /// segment (dual residency), so a later concat/assemble job can use it as an input without the
    /// browser re-uploading bytes it just downloaded. The retained id comes back on
    /// <see cref="JobStatusInfo.RetainedSegmentId"/>.
    ///
    /// <para>Defaults to <c>false</c>, and the client only ever sends it when the sidecar
    /// advertises the <c>"concat"</c> capability: <see cref="SidecarJsonOptions.Default"/> uses
    /// <c>JsonUnmappedMemberHandling.Disallow</c>, so sending this field to an older sidecar that
    /// doesn't know it would hard-400 the whole job.</para>
    /// </summary>
    bool Retain = false);
