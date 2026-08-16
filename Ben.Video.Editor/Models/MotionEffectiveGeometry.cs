using Ben.Video.Editor.Services;

namespace Ben.Video.Editor.Models;

/// <summary>
/// The "effective geometry at time T" computation — evaluate a layer's motion path (if any) and
/// fold it into its X/Y/size — extracted from <c>LiveOverlayPreview.razor</c> so every consumer
/// (the live visual, canvas hit-testing/selection, the on-canvas control-point overlays) shares
/// one implementation instead of three independently-maintained copies that could silently drift
/// apart on a future edit.
/// </summary>
public static class MotionEffectiveGeometry
{
    public static CalloutClip Effective(MotionKeyframeService motion, CalloutClip clip, double time)
    {
        if (!motion.HasPath(clip.Id)) return clip;
        var frame = motion.Evaluate(clip.Id, time) ?? new MotionFrame(clip.X, clip.Y, 1.0, 1.0);
        return ExportArgBuilders.ApplyMotionFrame(clip, frame);
    }

    public static ClipArtClip Effective(MotionKeyframeService motion, ClipArtClip clip, double time)
    {
        if (!motion.HasPath(clip.Id)) return clip;
        var frame = motion.Evaluate(clip.Id, time) ?? new MotionFrame(clip.X, clip.Y, 1.0, 1.0);
        return ExportArgBuilders.ApplyMotionFrame(clip, frame);
    }

    public static TextOverlay Effective(MotionKeyframeService motion, TextOverlay overlay, double time)
    {
        if (!motion.HasPath(overlay.Id)) return overlay;
        var frame = motion.Evaluate(overlay.Id, time)
            ?? new MotionFrame(overlay.OverrideX ?? 0.5, overlay.OverrideY ?? 0.5, 1.0, 1.0);
        return ExportArgBuilders.ApplyMotionFrame(overlay, frame);
    }

    /// <summary>ClipArtClip.Height of -1 means "preserve aspect ratio from Width" — there's no
    /// asset pixel-aspect data resolved at this layer, so callers fall back to a square box
    /// until a real height is set by resizing. Matches ClipArtControlPointOverlay's own
    /// fallback exactly.</summary>
    public static double EffectiveClipArtHeight(ClipArtClip clip) => clip.Height > 0 ? clip.Height : clip.Width;

    /// <summary>The text overlay's anchor X as a canvas fraction — <see cref="TextOverlay.OverrideX"/>
    /// if set, else an alignment-based approximation. ffmpeg's runtime-measured text_w/text_h
    /// aren't available in C#, so this ignores the actual glyph box size — close, not
    /// pixel-exact, until the first drag pins an explicit position down.</summary>
    public static double TextAnchorX(TextOverlay overlay, int canvasWidth) => overlay.OverrideX ?? overlay.HorizontalAlign switch
    {
        TextHorizontalAlign.Left  => canvasWidth <= 0 ? 0.0 : overlay.OffsetX / (double)canvasWidth,
        TextHorizontalAlign.Right => canvasWidth <= 0 ? 1.0 : 1.0 - overlay.OffsetX / (double)canvasWidth,
        _                         => 0.5,
    };

    /// <summary>The text overlay's anchor Y as a canvas fraction — see <see cref="TextAnchorX"/>.</summary>
    public static double TextAnchorY(TextOverlay overlay, int canvasHeight) => overlay.OverrideY ?? overlay.VerticalAlign switch
    {
        TextVerticalAlign.Top    => canvasHeight <= 0 ? 0.0 : overlay.OffsetY / (double)canvasHeight,
        TextVerticalAlign.Bottom => canvasHeight <= 0 ? 1.0 : 1.0 - overlay.OffsetY / (double)canvasHeight,
        _                        => 0.5,
    };

