using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Geometry;

/// <summary>Screen and world stay in step through zoom, pinch and fit.</summary>
public sealed class ViewportMathTests
{
    [Fact]
    public void Zooming_about_a_point_keeps_that_world_point_under_the_cursor()
    {
        var vp = new CanvasViewport(120, -40, 1.3);
        var before = ViewportMath.ClientToWorld(vp, 400, 300);
        var zoomed = ViewportMath.ZoomAboutPoint(vp, 400, 300, 1.8);
        var after = ViewportMath.ClientToWorld(zoomed, 400, 300);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }

    [Fact]
    public void Zoom_is_clamped_between_point_one_and_four()
    {
        Assert.Equal(4, ViewportMath.ZoomAboutPoint(CanvasViewport.Identity, 0, 0, 100).Zoom);
        Assert.Equal(0.1, ViewportMath.ZoomAboutPoint(CanvasViewport.Identity, 0, 0, 0.0001).Zoom);
    }

    [Fact]
    public void Screen_to_world_and_back_is_identity()
    {
        var vp = new CanvasViewport(-250.5, 77, 0.35);
        var world = ViewportMath.ClientToWorld(vp, 812, 19);
        var client = ViewportMath.WorldToClient(vp, world.X, world.Y);
        Assert.Equal(812, client.X, 6);
        Assert.Equal(19, client.Y, 6);
    }

    [Fact]
    public void Delta_is_divided_by_zoom()
    {
        var delta = ViewportMath.ClientDeltaToWorld(new CanvasViewport(0, 0, 0.5), 120, 80);
        Assert.Equal(240, delta.X);
        Assert.Equal(160, delta.Y);
        Assert.Equal(12, ViewportMath.ScreenPxToWorld(new CanvasViewport(0, 0, 0.5), 6));
    }

    [Fact]
    public void Fit_puts_the_whole_content_inside_the_view_with_margin()
    {
        var bounds = new WorldRect(-1000, 200, 3000, 1200);
        var vp = ViewportMath.FitToContent(bounds, 1280, 800);
        var topLeft = ViewportMath.WorldToClient(vp, bounds.X, bounds.Y);
        var bottomRight = ViewportMath.WorldToClient(vp, bounds.Right, bounds.Bottom);
        var margin = Math.Max(40, 0.05 * 800);

        Assert.True(topLeft.X >= margin - 0.001 && topLeft.Y >= margin - 0.001);
        Assert.True(bottomRight.X <= 1280 - margin + 0.001 && bottomRight.Y <= 800 - margin + 0.001);
    }

    [Fact]
    public void Fit_of_an_empty_board_is_zoom_one_at_origin() =>
        Assert.Equal(CanvasViewport.Identity, ViewportMath.FitToContent(WorldRect.Empty, 1280, 800));

    [Fact]
    public void The_world_point_under_the_focal_point_does_not_move()
    {
        var start = new CanvasViewport(30, 60, 1);
        var before = ViewportMath.ClientToWorld(start, 200, 300);
        var pinched = ViewportMath.PinchFrom(start, 100, 200, 200, 300, 200, 300);
        var after = ViewportMath.ClientToWorld(pinched, 200, 300);
        Assert.Equal(2, pinched.Zoom, 6);
        Assert.Equal(before.X, after.X, 6);
        Assert.Equal(before.Y, after.Y, 6);
    }

    [Fact]
    public void Moving_both_fingers_pans_by_the_focal_delta()
    {
        var start = new CanvasViewport(0, 0, 1);
        var moved = ViewportMath.PinchFrom(start, 100, 100, 200, 300, 250, 280);
        Assert.Equal(50, moved.PanX, 6);
        Assert.Equal(-20, moved.PanY, 6);
    }

    [Fact]
    public void A_pinch_past_the_clamp_stops_at_the_clamp() =>
        Assert.Equal(4, ViewportMath.PinchFrom(CanvasViewport.Identity, 10, 1000, 0, 0, 0, 0).Zoom);
}

/// <summary>A stored view that was hand-edited or written by an older build still opens a usable board.</summary>
public sealed class ViewSnapshotTests
{
    [Fact]
    public void A_hand_edited_view_with_NaN_gives_a_usable_viewport()
    {
        var snapshot = ViewSnapshot.Deserialise("{\"panX\":\"NaN\",\"panY\":12,\"zoom\":\"Infinity\"}");
        var vp = snapshot!.Apply();
        Assert.Equal(0, vp.PanX);
        Assert.Equal(12, vp.PanY);
        Assert.Equal(1, vp.Zoom);
    }

