using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Geometry;

/// <summary>
/// The result of a snap check along one axis: which guide matched (null = no snap), and the offset from
/// the block's leading edge that guide corresponds to (0 = leading edge, size/2 = centre, size = trailing).
/// </summary>
public readonly record struct CanvasSnapResult(double? Guide, double Offset);

/// <summary>
/// Alignment-guide snapping along one axis.
/// </summary>
/// <remarks>
/// Copied from Ben.Video.Editor's CanvasSnapCalculator. The unit is whatever the caller passes; the canvas
/// passes world pixels, with a threshold of the configured screen pixels divided by the zoom, so a snap
/// feels the same at every zoom. A block's leading edge, centre and trailing edge are each checked.
/// </remarks>
public static class CanvasSnapCalculator
{
    public static CanvasSnapResult FindSnap(double position, double size, IReadOnlyList<double> guides, double threshold)
    {
        if (guides.Count == 0 || threshold <= 0)
            return new CanvasSnapResult(null, 0);

        double? bestGuide = null;
        var bestDist = double.MaxValue;
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
                    bestDist = dist;
                    bestGuide = guide;
                    bestOffset = offset;
                }
            }
        }

        return bestDist <= threshold ? new CanvasSnapResult(bestGuide, bestOffset) : new CanvasSnapResult(null, 0);
    }

    /// <summary>The snapped leading-edge position, or <paramref name="position"/> when nothing is in range.</summary>
    public static double Snap(double position, double size, IReadOnlyList<double> guides, double threshold)
    {
        var result = FindSnap(position, size, guides, threshold);
        return result.Guide is { } guide ? guide - result.Offset : position;
    }

    /// <summary>The guide matched, for drawing the guide line, or null.</summary>
    public static double? ActiveGuide(double position, double size, IReadOnlyList<double> guides, double threshold) =>
        FindSnap(position, size, guides, threshold).Guide;
}

/// <summary>The guide lines other blocks offer.</summary>
public sealed record SnapGuides(IReadOnlyList<double> X, IReadOnlyList<double> Y)
{
    public static SnapGuides Empty { get; } = new([], []);
}

/// <summary>Collects snap guides from the blocks around a drag.</summary>
public static class SnapGuideCollector
{
    /// <summary>
    /// Left, centre and right of every other block on X; top, middle and bottom on Y.
    /// </summary>
    /// <param name="nodes">The board's blocks.</param>
    /// <param name="exclude">The blocks being dragged, which must not snap to themselves.</param>
    /// <param name="nearOnly">
    /// When set, only blocks intersecting it contribute - so a 2,000-block board does not build 6,000 guides
    /// for every drag.
    /// </param>
    public static SnapGuides Collect(IEnumerable<CanvasNode> nodes, IReadOnlySet<Guid> exclude, WorldRect? nearOnly = null)
    {
        var xs = new SortedSet<double>();
        var ys = new SortedSet<double>();

        foreach (var n in nodes)
        {
            if (exclude.Contains(n.Id)) continue;
            var r = CanvasHitTester.RectOf(n);
            if (nearOnly is { } near && !r.Intersects(near)) continue;

            xs.Add(r.X); xs.Add(r.CenterX); xs.Add(r.Right);
            ys.Add(r.Y); ys.Add(r.CenterY); ys.Add(r.Bottom);
        }

        return new SnapGuides(xs.ToList(), ys.ToList());
    }

    /// <summary>Rounds to the nearest grid line.</summary>
    public static double GridSnap(double value, double gridSize) =>
        gridSize <= 0 ? value : Math.Round(value / gridSize, MidpointRounding.AwayFromZero) * gridSize;
}

/// <summary>
/// Dragging a resize handle.
/// </summary>
/// <remarks>
/// Copied from Ben.Video.Editor's CalloutResizeMath, in world pixels, with a minimum per axis (a message box
/// is wider than it is tall) and an aspect lock for corner handles. The edge opposite the handle stays fixed;
/// dragging past the minimum holds at the minimum instead of flipping the box inside out.
/// </remarks>
public static class RectResizeMath
{
    /// <summary>The handle keys, in the order the overlay renders them; also the DOM's data-bc-handle values.</summary>
    public static readonly string[] HandleKeys = ["tl", "t", "tr", "r", "br", "b", "bl", "l"];

    public static WorldRect ApplyResize(WorldRect orig, string handle, double dx, double dy, double minWidth, double minHeight) =>
        ApplyResize(orig, handle, dx, dy, minWidth, minHeight, keepAspect: false);

    public static WorldRect ApplyResize(WorldRect orig, string handle, double dx, double dy, double minWidth, double minHeight, bool keepAspect)
    {
        var left = orig.X;
        var top = orig.Y;
        var right = orig.Right;
        var bottom = orig.Bottom;

        var movesLeft = handle is "tl" or "l" or "bl";
        var movesRight = handle is "tr" or "r" or "br";
        var movesTop = handle is "tl" or "t" or "tr";
        var movesBottom = handle is "bl" or "b" or "br";

        if (movesLeft) left = Math.Min(left + dx, right - minWidth);
        if (movesRight) right = Math.Max(right + dx, left + minWidth);
        if (movesTop) top = Math.Min(top + dy, bottom - minHeight);
        if (movesBottom) bottom = Math.Max(bottom + dy, top + minHeight);

        var result = new WorldRect(left, top, right - left, bottom - top);

        var isCorner = handle is "tl" or "tr" or "br" or "bl";
        if (!keepAspect || !isCorner || orig.Width <= 0 || orig.Height <= 0) return result;

        // Hold the original aspect: the axis that changed more (relatively) leads, the other follows,
        // then both minimums are re-applied along that aspect.
        var aspect = orig.Width / orig.Height;
        var scale = Math.Max(result.Width / orig.Width, result.Height / orig.Height);
        var width = orig.Width * scale;
        var height = width / aspect;

        var minScale = Math.Max(minWidth / orig.Width, minHeight / orig.Height);
        if (scale < minScale)
        {
            width = orig.Width * minScale;
            height = width / aspect;
        }

        var x = movesLeft ? orig.Right - width : orig.X;
        var y = movesTop ? orig.Bottom - height : orig.Y;
        return new WorldRect(x, y, width, height);
    }

    public static string CursorFor(string handle) => handle switch
    {
        "tl" or "br" => "nwse-resize",
        "tr" or "bl" => "nesw-resize",
        "t" or "b" => "ns-resize",
        "l" or "r" => "ew-resize",
        _ => "default",
    };

    /// <summary>Whether a handle is offered for a block that resizes on these axes.</summary>
    public static bool AllowedFor(string handle, bool resizableWidth, bool resizableHeight) => handle switch
    {
        "l" or "r" => resizableWidth,
        "t" or "b" => resizableHeight,
        "tl" or "tr" or "br" or "bl" => resizableWidth && resizableHeight,
        _ => false,
    };
}