    /// <summary>A Callout/ClipArt/TextOverlay layer's on-screen position right now (item #57
    /// P5) — the interpolated motion frame if it has a path (so an edit starts from wherever it's
    /// actually rendered, not a possibly-stale static field), else its static position. Extracted
    /// from <c>CanvasSelectionOverlay.StartPos</c> (its own drag-start resolution) since P5's
    /// keyboard nudge needs the exact same computation from a different component.</summary>
    public static (double X, double Y) EffectivePosition(
        MotionKeyframeService motion, TrackItem item, double time, int canvasWidth, int canvasHeight)
    {
        if (motion.HasPath(item.Id))
        {
            var frame = motion.Evaluate(item.Id, time);
            if (frame is not null) return (frame.X, frame.Y);
        }

        return item switch
        {
            CalloutClip c => (c.X, c.Y),
            ClipArtClip a => (a.X, a.Y),
            TextOverlay t => (TextAnchorX(t, canvasWidth), TextAnchorY(t, canvasHeight)),
            _             => (0.5, 0.5),
        };
    }

    // ── Keyframe seeding (item #57, phase P2) ───────────────────────────────────
    // Builds the MotionKeyframe a layer's FIRST keyframe should carry, from its current static
    // values — so creating that first keyframe never visibly snaps the layer. Scale/Alpha are
    // always 1.0 (no additional multiplier yet; the layer's own static Width/Height/Opacity stay
    // as the base ApplyMotionFrame scales/multiplies from). Mirrors
    // MotionKeyframeEditor.AddKeyframeAtPlayhead's existing per-type field mapping, generalized
    // into one place three callers (that panel, and the two new canvas-editing paths) share.

    public static MotionKeyframe StaticSeed(CalloutClip clip, double time) => new()
    {
        Time               = time,
        X                  = clip.X,
        Y                  = clip.Y,
        Scale              = 1.0,
        Alpha              = 1.0,
        FillColor          = clip.FillColor,
        StrokeColor        = clip.StrokeColor,
        ControlPointValues = new Dictionary<string, double>(clip.ControlPointValues),
        ShadowColor        = clip.ShadowColor,
        ShadowOffsetX      = clip.ShadowOffsetX,
        ShadowOffsetY      = clip.ShadowOffsetY,
        ShadowBlur         = clip.ShadowBlur,
    };

    /// <summary>ClipArtClip has no FillColor/StrokeColor/ShadowColor of its own — <see cref="Services.ExportArgBuilders.ApplyMotionFrame(ClipArtClip, MotionFrame)"/>
    /// never reads those fields, so the returned keyframe leaves them at MotionKeyframe's own
    /// (irrelevant, harmless) defaults. <see cref="MotionKeyframe.Rotation"/> seeds from the
    /// clip's current static value (item #57 P3) so the first rotation keyframe doesn't jump the
    /// layer back to 0°; <see cref="MotionKeyframe.ScaleX"/>/<see cref="MotionKeyframe.ScaleY"/>
    /// are deliberately left null — a fresh keyframe has no per-axis scale yet, and null correctly
    /// falls back to the uniform <c>Scale = 1.0</c> set here.</summary>
    public static MotionKeyframe StaticSeed(ClipArtClip clip, double time) => new()
    {
        Time     = time,
        X        = clip.X,
        Y        = clip.Y,
        Scale    = 1.0,
        Alpha    = 1.0,
        Rotation = clip.Rotation,
    };

    public static MotionKeyframe StaticSeed(TextOverlay overlay, double time, int canvasWidth, int canvasHeight) => new()
    {
        Time          = time,
        X             = TextAnchorX(overlay, canvasWidth),
        Y             = TextAnchorY(overlay, canvasHeight),
        Scale         = 1.0,
        Alpha         = 1.0,
        ShadowColor   = overlay.ShadowColor,
        ShadowOffsetX = overlay.ShadowOffsetX,
        ShadowOffsetY = overlay.ShadowOffsetY,
        ShadowBlur    = overlay.ShadowBlur,
    };
}