    [Fact]
    public void Missing_fields_keep_defaults()
    {
        var vp = ViewSnapshot.Deserialise("{\"zoom\":2.5}")!.Apply();
        Assert.Equal(new CanvasViewport(0, 0, 2.5), vp);
    }

    [Fact]
    public void Out_of_range_zoom_is_clamped() => Assert.Equal(4, ViewSnapshot.Deserialise("{\"zoom\":40}")!.Apply().Zoom);

    [Fact]
    public void Unreadable_json_gives_null() => Assert.Null(ViewSnapshot.Deserialise("{not json"));

    [Fact]
    public void A_view_round_trips()
    {
        var vp = new CanvasViewport(-12.5, 40, 0.75);
        Assert.Equal(vp, ViewSnapshot.Deserialise(ViewSnapshot.Serialise(vp))!.Apply());
    }
}

/// <summary>Numbers written into CSS and SVG use a dot whatever the culture.</summary>
public sealed class CssNumberTests
{
    [Fact]
    public void A_French_culture_still_writes_a_dot() => TestBoards.InCulture("fr-FR", () => Assert.Equal("2.5", CssNumber.F(2.5)));

    [Fact]
    public void A_non_finite_number_is_zero() => Assert.Equal("0", CssNumber.F(double.NaN));

    [Fact]
    public void Path_string_uses_dots_under_fr_FR() => TestBoards.InCulture("fr-FR", () =>
    {
        var path = EdgeGeometry.Resolve(new WorldRect(0.5, 0.5, 100, 50), new WorldRect(400.25, 0, 100, 50), null, null).ToSvgPath();
        Assert.DoesNotMatch(@"\d,\d", path);
        Assert.StartsWith("M 100.5 25.5 C", path);
    });
}

/// <summary>What a point or a marquee on the board lands on.</summary>
public sealed class CanvasHitTesterTests
{
    [Fact]
    public void The_front_most_of_two_overlapping_nodes_wins()
    {
        var back = TestBoards.Node(x: 0, y: 0);
        var front = TestBoards.Node(x: 50, y: 50);
        back.Z = 1;
        front.Z = 2;
        Assert.Same(front, CanvasHitTester.TopmostNodeAt([back, front], 100, 100));
        Assert.Same(front, CanvasHitTester.TopmostNodeAt([front, back], 100, 100));
    }

    [Fact]
    public void A_point_outside_every_node_hits_nothing() =>
        Assert.Null(CanvasHitTester.TopmostNodeAt([TestBoards.Node()], 5000, 5000));

    [Fact]
    public void Marquee_selects_a_node_it_only_touches()
    {
        var node = TestBoards.Node(x: 100, y: 100);
        Assert.Equal([node.Id], CanvasHitTester.NodesIn([node], new WorldRect(0, 0, 105, 105)));
        Assert.Empty(CanvasHitTester.NodesIn([node], new WorldRect(0, 0, 50, 50)));
    }

    [Fact]
    public void A_locked_node_is_still_hit_for_selection()
    {
        var node = TestBoards.Node();
        node.Locked = true;
        Assert.Same(node, CanvasHitTester.TopmostNodeAt([node], 10, 10));
    }

    [Fact]
    public void Group_is_hit_only_outside_its_members()
    {
        var document = new CanvasDocument();
        var group = new CanvasGroup { X = 0, Y = 0, Width = 1000, Height = 1000 };
        var member = TestBoards.Node(x: 100, y: 100);
        member.GroupId = group.Id;
        document.Groups.Add(group);
        document.Nodes.Add(member);

        Assert.Equal(new CanvasHit(CanvasHitKind.Node, member.Id), CanvasHitTester.HitAt(document, 150, 150));
        Assert.Equal(new CanvasHit(CanvasHitKind.Group, group.Id), CanvasHitTester.HitAt(document, 900, 900));
        Assert.Equal(CanvasHit.None, CanvasHitTester.HitAt(document, 2000, 2000));
    }
}

/// <summary>Only blocks near the view render on a very large board.</summary>
public sealed class VisibleNodeFilterTests
{
    private static readonly WorldRect View = new(0, 0, 1000, 800);

    [Fact]
    public void A_node_just_outside_the_padding_is_skipped() =>
        Assert.Empty(VisibleNodeFilter.Visible([TestBoards.Node(x: 1501, y: 0, width: 220, height: 140)], View));

