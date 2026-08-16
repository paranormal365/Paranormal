namespace Ben.Video.Editor.Models;

/// <summary>
/// Named keys for <see cref="CalloutClip.ControlPointValues"/> that control
/// the geometric and stylistic properties of SVG-rendered callout shapes.
///
/// <para>Keys are strings so they round-trip through JSON project files and
/// match the <c>Key</c> field on <see cref="Assets.CalloutControlPointDef"/>
/// when shapes are served from the Ben app WebAPI.</para>
/// </summary>
public static class CalloutControlPoints
{
    // ── Arrow / Line path ─────────────────────────────────────────────────────

    /// <summary>Start point X (canvas fraction 0–1).</summary>
    public const string StartX = "startX";
    /// <summary>Start point Y (canvas fraction 0–1).</summary>
    public const string StartY = "startY";
    /// <summary>End point X (canvas fraction 0–1).</summary>
    public const string EndX   = "endX";
    /// <summary>End point Y (canvas fraction 0–1).</summary>
    public const string EndY   = "endY";
    /// <summary>
    /// Bezier curve midpoint handle X.
    /// Dragging this creates the curve on Arrow and Line shapes.
    /// </summary>
    public const string MidX   = "midX";
    /// <summary>Bezier curve midpoint handle Y.</summary>
    public const string MidY   = "midY";

    // ── Star ──────────────────────────────────────────────────────────────────

    /// <summary>Outer spike radius as a fraction of the bounding-box half-size (0–1).</summary>
    public const string OuterRadius = "outerRadius";
    /// <summary>Inner spike-base radius as a fraction of bounding-box half-size (0–1).</summary>
    public const string InnerRadius = "innerRadius";
    /// <summary>Number of star points (integer stored as double, min 3).</summary>
    public const string Points      = "points";

    // ── Rectangle ─────────────────────────────────────────────────────────────

    /// <summary>Corner radius in canvas pixels (0 = sharp corners).</summary>
    public const string CornerRadius = "cornerRadius";

    // ── Convenience helpers ───────────────────────────────────────────────────

    /// <summary>All six keys used by Arrow and Line shapes.</summary>
    public static readonly IReadOnlyList<string> ArrowKeys  =
        [StartX, StartY, EndX, EndY, MidX, MidY];

    /// <summary>Keys that define the visual curve of an arrow (user-adjustable in most cases).</summary>
    public static readonly IReadOnlyList<string> CurveKeys  = [MidX, MidY];

    /// <summary>Keys for Star shapes.</summary>
    public static readonly IReadOnlyList<string> StarKeys   =
        [OuterRadius, InnerRadius, Points];

    /// <summary>Keys for Rectangle shapes.</summary>
    public static readonly IReadOnlyList<string> RectKeys   = [CornerRadius];
}
