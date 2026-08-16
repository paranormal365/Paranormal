namespace Ben.Video.Editor.Models;

/// <summary>
/// The style of transition effect between two adjacent clips.
/// </summary>
public enum TransitionStyle
{
    /// <summary>Instant cut — no transition effect.</summary>
    Cut,
    /// <summary>Opacity fade from clip A to clip B.</summary>
    Fade,
    /// <summary>Cross-dissolve (simultaneous fade out / fade in).</summary>
    Dissolve,
    /// <summary>Wipe left-to-right.</summary>
    WipeLeft,
    /// <summary>Wipe right-to-left.</summary>
    WipeRight,
    /// <summary>Slide/push from right to left.</summary>
    SlideLeft,
    /// <summary>Zoom/scale transition.</summary>
    Zoom,
    // ── Curated extras (item #57 T5) — a small hand-picked subset of ffmpeg xfade's ~50 named
    // transitions, chosen for visual variety over the 6 above rather than completeness.
    /// <summary>Circular reveal expanding outward from the center.</summary>
    CircleOpen,
    /// <summary>Circular reveal shrinking inward to the center.</summary>
    CircleClose,
    /// <summary>Circular reveal expanding outward from the center with a soft edge.</summary>
    Radial,
    /// <summary>Soft-edged wipe left-to-right.</summary>
    SmoothLeft,
    /// <summary>Soft-edged wipe right-to-left.</summary>
    SmoothRight,
    /// <summary>Soft-edged wipe bottom-to-top.</summary>
    SmoothUp,
    /// <summary>Soft-edged wipe top-to-bottom.</summary>
    SmoothDown,
    /// <summary>Blocky mosaic/pixelation dissolve.</summary>
    Pixelize,
    /// <summary>Fade to black, then fade in.</summary>
    FadeBlack,
    /// <summary>Fade to white, then fade in.</summary>
    FadeWhite
}

/// <summary>
/// A transition effect placed between two adjacent video clips on a track.
/// Requires the Transitions feature flag.
/// The transition overlaps both clips: half its duration comes from the tail of
/// the preceding clip, half from the head of the following clip.
/// </summary>
public sealed record Transition : TrackItem
{
    /// <summary>The visual style of this transition.</summary>
    public TransitionStyle Style { get; set; } = TransitionStyle.Fade;

    /// <summary>Id of the clip that precedes this transition.</summary>
    public Guid FromClipId { get; set; }

    /// <summary>Id of the clip that follows this transition.</summary>
    public Guid ToClipId { get; set; }
}