    [Fact]
    public void A_node_inside_the_padding_is_kept() =>
        Assert.Single(VisibleNodeFilter.Visible([TestBoards.Node(x: 1400, y: 0)], View));
}

/// <summary>Group membership follows where a block's centre is.</summary>
public sealed class GroupMembershipTests
{
    private static readonly CanvasGroup Low = new() { X = 0, Y = 0, Width = 600, Height = 600, Z = 1 };
    private static readonly CanvasGroup High = new() { X = 300, Y = 300, Width = 600, Height = 600, Z = 2 };

    [Fact]
    public void A_node_whose_centre_is_inside_a_group_joins_it() =>
        Assert.Equal(Low.Id, GroupMembership.GroupContaining([Low], new WorldRect(500, 500, 200, 100)));

    [Fact]
    public void A_node_with_its_centre_outside_leaves() =>
        Assert.Null(GroupMembership.GroupContaining([Low], new WorldRect(550, 550, 200, 200)));

    [Fact]
    public void The_topmost_of_overlapping_groups_wins() =>
        Assert.Equal(High.Id, GroupMembership.GroupContaining([High, Low], new WorldRect(400, 400, 100, 100)));

    [Fact]
    public void An_excluded_group_is_ignored() =>
        Assert.Equal(Low.Id, GroupMembership.GroupContaining([Low, High], new WorldRect(400, 400, 100, 100), new HashSet<Guid> { High.Id }));
}

/// <summary>Alignment snapping, ported from the video editor's cases in world pixels.</summary>
public sealed class CanvasSnapCalculatorTests
{
    private static readonly IReadOnlyList<double> Guides = [0, 500, 1000, 333.333];

    [Fact]
    public void Centre_within_threshold_snaps_to_the_centre_guide()
    {
        var result = CanvasSnapCalculator.FindSnap(480, 40, Guides, 6);
        Assert.Equal(500, result.Guide);
        Assert.Equal(20, result.Offset, 5);
    }

    [Fact]
    public void Leading_edge_within_threshold_snaps_to_the_edge_guide()
    {
        var result = CanvasSnapCalculator.FindSnap(4, 200, Guides, 6);
        Assert.Equal(0, result.Guide);
        Assert.Equal(0, result.Offset);
    }

    [Fact]
    public void Trailing_edge_within_threshold_snaps_to_the_edge_guide()
    {
        var result = CanvasSnapCalculator.FindSnap(795, 200, Guides, 6);
        Assert.Equal(1000, result.Guide);
        Assert.Equal(200, result.Offset);
    }

    [Fact]
    public void Nothing_within_threshold_returns_no_guide()
    {
        var result = CanvasSnapCalculator.FindSnap(200, 20, Guides, 6);
        Assert.Null(result.Guide);
        Assert.Equal(0, result.Offset);
    }

    [Fact]
    public void A_point_with_no_size_checks_its_anchor_against_every_guide() =>
        Assert.Equal(333.333, CanvasSnapCalculator.FindSnap(336, 0, Guides, 6).Guide!.Value, 3);

    [Fact]
    public void Snap_returns_the_adjusted_leading_edge_when_the_centre_matches() =>
        Assert.Equal(480, CanvasSnapCalculator.Snap(482, 40, Guides, 6), 5);

    [Fact]
    public void Snap_with_no_match_returns_the_position_unchanged() =>
        Assert.Equal(200, CanvasSnapCalculator.Snap(200, 20, Guides, 6));

    [Fact]
    public void Active_guide_matches_find_snap() =>
        Assert.Equal(500, CanvasSnapCalculator.ActiveGuide(480, 40, Guides, 6));

    [Fact]
    public void Empty_guides_return_no_guide() => Assert.Null(CanvasSnapCalculator.FindSnap(500, 100, [], 6).Guide);

    [Fact]
    public void Zero_threshold_returns_no_guide() => Assert.Null(CanvasSnapCalculator.FindSnap(500, 0, Guides, 0).Guide);

    [Fact]
    public void A_tie_goes_to_the_leading_edge()
    {
        var result = CanvasSnapCalculator.FindSnap(0, 1000, Guides, 6);
        Assert.Equal(0, result.Guide);
        Assert.Equal(0, result.Offset);
    }
}

