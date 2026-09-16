using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Geometry;

/// <summary>A connector's resolved cubic curve.</summary>
public readonly record struct EdgePath(CanvasPoint P0, CanvasPoint P1, CanvasPoint P2, CanvasPoint P3, CanvasSide FromSide, CanvasSide ToSide)
{
    /// <summary>The SVG path, with every number written with a dot whatever the culture.</summary>
    public string ToSvgPath() =>
        $"M {CssNumber.F(P0.X)} {CssNumber.F(P0.Y)} C {CssNumber.F(P1.X)} {CssNumber.F(P1.Y)}, {CssNumber.F(P2.X)} {CssNumber.F(P2.Y)}, {CssNumber.F(P3.X)} {CssNumber.F(P3.Y)}";

    public CanvasPoint PointAt(double t)
    {
        var u = 1 - t;
        var a = u * u * u;
        var b = 3 * u * u * t;
        var c = 3 * u * t * t;
        var d = t * t * t;
        return new(
            a * P0.X + b * P1.X + c * P2.X + d * P3.X,
            a * P0.Y + b * P1.Y + c * P2.Y + d * P3.Y);
    }

    /// <summary>Where the connector's label sits.</summary>
    public CanvasPoint MidPoint => PointAt(0.5);

    /// <summary>The direction the curve arrives in, in degrees. Informational; arrowheads use SVG orient=auto.</summary>
    public double EndTangentAngleDeg => Math.Atan2(P3.Y - P2.Y, P3.X - P2.X) * 180 / Math.PI;
}

/// <summary>
/// Where a connector runs: which sides it leaves and enters, and the curve between.
/// </summary>
public static class EdgeGeometry
{
    /// <summary>
    /// Resolves a connector between two rectangles. A side left null is chosen from where the blocks sit;
    /// an explicit side overrides its own end only.
    /// </summary>
    public static EdgePath Resolve(WorldRect from, WorldRect to, CanvasSide? fromSide, CanvasSide? toSide)
    {
        var (autoFrom, autoTo) = AutoSides(from, to);
        var fs = fromSide ?? autoFrom;
        var ts = toSide ?? autoTo;

        var p0 = Anchor(from, fs);
        var p3 = Anchor(to, ts);
        var distance = Math.Sqrt(Math.Pow(p3.X - p0.X, 2) + Math.Pow(p3.Y - p0.Y, 2));
        var offset = Math.Max(40, 0.4 * distance);

        var (nfx, nfy) = Normal(fs);
        var (ntx, nty) = Normal(ts);

        return new EdgePath(
            p0,
            new CanvasPoint(p0.X + nfx * offset, p0.Y + nfy * offset),
            new CanvasPoint(p3.X + ntx * offset, p3.Y + nty * offset),
            p3,
            fs,
            ts);
    }

    /// <summary>Overlapping blocks run bottom to top; otherwise the dominant axis decides.</summary>
    public static (CanvasSide From, CanvasSide To) AutoSides(WorldRect from, WorldRect to)
    {
        if (from.Intersects(to)) return (CanvasSide.Bottom, CanvasSide.Top);

        var dx = to.CenterX - from.CenterX;
        var dy = to.CenterY - from.CenterY;

        if (Math.Abs(dx) >= Math.Abs(dy))
            return dx > 0 ? (CanvasSide.Right, CanvasSide.Left) : (CanvasSide.Left, CanvasSide.Right);

        return dy > 0 ? (CanvasSide.Bottom, CanvasSide.Top) : (CanvasSide.Top, CanvasSide.Bottom);
    }

    /// <summary>The midpoint of a side.</summary>
    public static CanvasPoint Anchor(WorldRect r, CanvasSide side) => side switch
    {
        CanvasSide.Top => new(r.CenterX, r.Y),
        CanvasSide.Right => new(r.Right, r.CenterY),
        CanvasSide.Bottom => new(r.CenterX, r.Bottom),
        _ => new(r.X, r.CenterY),
    };

    private static (double X, double Y) Normal(CanvasSide side) => side switch
    {
        CanvasSide.Top => (0, -1),
        CanvasSide.Right => (1, 0),
        CanvasSide.Bottom => (0, 1),
        _ => (-1, 0),
    };
}

/// <summary>
/// Whether a point is on a connector.
/// </summary>
/// <remarks>
/// A curve cannot be hit-tested as a rectangle. It is sampled into short segments and the nearest distance
/// compared with a generous tolerance - the same reason the video editor draws a wide invisible hit shape
/// over a thin visible line: a two-pixel line is impossible to click, and impossible to tap.
/// </remarks>
public static class BezierHitTester
{
    public static double DistanceTo(EdgePath path, CanvasPoint p, int samples = 32)
    {
        var best = double.MaxValue;
        var prev = path.PointAt(0);
        for (var i = 1; i <= samples; i++)
        {
            var next = path.PointAt((double)i / samples);
            best = Math.Min(best, SegmentDistance(prev, next, p));
            prev = next;
        }
        return best;
    }

    /// <summary>The closest connector within <paramref name="tolerance"/> world units, or null.</summary>
    public static Guid? HitTest(IReadOnlyList<(Guid Id, EdgePath Path)> edges, CanvasPoint p, double tolerance)
    {
        Guid? best = null;
        var bestDistance = double.MaxValue;

        foreach (var (id, path) in edges)
        {
            var d = DistanceTo(path, p);
            if (d <= tolerance && d < bestDistance)
            {
                best = id;
                bestDistance = d;
            }
        }

        return best;
    }

    private static double SegmentDistance(CanvasPoint a, CanvasPoint b, CanvasPoint p)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var lengthSq = dx * dx + dy * dy;
        var t = lengthSq == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lengthSq, 0, 1);
        var cx = a.X + t * dx;
        var cy = a.Y + t * dy;
        return Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
    }
}
