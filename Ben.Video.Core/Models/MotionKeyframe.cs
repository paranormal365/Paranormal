using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Models;

/// <summary>
/// A single keyframe on a <see cref="MotionPath"/> — defines layer property values
/// at a specific point in project time. Properties are interpolated between adjacent
/// keyframes by <see cref="Ben.Video.Editor.Services.MotionKeyframeService.Evaluate"/>.
///
/// <para><b>Coordinates:</b> <see cref="X"/> and <see cref="Y"/> are canvas fractions
/// (0.0 = left/top, 1.0 = right/bottom) matching <see cref="CalloutClip"/> conventions.</para>
///
/// <para><b>Bezier handles:</b> When <see cref="HandleOutX"/>/<see cref="HandleOutY"/> are
/// set on keyframe N, and <see cref="HandleInX"/>/<see cref="HandleInY"/> are set on
/// keyframe N+1, the path between the two keyframes is a cubic bezier curve.
/// If either handle is null the path segment is a straight line.</para>
/// </summary>
public sealed record MotionKeyframe
{
    /// <summary>Time in project seconds where this keyframe applies.</summary>
    public double  Time   { get; set; }

    // ── Position (canvas fractions 0–1) ──────────────────────────────────────

    public double  X      { get; set; } = 0.5;
    public double  Y      { get; set; } = 0.5;

    // ── Transform ────────────────────────────────────────────────────────────

    /// <summary>Scale multiplier (1.0 = original size). Legacy uniform axis — superseded by
    /// <see cref="ScaleX"/>/<see cref="ScaleY"/> when either is set (item #57 P3); kept as the
    /// fallback for keyframes that don't set them, so old saved projects behave identically.</summary>
    public double  Scale  { get; set; } = 1.0;

    /// <summary>Opacity/alpha (0.0 = transparent, 1.0 = fully opaque).</summary>
    public double  Alpha  { get; set; } = 1.0;

    /// <summary>Per-axis scale multiplier (item #57 P3). Null = not set on this keyframe — falls
    /// back to <see cref="Scale"/> when evaluated. Additive: existing saved projects deserialize
    /// both to null and behave exactly as before (uniform <see cref="Scale"/>).</summary>
    public double? ScaleX { get; set; }
    public double? ScaleY { get; set; }

    /// <summary>Rotation in degrees (item #57 P3). Null = not animated on this keyframe. Only
    /// honored for <c>ClipArtClip</c> layers — Callout/TextOverlay's SVG renderers have no
    /// rotation support at all, matching the locked scope decision for this arc.</summary>
    public double? Rotation { get; set; }

    // ── Easing (EasingHelper key applied from the PREVIOUS keyframe to this one) ──

    /// <summary>Easing curve applied to the segment ending at this keyframe.
    /// One of the <see cref="Ben.Video.Editor.Effects.EasingHelper"/> label keys
    /// (e.g. "Linear", "Ease Out", "Bounce Out"). Defaults to "Linear".</summary>
    public string  Easing { get; set; } = "Linear";

    // ── Bezier handles (canvas fractions, nullable = straight line) ───────────

    /// <summary>Bezier control handle leaving this keyframe (outgoing direction).
    /// Null means straight line to the next keyframe.</summary>
    public double? HandleOutX { get; set; }
    public double? HandleOutY { get; set; }

    /// <summary>Bezier control handle arriving at this keyframe (incoming direction).
    /// Null means straight line from the previous keyframe.</summary>
    public double? HandleInX  { get; set; }
    public double? HandleInY  { get; set; }

    // ── Shadow (Callout + TextOverlay layers; ignored by ImageClip) ────────────

    /// <summary>Shadow colour as a packed ARGB double (<see cref="Effects.ColorHelper"/>).</summary>
    public double ShadowColor { get; set; } = ColorHelper.Pack(0, 0, 0, 120);

    /// <summary>Shadow offset in pixels along X, at the export's native resolution.</summary>
    public double ShadowOffsetX { get; set; } = 3.0;

    /// <summary>Shadow offset in pixels along Y, at the export's native resolution.</summary>
    public double ShadowOffsetY { get; set; } = 3.0;

    /// <summary>Shadow blur radius in pixels.</summary>
    public double ShadowBlur { get; set; } = 4.0;

    // ── Callout-specific appearance (ignored by TextOverlay/ImageClip layers) ─