/// <summary>The guides a drag snaps to come from the other blocks.</summary>
public sealed class SnapGuideCollectorTests
{
    [Fact]
    public void The_dragged_node_is_not_its_own_guide()
    {
        var dragged = TestBoards.Node(x: 10, y: 10, width: 100, height: 100);
        var guides = SnapGuideCollector.Collect([dragged], new HashSet<Guid> { dragged.Id });
        Assert.Empty(guides.X);
        Assert.Empty(guides.Y);
    }

    [Fact]
    public void Guides_are_distinct_and_sorted()
    {
        var a = TestBoards.Node(x: 100, y: 0, width: 100, height: 100);
        var b = TestBoards.Node(x: 0, y: 0, width: 100, height: 100);
        var guides = SnapGuideCollector.Collect([a, b], new HashSet<Guid>());
        Assert.Equal([0, 50, 100, 150, 200], guides.X);
        Assert.Equal([0, 50, 100], guides.Y);
    }

    [Fact]
    public void Only_nodes_near_the_view_contribute()
    {
        var near = TestBoards.Node(x: 0, y: 0, width: 100, height: 100);
        var far = TestBoards.Node(x: 90000, y: 0, width: 100, height: 100);
        var guides = SnapGuideCollector.Collect([near, far], new HashSet<Guid>(), new WorldRect(-500, -500, 2000, 2000));
        Assert.DoesNotContain(90000, guides.X);
        Assert.Contains(0, guides.X);
    }

    [Theory]
    [InlineData(29, 20, 20)]
    [InlineData(31, 20, 40)]
    [InlineData(-9, 20, -0.0)]
    public void Grid_snap_rounds_to_the_nearest_cell(double value, double grid, double expected) =>
        Assert.Equal(expected, SnapGuideCollector.GridSnap(value, grid), 6);
}

/// <summary>Resize handles keep the opposite edge still and never collapse a block.</summary>
public sealed class RectResizeMathTests
{
    private static readonly WorldRect Box = new(100, 100, 300, 200);

    [Fact]
    public void Dragging_the_left_handle_keeps_the_right_edge_fixed()
    {
        var r = RectResizeMath.ApplyResize(Box, "l", -50, 0, 100, 80);
        Assert.Equal(50, r.X);
        Assert.Equal(Box.Right, r.Right);
    }

    [Fact]
    public void Dragging_the_right_handle_keeps_the_left_edge_fixed()
    {
        var r = RectResizeMath.ApplyResize(Box, "r", 60, 0, 100, 80);
        Assert.Equal(Box.X, r.X);
        Assert.Equal(360, r.Width);
    }

    [Theory]
    [InlineData("tl")]
    [InlineData("br")]
    [InlineData("l")]
    [InlineData("b")]
    public void A_box_cannot_collapse_below_its_minimum(string handle)
    {
        var sign = handle.Contains('t') || handle.Contains('l') ? 1 : -1;
        var r = RectResizeMath.ApplyResize(Box, handle, sign * 5000, sign * 5000, 120, 90);
        Assert.True(r.Width >= 120 - 1e-9);
        Assert.True(r.Height >= 90 - 1e-9);
    }

    [Fact]
    public void Shift_on_a_corner_keeps_the_aspect()
    {
        var r = RectResizeMath.ApplyResize(Box, "br", 300, 20, 100, 80, keepAspect: true);
        Assert.Equal(Box.Width / Box.Height, r.Width / r.Height, 6);
        Assert.Equal(Box.X, r.X);
        Assert.Equal(Box.Y, r.Y);
    }

    [Fact]
    public void Shift_on_a_top_left_corner_keeps_the_bottom_right_fixed()
    {
        var r = RectResizeMath.ApplyResize(Box, "tl", -150, 0, 100, 80, keepAspect: true);
        Assert.Equal(Box.Right, r.Right, 6);
        Assert.Equal(Box.Bottom, r.Bottom, 6);
        Assert.Equal(1.5, r.Width / r.Height, 6);
    }

    [Fact]
    public void Shift_on_an_edge_handle_is_ignored() =>
        Assert.Equal(RectResizeMath.ApplyResize(Box, "r", 60, 40, 100, 80), RectResizeMath.ApplyResize(Box, "r", 60, 40, 100, 80, keepAspect: true));

    [Fact]
    public void A_width_only_block_offers_only_left_and_right_handles() =>
        Assert.Equal(["r", "l"], RectResizeMath.HandleKeys.Where(h => RectResizeMath.AllowedFor(h, true, false)));

