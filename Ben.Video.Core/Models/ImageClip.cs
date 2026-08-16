using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Models;

/// <summary>
/// A static image clip (PNG, JPG, GIF, WebP) placed on a Video track.
/// <see cref="TrackItem.Duration"/> is the display duration in seconds (default 5.0).
/// During export the image is converted to a video segment via <c>-loop 1 -t &lt;duration&gt;</c>.
/// </summary>
public sealed record ImageClip : TrackItem
{
    /// <summary>Source image width in pixels (populated after import).</summary>
    public int Width { get; set; }

    /// <summary>Source image height in pixels (populated after import).</summary>
    public int Height { get; set; }

    /// <summary>MEMFS filename of the source image (set after the file is written to ffmpeg MEMFS).</summary>
    public string? MemFsName { get; set; }

    /// <summary>
    /// Blob URL of the thumbnail image shown on the timeline chip and in the clip browser.
    /// Created via a JS canvas render — no ffmpeg required.
    /// </summary>
    public string? ThumbnailUrl { get; set; }

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
}
