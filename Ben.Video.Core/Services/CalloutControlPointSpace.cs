using Ben.Video.Editor.Models;

namespace Ben.Video.Editor.Services;

/// <summary>
/// What an arrow or line callout's control points are measured against.
/// </summary>
/// <remarks>
/// <para>They were canvas fractions: a point at 0.25 meant a quarter of the way across the
/// <i>frame</i>, with no relationship to the callout it belonged to. So moving the callout, or
/// resizing it, or animating it along a motion path, left its arrow exactly where it was — the box
/// went one way and the line it was supposed to be drawing stayed behind (2026-09-05 audit,
/// callouts-3).</para>
///
/// <para>They are fractions of the callout's own bounding box now: 0 is the box's left or top edge,
/// 1 is its right or bottom, and values outside that range are allowed because an arrow pointing
/// out of its box is an ordinary thing to want. The box moves, the arrow moves with it.</para>
///
/// <para>Every other control point — a star's radii, a rectangle's corner radius — was already
/// measured against the box. Only the path points were not.</para>
/// </remarks>
public static class CalloutControlPointSpace
{
    /// <summary>The control points that used to be canvas fractions and are now box fractions.</summary>
    public static readonly string[] PathKeys =
    [
        CalloutControlPoints.StartX, CalloutControlPoints.StartY,
        CalloutControlPoints.EndX,   CalloutControlPoints.EndY,
        CalloutControlPoints.MidX,   CalloutControlPoints.MidY,
    ];

    /// <summary>Where a box fraction sits on the canvas, as a canvas fraction.</summary>
    public static double ToCanvas(double boxFraction, double boxOrigin, double boxSize) =>
        boxOrigin + boxFraction * boxSize;

    /// <summary>The reverse: what canvas fraction <paramref name="canvasFraction"/> is of the box.</summary>
    /// <remarks>
    /// A box with no size has no inside, so there is no fraction to give. Returning the canvas
    /// value unchanged keeps a degenerate callout where it was drawn instead of sending its arrow
    /// to infinity.
    /// </remarks>
    public static double FromCanvas(double canvasFraction, double boxOrigin, double boxSize) =>
        boxSize <= 0 ? canvasFraction : (canvasFraction - boxOrigin) / boxSize;

    /// <summary>
    /// Rewrites one callout's path points from canvas fractions to box fractions.
    /// </summary>
    /// <remarks>
    /// Run once when an older project is opened. A callout with no path points is untouched, so
    /// this is safe to run over everything.
    /// </remarks>
    public static void MigrateToBoxRelative(CalloutClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        var cpv = clip.ControlPointValues;

        foreach (var key in PathKeys)
        {
            if (!cpv.TryGetValue(key, out var canvasValue)) continue;

            var isX = key.EndsWith('X');
            cpv[key] = FromCanvas(
                canvasValue,
                isX ? clip.X : clip.Y,
                isX ? clip.Width : clip.Height);
        }
    }
}