    [Fact]
    public void A_block_that_does_not_resize_offers_no_handles() =>
        Assert.DoesNotContain(RectResizeMath.HandleKeys, h => RectResizeMath.AllowedFor(h, false, false));

    [Theory]
    [InlineData("tl", "nwse-resize")]
    [InlineData("tr", "nesw-resize")]
    [InlineData("t", "ns-resize")]
    [InlineData("l", "ew-resize")]
    public void Each_handle_has_its_cursor(string handle, string cursor) => Assert.Equal(cursor, RectResizeMath.CursorFor(handle));
}

/// <summary>Connectors choose sensible sides and draw a smooth curve.</summary>
public sealed class EdgeGeometryTests
{
    [Fact]
    public void A_node_to_the_right_connects_right_to_left()
    {
        var path = EdgeGeometry.Resolve(new WorldRect(0, 0, 100, 100), new WorldRect(500, 20, 100, 100), null, null);
        Assert.Equal((CanvasSide.Right, CanvasSide.Left), (path.FromSide, path.ToSide));
    }

    [Fact]
    public void A_node_to_the_left_connects_left_to_right()
    {
        var path = EdgeGeometry.Resolve(new WorldRect(500, 0, 100, 100), new WorldRect(0, 20, 100, 100), null, null);
        Assert.Equal((CanvasSide.Left, CanvasSide.Right), (path.FromSide, path.ToSide));
    }

    [Fact]
    public void A_node_below_connects_bottom_to_top()
    {
        var path = EdgeGeometry.Resolve(new WorldRect(0, 0, 100, 100), new WorldRect(10, 600, 100, 100), null, null);
        Assert.Equal((CanvasSide.Bottom, CanvasSide.Top), (path.FromSide, path.ToSide));
    }

    [Fact]
    public void Overlapping_nodes_fall_back_to_bottom_to_top()
    {
        var path = EdgeGeometry.Resolve(new WorldRect(0, 0, 100, 100), new WorldRect(50, 50, 100, 100), null, null);
        Assert.Equal((CanvasSide.Bottom, CanvasSide.Top), (path.FromSide, path.ToSide));
    }

    [Fact]
    public void An_explicit_side_is_honoured()
    {
        var path = EdgeGeometry.Resolve(new WorldRect(0, 0, 100, 100), new WorldRect(500, 0, 100, 100), CanvasSide.Top, null);
        Assert.Equal(CanvasSide.Top, path.FromSide);
        Assert.Equal(CanvasSide.Left, path.ToSide);
        Assert.Equal(new CanvasPoint(50, 0), path.P0);
    }

    [Fact]
    public void Control_points_are_at_least_40px_out()
    {
        var path = EdgeGeometry.Resolve(new WorldRect(0, 0, 100, 100), new WorldRect(110, 0, 100, 100), null, null);
        Assert.True(path.P1.X - path.P0.X >= 40);
        Assert.True(path.P3.X - path.P2.X >= 40);
    }
}

/// <summary>A thin curve is still easy to click.</summary>
public sealed class BezierHitTesterTests
{
    private static readonly EdgePath Path = EdgeGeometry.Resolve(new WorldRect(0, 0, 100, 100), new WorldRect(600, 0, 100, 100), null, null);

    [Fact]
    public void A_point_within_eight_px_of_the_curve_hits()
    {
        var mid = Path.MidPoint;
        var id = Guid.NewGuid();
        Assert.Equal(id, BezierHitTester.HitTest([(id, Path)], new CanvasPoint(mid.X, mid.Y + 7), 8));
    }

    [Fact]
    public void A_point_far_away_is_not() =>
        Assert.Null(BezierHitTester.HitTest([(Guid.NewGuid(), Path)], new CanvasPoint(350, 400), 8));

    [Fact]
    public void The_nearer_of_two_curves_wins()
    {
        var other = EdgeGeometry.Resolve(new WorldRect(0, 20, 100, 100), new WorldRect(600, 20, 100, 100), null, null);
        var near = Guid.NewGuid();
        var far = Guid.NewGuid();
        var point = new CanvasPoint(Path.MidPoint.X, Path.MidPoint.Y + 2);
        Assert.Equal(near, BezierHitTester.HitTest([(far, other), (near, Path)], point, 30));
    }

    [Fact]
    public void The_midpoint_of_the_curve_is_where_the_label_sits()
    {
        Assert.Equal(350, Path.MidPoint.X, 6);
        Assert.Equal(50, Path.MidPoint.Y, 6);
    }
}