    /// <summary>Fill colour as a packed ARGB double (<see cref="Effects.ColorHelper"/>).
    /// Only meaningful for <c>CalloutClip</c> layers.</summary>
    public double FillColor { get; set; } = ColorHelper.Pack(255, 255, 0, 180);

    /// <summary>Stroke colour as a packed ARGB double. Only meaningful for <c>CalloutClip</c> layers.</summary>
    public double StrokeColor { get; set; } = ColorHelper.OpaqueBlack;

    /// <summary>
    /// Shape-specific control-point values at this keyframe (same keys as
    /// <see cref="CalloutClip.ControlPointValues"/>, e.g. <see cref="CalloutControlPoints.CornerRadius"/>).
    /// A key omitted here is simply not animated — <see cref="Services.MotionKeyframeService.Evaluate"/>
    /// holds the nearest keyframe's value for it rather than interpolating. Only meaningful for
    /// <c>CalloutClip</c> layers.
    /// </summary>
    public Dictionary<string, double> ControlPointValues { get; set; } = [];
}

/// <summary>
/// The complete animation path for one layer (TextOverlay, CalloutClip, ImageClip).
/// Owned by <see cref="Ben.Video.Editor.Services.MotionKeyframeService"/>.
/// </summary>
public sealed class MotionPath
{
    /// <summary>Stable identifier shared across serialisation.</summary>
    public Guid   Id        { get; init; } = Guid.NewGuid();

    /// <summary>Id of the layer this path animates (matches <c>TextOverlay.Id</c> etc.).</summary>
    public Guid   LayerId   { get; set; }

    /// <summary>Discriminator — "TextOverlay" | "CalloutClip" | "ImageClip".</summary>
    public string LayerType { get; set; } = string.Empty;

    /// <summary>Keyframes sorted ascending by <see cref="MotionKeyframe.Time"/>.</summary>
    public List<MotionKeyframe> Keyframes { get; set; } = [];
}

/// <summary>
/// Interpolated layer state at a specific project time, computed by
/// <see cref="Ben.Video.Editor.Services.MotionKeyframeService.Evaluate"/>.
/// </summary>
public sealed record MotionFrame(double X, double Y, double Scale, double Alpha)
{
    /// <summary>Interpolated per-axis scale (item #57 P3). Always resolved — defaults to
    /// <see cref="Scale"/> for every construction site that doesn't set it explicitly, so
    /// <c>ApplyMotionFrame</c> overloads can use <see cref="ScaleX"/>/<see cref="ScaleY"/>
    /// unconditionally instead of <see cref="Scale"/> with zero behavior change for anything
    /// that never touches per-axis scale.</summary>
    public double ScaleX { get; init; } = Scale;
    public double ScaleY { get; init; } = Scale;

    /// <summary>Interpolated rotation in degrees (item #57 P3). Null = no keyframe on this
    /// layer's path sets Rotation — callers fall back to the layer's own static Rotation field.
    /// Only meaningful for <c>ClipArtClip</c> layers.</summary>
    public double? Rotation { get; init; }

    /// <summary>Interpolated fill colour (packed ARGB). Only meaningful for <c>CalloutClip</c> layers.</summary>
    public double FillColor { get; init; } = ColorHelper.Pack(255, 255, 0, 180);

    /// <summary>Interpolated stroke colour (packed ARGB). Only meaningful for <c>CalloutClip</c> layers.</summary>
    public double StrokeColor { get; init; } = ColorHelper.OpaqueBlack;

    /// <summary>Interpolated shape control points. Only meaningful for <c>CalloutClip</c> layers.</summary>
    public IReadOnlyDictionary<string, double> ControlPointValues { get; init; } =
        new Dictionary<string, double>();

    /// <summary>Interpolated shadow colour (packed ARGB). Meaningful for <c>CalloutClip</c>/<c>TextOverlay</c> layers.</summary>
    public double ShadowColor { get; init; } = ColorHelper.Pack(0, 0, 0, 120);

    /// <summary>Interpolated shadow X offset in pixels.</summary>
    public double ShadowOffsetX { get; init; } = 3.0;

    /// <summary>Interpolated shadow Y offset in pixels.</summary>
    public double ShadowOffsetY { get; init; } = 3.0;

    /// <summary>Interpolated shadow blur radius in pixels.</summary>
    public double ShadowBlur { get; init; } = 4.0;
}
