using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Tests.Geometry;

/// <summary>
/// How a connector runs between two blocks: a curve, a straight line, or right angles.
/// </summary>
/// <remarks>
/// <para><b>Why this matters more than it looks.</b> The family tree Ben sent is drawn entirely in
/// right angles — a parent line drops, turns, and runs along to each child. A curve cannot express
/// that, and a board of forty curved connectors reads as spaghetti where the same board in elbows
/// reads as a tree.</para>
///
/// <para><b>One function answers it, because three callers ask.</b> <c>EdgeGeometry.Resolve</c> is
/// called when the board draws, when the published picture is painted, and when a click is tested
/// against a connector. A route the drawing knew about and the hit-test did not would give a line you
/// can see and cannot select — so the route lives in the path itself and all three follow.</para>
///
/// <para><b>Curve is first in the enum</b>, so every board written before this opens drawn exactly as
/// it was.</para>
/// </remarks>
public sealed class EdgeRouteTests
{
    private static readonly WorldRect Left = new(0, 0, 100, 60);
    private static readonly WorldRect Right = new(400, 300, 100, 60);

    private static EdgePath Path(EdgeRoute route, WorldRect? from = null, WorldRect? to = null) =>
        EdgeGeometry.Resolve(from ?? Left, to ?? Right, null, null, route);

    [Fact]
    public void A_connector_curves_unless_it_says_otherwise()
    {
        Assert.Equal(EdgeRoute.Curve, new CanvasEdge().Route);
    }

    /// <summary>
    /// The curve is untouched: its control points still stand off the sides, which is what every
    /// existing board looks like.
    /// </summary>
    [Fact]
    public void A_curve_is_what_it_always_was()
    {
        var path = Path(EdgeRoute.Curve);

        Assert.Empty(path.Corners);
        Assert.NotEqual(path.P0.X, path.P1.X);   // the control point stands off the side
        Assert.Contains("C ", path.ToSvgPath());
    }

    /// <summary>
    /// A straight line is straight: the control points sit ON the ends, so the cubic degenerates to a
    /// line and every reader — the SVG, the canvas painter, the sampler — draws the same thing.
    /// </summary>
    [Fact]
    public void A_straight_route_is_a_line()
    {
        var path = Path(EdgeRoute.Straight);

        Assert.Empty(path.Corners);
        Assert.Equal(path.P0.X, path.P1.X, 6);
        Assert.Equal(path.P0.Y, path.P1.Y, 6);
        Assert.Equal(path.P3.X, path.P2.X, 6);
        Assert.Equal(path.P3.Y, path.P2.Y, 6);

        // Half way along a straight line is half way between its ends.
        var mid = path.PointAt(0.5);
        Assert.Equal((path.P0.X + path.P3.X) / 2, mid.X, 6);
        Assert.Equal((path.P0.Y + path.P3.Y) / 2, mid.Y, 6);
    }

    /// <summary>
    /// An elbow turns only at right angles. Asserted as a property of every segment rather than as a
    /// fixed list of points, because the corner count depends on which sides it leaves and enters —
    /// and the property is what makes it read as a tree.
    /// </summary>
    [Fact]
    public void An_elbow_route_turns_only_at_right_angles()
    {
        var path = Path(EdgeRoute.Elbow);

        Assert.NotEmpty(path.Corners);

        var points = new List<CanvasPoint> { path.P0 };
        points.AddRange(path.Corners);
        points.Add(path.P3);

        for (var i = 1; i < points.Count; i++)
        {
            var dx = Math.Abs(points[i].X - points[i - 1].X);
            var dy = Math.Abs(points[i].Y - points[i - 1].Y);
            Assert.True(dx < 0.001 || dy < 0.001,
                $"segment {i} runs diagonally: ({points[i - 1].X},{points[i - 1].Y}) to ({points[i].X},{points[i].Y})");
        }
    }

    /// <summary>An elbow draws as line segments, not as a curve.</summary>
    [Fact]
    public void An_elbow_is_drawn_with_line_segments()
    {
        var svg = Path(EdgeRoute.Elbow).ToSvgPath();

        Assert.Contains("L ", svg);
        Assert.DoesNotContain("C ", svg);
    }

    /// <summary>
    /// Two blocks side by side elbow with one turn each way — the parent-to-child shape the family
    /// tree is made of.
    /// </summary>
    [Fact]
    public void A_block_below_and_across_elbows_through_the_gap()
    {
        var path = EdgeGeometry.Resolve(
            new WorldRect(0, 0, 100, 60), new WorldRect(300, 200, 100, 60),
            CanvasSide.Bottom, CanvasSide.Top, EdgeRoute.Elbow);

        // Leaves downward and arrives downward, so the turns are in between.
        Assert.Equal(path.P0.X, path.Corners[0].X, 6);
        Assert.Equal(path.P3.X, path.Corners[^1].X, 6);
    }

    /// <summary>
    /// The midpoint is on the line, whatever the route — it is where a label and the new midpoint
    /// icon sit, and a label floating off a connector is worse than no label.
    /// </summary>
    [Theory]
    [InlineData(EdgeRoute.Curve)]
    [InlineData(EdgeRoute.Straight)]
    [InlineData(EdgeRoute.Elbow)]
    public void The_midpoint_is_on_the_connector(EdgeRoute route)
    {
        var path = Path(route);

        Assert.True(BezierHitTester.DistanceTo(path, path.MidPoint) < 0.5,
            "the midpoint has to sit on the line the reader sees");
    }

    /// <summary>
    /// A click near a corner finds the connector. An elbow sampled as though it were still a cubic
    /// would cut the corner and leave the turn unclickable — a line you can see and cannot select.
    /// </summary>
    [Fact]
    public void Elbow_hit_test_finds_the_corner()
    {
        var path = EdgeGeometry.Resolve(
            new WorldRect(0, 0, 100, 60), new WorldRect(300, 200, 100, 60),
            CanvasSide.Bottom, CanvasSide.Top, EdgeRoute.Elbow);

        var corner = path.Corners[0];

        Assert.True(BezierHitTester.DistanceTo(path, corner) < 0.5,
            "the corner itself is on the connector");
        Assert.True(BezierHitTester.DistanceTo(path, new CanvasPoint(corner.X + 2, corner.Y + 2)) < 8,
            "a click two units off the corner still finds it");
    }

    /// <summary>Every route keeps the ends where the sides say they are.</summary>
    [Theory]
    [InlineData(EdgeRoute.Curve)]
    [InlineData(EdgeRoute.Straight)]
    [InlineData(EdgeRoute.Elbow)]
    public void Every_route_starts_and_ends_on_its_sides(EdgeRoute route)
    {
        var path = EdgeGeometry.Resolve(Left, Right, CanvasSide.Right, CanvasSide.Left, route);

        Assert.Equal(EdgeGeometry.Anchor(Left, CanvasSide.Right).X, path.P0.X, 6);
        Assert.Equal(EdgeGeometry.Anchor(Right, CanvasSide.Left).X, path.P3.X, 6);
    }
}
