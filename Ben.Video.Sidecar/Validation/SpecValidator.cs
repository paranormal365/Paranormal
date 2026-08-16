using Ben.Video.Core.SidecarContracts;
using Ben.Video.Editor.Services;
using Microsoft.Extensions.Options;

namespace Ben.Video.Sidecar.Validation;

/// <summary>
/// The one place request-supplied identifiers and extensions turn into filesystem-safe values —
/// item #38 phase E/F threat T4/T5. Every clip/job/asset id crossing the wire is re-parsed as a
/// <see cref="Guid"/> and reformatted (<c>{id:N}</c>) before it ever touches a path, which makes
/// path traversal (<c>../</c>), absolute paths, and null bytes structurally impossible — there is
/// no code path where a raw request string is concatenated into a filesystem path. Extensions are
/// checked against a closed allowlist, never merely sanitized.
/// </summary>
public sealed class SpecValidator(IOptions<SidecarOptions> options)
{
    private readonly SidecarOptions _options = options.Value;

    /// <summary>Parses a route/query id strictly — rejects anything that isn't a canonical GUID
    /// (so e.g. <c>"..%2f..%2fetc"</c> never reaches a Guid, let alone a path).</summary>
    public bool TryParseId(string? raw, out Guid id) =>
        Guid.TryParseExact(raw, "D", out id) || Guid.TryParse(raw, out id);

    /// <summary>Normalizes and validates a client-supplied extension against the closed
    /// allowlist. Returns null (reject) rather than attempting to sanitize an invalid value.</summary>
    public string? ValidateExtension(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var normalized = raw.StartsWith('.') ? raw : $".{raw}";
        normalized = normalized.ToLowerInvariant();
        return _options.AllowedSourceExtensions.Contains(normalized) ? normalized : null;
    }

    /// <summary>Range/allowlist-checks every field of a <see cref="SegmentRenderSpec"/> — item #38
    /// phase 123 threat T4. Nothing here trusts the browser: an out-of-range number or an unknown
    /// effect id is rejected outright rather than clamped/best-effort-corrected, so
    /// <see cref="Jobs.ArgvFactory"/> only ever sees values that were already proven safe.
    /// Returns null when valid; otherwise a short reason suitable for a 400 response body.</summary>
    public string? ValidateSegmentSpec(SegmentRenderSpec spec, ClipEffectRegistry registry)
    {
        if (spec.ClipId == Guid.Empty) return "ClipId is required.";
        if (ValidateExtension(spec.SourceExt) is null) return "Unsupported or missing SourceExt.";

        if (spec.Duration is < 0 or > 86_400) return "Duration out of range.";
        if (spec.StartTrim is < 0 or > 86_400) return "StartTrim out of range.";
        if (spec.EndTrim is < 0 or > 86_400) return "EndTrim out of range.";
        if (spec.Speed is < 0.1 or > 10) return "Speed out of range.";
        if (spec.Gain is < 0 or > 8) return "Gain out of range.";

        // Floor is 2 (not the plan's original 16): RenderStatusService.ComputePreviewDimensions()
        // clamps to Math.Max(2, ...) — a very low preview-quality scale setting can legitimately
        // compute a dimension that small. 0 is also allowed — ExportArgBuilders' "no scale/pad
        // filter" sentinel, which ExportService's own TrimSegmentsAsync call site already uses
        // for video clips (scaling happens later, at composite time), so a real-export-quality
        // native segment (item #38 phase 124) needs to be able to send it too.
        if (spec.OutputWidth != 0 && (spec.OutputWidth is < 2 or > 7680 || spec.OutputWidth % 2 != 0))
            return "OutputWidth out of range.";
        if (spec.OutputHeight != 0 && (spec.OutputHeight is < 2 or > 7680 || spec.OutputHeight % 2 != 0))
            return "OutputHeight out of range.";

        if (spec.VolumeAutomation.Count > 1000) return "Too many volume keyframes.";
        foreach (var kf in spec.VolumeAutomation)
        {
            if (kf.Position is < 0 or > 1) return "Volume keyframe position out of range.";
            if (kf.Volume is < 0 or > 8) return "Volume keyframe volume out of range.";
        }

        if (spec.AppliedEffects.Count > 32) return "Too many applied effects.";
        foreach (var applied in spec.AppliedEffects)
        {
            var def = registry.GetById(applied.EffectId);
            if (def is null) return $"Unknown effect id '{applied.EffectId}'.";

            foreach (var (key, value) in applied.Parameters)
            {
                var schema = def.ParameterSchema.FirstOrDefault(p => p.Key == key);
                if (schema is null) return $"Unknown parameter '{key}' for effect '{applied.EffectId}'.";
                if (value < schema.Min || value > schema.Max)
                    return $"Parameter '{key}' for effect '{applied.EffectId}' out of range.";
            }
        }

        if (spec.Effects is { } fx)
        {
            if (fx.Brightness is < -1 or > 1) return "Effects.Brightness out of range.";
            if (fx.Contrast is < 0 or > 2) return "Effects.Contrast out of range.";
            if (fx.Saturation is < 0 or > 3) return "Effects.Saturation out of range.";
            if (fx.FadeInSeconds is < 0 or > 86_400) return "Effects.FadeInSeconds out of range.";
            if (fx.FadeOutSeconds is < 0 or > 86_400) return "Effects.FadeOutSeconds out of range.";
        }

        // Item #38 phase 124 — RenderPassKind.Export carries its own explicit quality settings
        // instead of deriving them from the pass the way Rough/Fine do; ArgvFactory requires
        // ExportQuality to be present for this pass (and ignores it otherwise), so catching a
        // missing/malformed one here as a clean 400 is better than a 500 mid-job.
        if (spec.Pass == RenderPassKind.Export)
        {
            if (spec.ExportQuality is not { } q) return "ExportQuality is required for the Export pass.";
            if (q.Bitrate is < 1 or > 500_000) return "ExportQuality.Bitrate out of range.";
            if (q.Crf is < 0 or > 63) return "ExportQuality.Crf out of range.";
            if (q.AudioBitrate is < 1 or > 5_000) return "ExportQuality.AudioBitrate out of range.";
            if (q.Fps is < 1 or > 240) return "ExportQuality.Fps out of range.";
        }
        else if (spec.ExportQuality is not null)
        {
            return "ExportQuality must be omitted outside the Export pass.";
        }

        return null;
    }

