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
