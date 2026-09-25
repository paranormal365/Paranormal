using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Geometry;

/// <summary>A connector's resolved cubic curve.</summary>
public readonly record struct EdgePath(
    CanvasPoint P0, CanvasPoint P1, CanvasPoint P2, CanvasPoint P3, CanvasSide FromSide, CanvasSide ToSide,
    IReadOnlyList<CanvasPoint>? Corners = null)
{
    /// <summary>
    /// The turns an elbow makes, in order, between <see cref="P0"/> and <see cref="P3"/>. Empty for a
    /// curve and for a straight line.
    /// </summary>
    /// <remarks>
    /// On the path rather than worked out by each reader, because three of them read it — the board's
    /// SVG, the published picture's canvas and the hit-test — and a corner one knew about and another
    /// did not would give a line you can see and cannot select.
    /// </remarks>
    public IReadOnlyList<CanvasPoint> Corners { get; init; } = Corners ?? [];

    /// <summary>Whether this path is drawn as straight segments rather than as a curve.</summary>
    public bool IsPolyline => Corners.Count > 0;

    /// <summary>The SVG path, with every number written with a dot whatever the culture.</summary>
    public string ToSvgPath()
    {
        var head = $"M {CssNumber.F(P0.X)} {CssNumber.F(P0.Y)}";

        if (!IsPolyline)
            return head + $" C {CssNumber.F(P1.X)} {CssNumber.F(P1.Y)}, {CssNumber.F(P2.X)} {CssNumber.F(P2.Y)}, {CssNumber.F(P3.X)} {CssNumber.F(P3.Y)}";

        var parts = new List<string>(Corners.Count + 1);
        foreach (var corner in Corners) parts.Add($"L {CssNumber.F(corner.X)} {CssNumber.F(corner.Y)}");
        parts.Add($"L {CssNumber.F(P3.X)} {CssNumber.F(P3.Y)}");
        return head + " " + string.Join(" ", parts);
    }

    /// <summary>
    /// A point along the connector, where 0 is the start and 1 the end.
    /// </summary>
    /// <remarks>
    /// A polyline is walked by LENGTH, not by segment index, so half way along really is half way —
    /// which is what puts a label and a midpoint icon where a reader expects them on an elbow whose
    /// segments are wildly different lengths.
    /// </remarks>
    public CanvasPoint PointAt(double t)
    {
        if (IsPolyline) return AlongPolyline(t);

        var u = 1 - t;
        var a = u * u * u;
        var b = 3 * u * u * t;
        var c = 3 * u * t * t;
        var d = t * t * t;
        return new(
            a * P0.X + b * P1.X + c * P2.X + d * P3.X,
            a * P0.Y + b * P1.Y + c * P2.Y + d * P3.Y);
    }

    /// <summary>Every point of the polyline, ends included.</summary>
    public IReadOnlyList<CanvasPoint> Points()
    {
        var points = new List<CanvasPoint>(Corners.Count + 2) { P0 };
        points.AddRange(Corners);
        points.Add(P3);
        return points;
    }

    private CanvasPoint AlongPolyline(double t)
    {
        var points = Points();
        var lengths = new double[points.Count - 1];
        var total = 0d;

        for (var i = 0; i < lengths.Length; i++)
        {
            lengths[i] = Distance(points[i], points[i + 1]);
            total += lengths[i];
        }

        if (total <= 0) return P0;

        var target = Math.Clamp(t, 0, 1) * total;
        var walked = 0d;

        for (var i = 0; i < lengths.Length; i++)
        {
            if (walked + lengths[i] >= target)
            {
                var into = lengths[i] <= 0 ? 0 : (target - walked) / lengths[i];
                return new(
                    points[i].X + (points[i + 1].X - points[i].X) * into,
                    points[i].Y + (points[i + 1].Y - points[i].Y) * into);
            }
            walked += lengths[i];
        }

        return P3;
    }

    private static double Distance(CanvasPoint a, CanvasPoint b) =>
        Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));

    /// <summary>Where the connector's label sits.</summary>
    public CanvasPoint MidPoint => PointAt(0.5);

    /// <summary>The direction the connector arrives in, in degrees.</summary>
    /// <remarks>
    /// A polyline arrives along its LAST SEGMENT, not along P2-P3 — those control points are unused
    /// on an elbow, and a marker aimed by them would point somewhere the line never went.
    /// </remarks>
    public double EndTangentAngleDeg
    {
        get
        {
            var from = IsPolyline ? Corners[^1] : P2;
            return Math.Atan2(P3.Y - from.Y, P3.X - from.X) * 180 / Math.PI;
        }
    }

    /// <summary>The point a marker at the START should be aimed away from.</summary>
    public CanvasPoint StartControl => IsPolyline ? Corners[0] : P1;

    /// <summary>The point a marker at the END should be aimed away from.</summary>
    public CanvasPoint EndControl => IsPolyline ? Corners[^1] : P2;
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
    public static EdgePath Resolve(
        WorldRect from, WorldRect to, CanvasSide? fromSide, CanvasSide? toSide, EdgeRoute route = EdgeRoute.Curve)
    {
        var (autoFrom, autoTo) = AutoSides(from, to);
        var fs = fromSide ?? autoFrom;
        var ts = toSide ?? autoTo;

        var p0 = Anchor(from, fs);
        var p3 = Anchor(to, ts);

        // A straight line is the cubic with its controls ON its ends, so it degenerates to a line and
        // every reader draws the same thing without a second code path.
        if (route == EdgeRoute.Straight) return new EdgePath(p0, p0, p3, p3, fs, ts);

        if (route == EdgeRoute.Elbow) return new EdgePath(p0, p0, p3, p3, fs, ts, Elbow(p0, p3, fs, ts));
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

    /// <summary>
    /// The turns a right-angled connector makes.
    /// </summary>
    /// <remarks>
    /// <para>The rule is simple and gives the tree shape Ben's family chart is drawn in: leave along
    /// the side's own direction, arrive along the other side's, and meet in the middle of whichever
    /// axis the two ends disagree on.</para>
    ///
    /// <para>Two ends facing the same axis need two turns (down, across, down); ends facing different
    /// axes need one. A pair already lined up on the axis needs none at all, and returning an empty
    /// list there is right — the straight segment IS the connector, and a corner on top of it would
    /// be a turn of zero degrees that the hit-test would still have to walk.</para>
    /// </remarks>
    private static List<CanvasPoint> Elbow(CanvasPoint p0, CanvasPoint p3, CanvasSide fs, CanvasSide ts)
    {
        var fromVertical = fs is CanvasSide.Top or CanvasSide.Bottom;
        var toVertical = ts is CanvasSide.Top or CanvasSide.Bottom;
        var corners = new List<CanvasPoint>();

        if (fromVertical && toVertical)
        {
            if (Math.Abs(p0.X - p3.X) < 0.001) return corners;   // already lined up: a straight drop
            var midY = (p0.Y + p3.Y) / 2;
            corners.Add(new CanvasPoint(p0.X, midY));
            corners.Add(new CanvasPoint(p3.X, midY));
        }
        else if (!fromVertical && !toVertical)
        {
            if (Math.Abs(p0.Y - p3.Y) < 0.001) return corners;
            var midX = (p0.X + p3.X) / 2;
            corners.Add(new CanvasPoint(midX, p0.Y));
            corners.Add(new CanvasPoint(midX, p3.Y));
        }
        else if (fromVertical)
        {
            // Out vertically, in horizontally: one turn, under the start and level with the end.
            corners.Add(new CanvasPoint(p0.X, p3.Y));
        }
        else
        {
            corners.Add(new CanvasPoint(p3.X, p0.Y));
        }

        return corners;
    }

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
        // A polyline is measured against its OWN segments rather than sampled. Sampling would cut
        // every corner, leaving the turn of an elbow unclickable — a line you can see and cannot
        // select — and it is both exact and cheaper this way.
        if (path.IsPolyline)
        {
            var points = path.Points();
            var nearest = double.MaxValue;
            for (var i = 1; i < points.Count; i++)
                nearest = Math.Min(nearest, SegmentDistance(points[i - 1], points[i], p));
            return nearest;
        }

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