    /// <summary>
    /// Range-checks a <see cref="ThumbnailJobRequest"/> — item #70 phase 159. Same
    /// reject-don't-clamp policy as <see cref="ValidateSegmentSpec"/>.
    ///
    /// <para><see cref="ThumbnailJobRequest.Count"/>'s ceiling is the one that matters: it becomes
    /// N output groups in a single ffmpeg argv, so an absurd value would be both a very long argv
    /// and a very long encode. 50 is comfortably above anything <c>ThumbnailPlanner</c> asks for
    /// (its own cap is 8) while staying far away from a resource-exhaustion lever (threat T6).</para>
    /// </summary>
    /// <summary>
    /// Validates an <see cref="ExportAssembleRequest"/> — item #70 phase 162.
    ///
    /// <para><see cref="AudioMixClipDto.FilterChain"/> is the one field that can't be range-checked:
    /// it's a pre-built ffmpeg filter string. It is therefore <b>allowlisted by character class</b>
    /// rather than parsed — the chain is machine-generated by
    /// <c>ExportArgBuilders.BuildAudioClipFilterChain</c> from numeric clip properties, so it can
    /// only ever contain filter names, digits, and the small punctuation set below. Rejecting
    /// anything else keeps a hostile chain from reaching the ffmpeg command line even though this
    /// endpoint is already token-gated (defence in depth, threat T4).</para>
    /// </summary>
    public string? ValidateExportAssembleRequest(ExportAssembleRequest request)
    {
        if (request.SegmentIds is not { Count: > 0 }) return "At least one segment id is required.";
        if (request.SegmentIds.Count > 1000) return "Too many segments in one assemble request.";
        if (request.SegmentIds.Any(id => id == Guid.Empty)) return "Segment ids must be non-empty GUIDs.";

        var q = request.Quality;
        if (q is null) return "Quality is required.";
        if (q.Bitrate is < 1 or > 500_000) return "Quality.Bitrate out of range.";
        if (q.Crf is < 0 or > 63) return "Quality.Crf out of range.";
        if (q.AudioBitrate is < 1 or > 5_000) return "Quality.AudioBitrate out of range.";
        if (q.Fps is < 1 or > 240) return "Quality.Fps out of range.";

        if (request.Audio is not { } audio) return null;
        if (audio.Clips.Count > 200) return "Too many audio clips in one assemble request.";

        foreach (var clip in audio.Clips)
        {
            if (clip.ClipId == Guid.Empty) return "Audio ClipId is required.";
            if (ValidateExtension(clip.SourceExt) is null) return "Unsupported or missing audio SourceExt.";
            if (clip.Start is < 0 or > 86_400) return "Audio Start out of range.";
            if (clip.End is < 0 or > 86_400) return "Audio End out of range.";
            if (clip.End <= clip.Start) return "Audio End must be greater than Start.";
            if (!IsSafeFilterChain(clip.FilterChain)) return "Audio FilterChain contains unsupported characters.";
        }

        return null;
    }

    /// <summary>Character allowlist for a machine-generated audio filter chain. Deliberately
    /// excludes quotes, backslashes, shell metacharacters and whitespace — none of which
    /// <c>BuildAudioClipFilterChain</c> ever emits.</summary>
    private static bool IsSafeFilterChain(string? chain)
    {
        if (string.IsNullOrEmpty(chain)) return false;
        if (chain.Length > 4096) return false;
        foreach (var c in chain)
        {
            var ok = char.IsAsciiLetterOrDigit(c)
                     || c is '=' or ',' or ':' or '.' or '-' or '+' or '|' or '[' or ']' or '_' or '/' or '*' or '@';
            if (!ok) return false;
        }
        return true;
    }

    public string? ValidateThumbnailRequest(ThumbnailJobRequest request)
    {
        if (request.ClipId == Guid.Empty) return "ClipId is required.";
        if (ValidateExtension(request.SourceExt) is null) return "Unsupported or missing SourceExt.";
        if (request.Count is < 1 or > 50) return "Count out of range.";
        if (request.Duration is < 0 or > 86_400) return "Duration out of range.";
        return null;
    }
}
