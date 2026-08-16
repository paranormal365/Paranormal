namespace Ben.Video.Editor.Models.Assets;

/// <summary>
/// A named, positioned control point defined by the Ben app administrator on an SVG asset.
/// Each point targets a specific SVG element (identified by CSS selector or element id)
/// and exposes one type of manipulation to the end user in the Ben.Video editor.
///
/// <para>During SVG frame rendering, the control point's current interpolated value
/// (driven by <c>MotionKeyframeService</c> + easing) is applied to the target element
/// as an SVG attribute or transform before rasterising the frame to PNG.</para>
/// </summary>
public sealed record SvgControlPoint
{
    /// <summary>
    /// Stable identifier. Used as the keyframe track id in the motion system:
    /// <c>"{assetId}/{PointId}"</c>.
    /// </summary>
    public string PointId { get; init; } = string.Empty;

    /// <summary>Human-readable label shown in the editor's control-point panel.</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// CSS selector or bare SVG element id (without #) that identifies which
    /// element inside the SVG this point targets.
    /// Example: <c>"#left-arm"</c>, <c>".highlight-ring"</c>, <c>"*"</c> (whole SVG).
    /// </summary>
    public string TargetSelector { get; init; } = "*";

    /// <summary>
    /// Visual placement on the SVG canvas (0–1 normalised against the SVG viewBox).
    /// Used to draw the handle dot in the admin point-picker UI.
    /// Not used during export.
    /// </summary>
    public double X { get; init; }

    /// <summary>Visual placement Y (0–1 normalised).</summary>
    public double Y { get; init; }

    /// <summary>The kind of SVG attribute/transform this point controls.</summary>
    public SvgControlPointType Type { get; init; }

    // ── Numeric constraints (for Move / Scale / Rotate / StrokeWidth) ─────────

    /// <summary>
    /// Minimum value the user can set. Interpretation depends on <see cref="Type"/>:
    /// <list type="bullet">
    ///   <item><c>Scale / ScaleX / ScaleY</c> — minimum scale factor (e.g. 0.1)</item>
    ///   <item><c>Move</c> — minimum translate offset in SVG user units</item>
    ///   <item><c>Rotate</c> — minimum angle in degrees</item>
    ///   <item><c>StrokeWidth</c> — minimum px width</item>
    ///   <item><c>*Alpha</c> — minimum opacity (0–1)</item>
    /// </list>
    /// </summary>
    public double? MinValue { get; init; }

    /// <summary>Maximum value. Same interpretation as <see cref="MinValue"/>.</summary>
    public double? MaxValue { get; init; }

    /// <summary>Default value at t=0 (rest state). Same unit as Min/Max.</summary>
    public double DefaultValue { get; init; }

    // ── Color constraints (for StrokeColor / FillColor) ──────────────────────

    /// <summary>
    /// Default hex color for color-type points, e.g. <c>"#FF0000"</c>.
    /// Null for non-color point types.
    /// </summary>
    public string? DefaultColor { get; init; }

    /// <summary>
    /// Optional curated list of allowed hex colors the user may pick for this point.
    /// Null = no restriction (full color picker available).
    /// </summary>
    public IReadOnlyList<string>? AllowedColors { get; init; }

    /// <summary>
    /// When true, a color-type point allows the user to also animate alpha
    /// (the server offers a color + opacity pair).
    /// </summary>
    public bool AllowColorAlpha { get; init; }
}
