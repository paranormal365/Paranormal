using Ben.Video.Editor.Models.Assets;

namespace Ben.Video.Editor.Models;

/// <summary>
/// Describes a single attribute/style mutation applied to an SVG element
/// during one frame of an animated <see cref="ClipArtClip"/> export.
///
/// <para>Each patch targets a CSS selector (or <c>"*"</c> for the whole SVG root)
/// and overrides one property derived from a <see cref="SvgControlPoint"/> value
/// evaluated at a specific time-offset via <see cref="Services.MotionKeyframeService"/>.</para>
/// </summary>
public sealed record SvgControlPointPatch
{
    /// <summary>The <see cref="SvgControlPoint.PointId"/> this patch was generated from.</summary>
    public string PointId { get; init; } = string.Empty;

    /// <summary>
    /// CSS selector identifying the target SVG element(s).
    /// Use <c>"*"</c> to target the root <c>&lt;svg&gt;</c> element.
    /// </summary>
    public string TargetSelector { get; init; } = "*";

    /// <summary>The kind of SVG attribute/style to modify.</summary>
    public SvgControlPointType Type { get; init; }

    /// <summary>
    /// Interpolated numeric value at this frame.
    /// Interpretation depends on <see cref="Type"/>:
    /// opacity (0–1), scale factor, rotation degrees, stroke-width px, etc.
    /// </summary>
    public double Value { get; init; }

    /// <summary>Translate-X offset in SVG user units (for <see cref="SvgControlPointType.Move"/>).</summary>
    public double X { get; init; }

    /// <summary>Translate-Y offset in SVG user units (for <see cref="SvgControlPointType.Move"/>).</summary>
    public double Y { get; init; }

    /// <summary>
    /// CSS colour string for color-type patches (e.g. <c>"#FF0000"</c>).
    /// Used when <see cref="Type"/> is <see cref="SvgControlPointType.StrokeColor"/>
    /// or <see cref="SvgControlPointType.FillColor"/>.
    /// </summary>
    public string? Color { get; init; }
}
