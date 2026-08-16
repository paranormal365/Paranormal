namespace Ben.Video.Editor.Models;

/// <summary>The result of a snap check along one axis: which guide fraction matched (null = no
/// snap), and the offset from the item's leading edge that guide corresponds to (0 = leading
/// edge, size/2 = center, size = trailing edge) — needed to convert the guide position back into
/// a leading-edge position for the caller to actually apply.</summary>
public readonly record struct CanvasSnapResult(double? Guide, double Offset);

/// <summary>
/// Pure static helper for canvas alignment-guide snapping (item #57 P5) — the 2D-canvas
/// counterpart to <see cref="Ben.Video.Editor.Services.SnapEngine"/>'s timeline snapping, checking
/// an item's leading edge, center, and trailing edge along one axis against a fixed set of guide
/// fractions (canvas center, edges, rule-of-thirds), rather than 1D point-to-point matching.
/// Isolated from Blazor/JSInterop so it can be unit-tested without a browser.
/// </summary>
public static class CanvasSnapCalculator
{
    /// <summary>Canvas edges (0, 1), center (0.5), and rule-of-thirds lines — the standard
    /// alignment guide set, shared by both axes (X and Y use the same fractions independently).</summary>
    public static readonly IReadOnlyList<double> DefaultGuides = [0.0, 1.0 / 3.0, 0.5, 2.0 / 3.0, 1.0];

    /// <summary>
    /// Checks <paramref name="position"/> (the item's leading-edge fraction along one axis) and
    /// <paramref name="size"/> (its extent along that same axis — 0 for a point-only item like
    /// text) against <paramref name="guides"/>, and returns whichever of the item's leading edge,
    /// center, or trailing edge is closest to any guide, if within <paramref name="thresholdFraction"/>.
    /// </summary>
    public static CanvasSnapResult FindSnap(
        double position, double size, IReadOnlyList<double> guides, double thresholdFraction)
    {
        if (guides.Count == 0 || thresholdFraction <= 0)
            return new CanvasSnapResult(null, 0);

        double? bestGuide = null;
        var bestDist   = double.MaxValue;
        var bestOffset = 0.0;

        Span<double> offsets = [0.0, size / 2.0, size];
        foreach (var offset in offsets)
        {
            var anchor = position + offset;
            foreach (var guide in guides)
            {
                var dist = Math.Abs(anchor - guide);
                if (dist < bestDist)
                {
                    bestDist   = dist;
                    bestGuide  = guide;
                    bestOffset = offset;
                }
            }
        }

        return bestDist <= thresholdFraction ? new CanvasSnapResult(bestGuide, bestOffset) : new CanvasSnapResult(null, 0);
    }

    /// <summary>Returns the snapped leading-edge position — <paramref name="position"/> unchanged
    /// if nothing is within range, otherwise the position that puts the nearest matching edge
    /// exactly on its guide.</summary>
    public static double Snap(double position, double size, IReadOnlyList<double> guides, double thresholdFraction)
    {
        var result = FindSnap(position, size, guides, thresholdFraction);
        return result.Guide is { } guide ? guide - result.Offset : position;
    }

    /// <summary>Returns the guide fraction currently matched (for drawing the guide line), or
    /// null if nothing is within range.</summary>
    public static double? ActiveGuide(double position, double size, IReadOnlyList<double> guides, double thresholdFraction) =>
        FindSnap(position, size, guides, thresholdFraction).Guide;
}
