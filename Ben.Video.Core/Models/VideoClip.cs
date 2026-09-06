using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Models;

/// <summary>
/// A video clip placed on a Video track.
/// Inherits timeline positioning (TimelinePosition, Duration, Order) from <see cref="TrackItem"/>.
/// </summary>
public sealed record VideoClip : TrackItem, IHasVolumeAutomation
{
    /// <summary>Trim start within the source video file in seconds.</summary>
    public double StartTrim { get; set; }

    /// <summary>Trim end within the source video file in seconds.</summary>
    public double EndTrim { get; set; }

    /// <summary>Source video width in pixels (populated after metadata extraction).</summary>
    public int Width { get; set; }

    /// <summary>Source video height in pixels (populated after metadata extraction).</summary>
    public int Height { get; set; }

    /// <summary>MEMFS filename of the source video (set after the file is written to ffmpeg MEMFS).</summary>
    public string? MemFsName { get; set; }

    /// <summary>
    /// Parts of this clip's picture that must not be shown.
    /// </summary>
    /// <remarks>
    /// Faces, number plates, house numbers — whatever identifies a client or an address. The
    /// editor had a whole-frame blur and nothing that could obscure part of a picture, so a clip
    /// with one identifying detail in it could only be left out (2026-09-05 audit, the
    /// completeness critic's first item). Empty on every clip until somebody draws one.
    /// </remarks>
    public List<RedactionRegion> Redactions { get; set; } = [];

    /// <summary>
    /// Where this clip's picture sits in the frame, and how much of it is used.
    /// </summary>
    /// <remarks>
    /// Null means "fill the frame", which is what every clip did before this existed. Set it to
    /// put a second camera in a corner or beside the first, to turn portrait phone footage
    /// upright, or to cut a DVR's bars off (2026-09-05 audit).
    /// </remarks>
    public ClipTransform? Transform { get; set; }

    /// <summary>Thumbnail blob: URLs extracted from the clip (populated after load).</summary>
    public List<string> ThumbnailUrls { get; set; } = [];

    /// <summary>
    /// The trimmed duration of this clip in seconds.
    /// Overrides TrackItem.Duration when trims are set.
    /// </summary>
    public double TrimmedDuration =>
        EndTrim > StartTrim ? EndTrim - StartTrim : Duration;

    /// <inheritdoc />
    public override double EffectiveLength => TrimmedDuration;


    /// <summary>
    /// Playback speed multiplier applied during export.
    /// 1.0 = normal speed, 2.0 = double speed, 0.5 = half speed.
    /// Valid range: 0.25 – 4.0.
    /// </summary>
    public double Speed { get; set; } = 1.0;

    /// <summary>
    /// The effective timeline duration after applying <see cref="Speed"/>.
    /// This is the real wall-clock length the clip occupies: TrimmedDuration / Speed.
    /// </summary>
    public double EffectiveDuration =>
        Speed > 0 ? TrimmedDuration / Speed : TrimmedDuration;

    /// <summary>
    /// Scalar gain fallback (0.0 = silence, 1.0 = unity, 2.0 ≈ +6 dB).
    /// Used when VolumeAutomation has fewer than 2 keyframes.
    /// </summary>
    public double Volume { get; set; } = 1.0;

    /// <summary>Ordered automation keyframes (sorted by Position ascending).</summary>
    public List<VolumeKeyframe> VolumeAutomation { get; set; } = [];

    /// <summary>
    /// Per-clip visual effect settings (colour grading + fade in/out).
    /// Defaults to a neutral no-op state so clips with no effects are unaffected during export.
    /// </summary>
    public ClipEffects Effects { get; set; } = new();

    /// <summary>
    /// Ordered list of effects applied to this clip via the extensible effects system (Phase 29+).
    /// Each entry is an <see cref="AppliedEffect"/> resolved through <c>ClipEffectRegistry</c>.
    /// </summary>
    public List<AppliedEffect> AppliedEffects { get; set; } = [];

    /// <summary>
    /// When <c>true</c> the audio stream of this video clip is suppressed during export
    /// (set automatically by "Separate Audio" so the detached <see cref="AudioClip"/> becomes the sole audio source).
    /// </summary>
    public bool MuteAudio { get; set; }

    /// <summary>
    /// Whether the source file has an audio stream at all, as opposed to having one the user has
    /// muted (<see cref="MuteAudio"/>).
    /// </summary>
    /// <remarks>
    /// Render commands attach a silent track to clips without sound, so that every rendered
    /// segment has the same audio layout and the segments stay concat-compatible. Getting this
    /// wrong in the "has audio" direction is not a cosmetic error: ffmpeg refuses the whole
    /// command with "Stream map '0:a' matches no streams".
    ///
    /// Defaults to true, so clips restored from a project saved before this existed behave as
    /// they did rather than being silenced.
    /// </remarks>
    public bool HasAudio { get; set; } = true;

    /// <summary>
    /// Returns the linearly-interpolated gain at a normalised position [0,1] within the clip.
    /// Falls back to the scalar <see cref="Volume"/> when fewer than 2 keyframes are present.
    /// </summary>
    public double GetVolumeAt(double position)
    {
        if (VolumeAutomation.Count < 2) return Volume;

        var before = VolumeAutomation.LastOrDefault(k => k.Position <= position);
        var after  = VolumeAutomation.FirstOrDefault(k => k.Position >  position);

        if (before is null) return after!.Volume;
        if (after  is null) return before.Volume;

        var t = (position - before.Position) / (after.Position - before.Position);
        return before.Volume + t * (after.Volume - before.Volume);
    }
}

