using Ben.Canvas.Core.Model;
using Ben.Canvas.Tests.Support;
using Ben.Canvas.Core.Serialization;

namespace Ben.Canvas.Tests.Model;

/// <summary>
/// A connector's ends, its line and the word at its middle.
/// </summary>
/// <remarks>
/// <para><b>The family tree needs all of it at once</b>: a dashed line between siblings, a diamond at
/// each end of a marriage, a plain line down to the children with a small glyph at its midpoint, and a
/// legend beside the board saying which is which. A single "arrow" switch cannot express that.</para>
///
/// <para><b>Nothing already drawn may change.</b> Every board on the site was written with
/// <c>Arrow</c>, so it is still read, and a file carrying only that is mapped onto the two markers on
/// read. The markers are nullable precisely so a board written before M9 can be told apart from one
/// that deliberately chose no marker — and the migrations resolve it once, so nothing downstream has
/// to know the difference.</para>
/// </remarks>
public sealed class EdgeStyleTests
{
    private static CanvasDocument WithEdge(Action<CanvasEdge> set)
    {
        var a = new CanvasNode { Type = CanvasNodeType.Card, Width = 100, Height = 60 };
        var b = new CanvasNode { Type = CanvasNodeType.Card, X = 400, Width = 100, Height = 60 };
        var edge = new CanvasEdge { FromNodeId = a.Id, ToNodeId = b.Id };
        set(edge);
        return new CanvasDocument { Nodes = [a, b], Edges = [edge] };
    }

    private static CanvasEdge RoundTrip(Action<CanvasEdge> set)
    {
        var (read, problem) = CanvasSerializer.Parse(CanvasSerializer.Serialize(WithEdge(set)));
        Assert.True(problem is null, problem);
        return read!.Edges.Single();
    }

    // ── defaults ─────────────────────────────────────────────────────────

    [Fact]
    public void A_new_connector_is_a_solid_curve_with_an_arrow_at_the_end()
    {
        var edge = new CanvasEdge();

        Assert.Equal(EdgeLine.Solid, edge.Line);
        Assert.Equal(EdgeRoute.Curve, edge.Route);
        Assert.Equal(EdgeArrow.End, edge.Arrow);
    }

    // ── the old switch still means what it meant ─────────────────────────

    /// <summary>
    /// A board written before markers existed draws exactly as it did: End becomes an arrow where it
    /// arrives and nothing where it leaves.
    /// </summary>
    [Fact]
    public void An_old_arrow_maps_to_an_end_marker()
    {
        var edge = RoundTrip(e => { e.Arrow = EdgeArrow.End; e.FromMarker = null; e.ToMarker = null; });

        Assert.Equal(EdgeMarker.None, edge.FromMarker);
        Assert.Equal(EdgeMarker.Arrow, edge.ToMarker);
    }

    [Fact]
    public void An_old_both_arrow_maps_to_arrows_at_each_end()
    {
        var edge = RoundTrip(e => { e.Arrow = EdgeArrow.Both; e.FromMarker = null; e.ToMarker = null; });

        Assert.Equal(EdgeMarker.Arrow, edge.FromMarker);
        Assert.Equal(EdgeMarker.Arrow, edge.ToMarker);
    }

    [Fact]
    public void An_old_connector_with_no_arrow_gets_no_markers()
    {
        var edge = RoundTrip(e => { e.Arrow = EdgeArrow.None; e.FromMarker = null; e.ToMarker = null; });

        Assert.Equal(EdgeMarker.None, edge.FromMarker);
        Assert.Equal(EdgeMarker.None, edge.ToMarker);
    }

    /// <summary>
    /// And a connector that DID choose its markers keeps them, whatever the old switch says. This is
    /// the whole reason the markers are nullable rather than defaulted.
    /// </summary>
    [Fact]
    public void Chosen_markers_win_over_the_old_switch()
    {
        var edge = RoundTrip(e =>
        {
            e.Arrow = EdgeArrow.End;
            e.FromMarker = EdgeMarker.Diamond;
            e.ToMarker = EdgeMarker.Diamond;
        });

        Assert.Equal(EdgeMarker.Diamond, edge.FromMarker);
        Assert.Equal(EdgeMarker.Diamond, edge.ToMarker);
    }

    // ── the new fields ───────────────────────────────────────────────────

    [Theory]
    [InlineData(EdgeMarker.None)]
    [InlineData(EdgeMarker.Arrow)]
    [InlineData(EdgeMarker.Diamond)]
    [InlineData(EdgeMarker.Dot)]
    public void Each_marker_round_trips(EdgeMarker marker)
    {
        Assert.Equal(marker, RoundTrip(e => { e.FromMarker = marker; e.ToMarker = marker; }).FromMarker);
    }

    [Theory]
    [InlineData(EdgeLine.Solid)]
    [InlineData(EdgeLine.Dashed)]
    public void The_line_style_round_trips(EdgeLine line)
    {
        Assert.Equal(line, RoundTrip(e => e.Line = line).Line);
    }

    [Theory]
    [InlineData(EdgeRoute.Curve)]
    [InlineData(EdgeRoute.Straight)]
    [InlineData(EdgeRoute.Elbow)]
    public void The_route_round_trips(EdgeRoute route)
    {
        Assert.Equal(route, RoundTrip(e => e.Route = route).Route);
    }

    [Fact]
    public void The_midpoint_icon_round_trips()
    {
        Assert.Equal("m", RoundTrip(e => e.Icon = "m").Icon);
    }

    /// <summary>
    /// An icon longer than the line can hold is clamped on read rather than drawn over the board. A
    /// hand-edited file or a newer editor can carry anything.
    /// </summary>
    [Fact]
    public void A_connector_icon_is_clamped_on_read()
    {
        var edge = RoundTrip(e => e.Icon = new string('x', 50));

        Assert.Equal(CanvasEdge.MaxIconLength, edge.Icon!.Length);
    }

    [Fact]
    public void A_blank_icon_reads_as_nothing_rather_than_an_empty_box()
    {
        Assert.Null(RoundTrip(e => e.Icon = "   ").Icon);
    }

    // ── unknown values are decoration, not a refusal ─────────────────────

    [Fact]
    public void An_unknown_route_reads_as_a_curve()
    {
        var json = CanvasSerializer.Serialize(WithEdge(e => e.Route = EdgeRoute.Elbow))
            .Replace("\"Elbow\"", "\"Orbital\"");

        var (read, problem) = CanvasSerializer.Parse(json);

        Assert.True(problem is null, problem);
        Assert.Equal(EdgeRoute.Curve, read!.Edges.Single().Route);
    }

    [Fact]
    public void An_unknown_marker_reads_as_none()
    {
        var json = CanvasSerializer.Serialize(WithEdge(e => e.ToMarker = EdgeMarker.Diamond))
            .Replace("\"Diamond\"", "\"Chevron\"");

        var (read, problem) = CanvasSerializer.Parse(json);

        Assert.True(problem is null, problem);
        Assert.Equal(EdgeMarker.None, read!.Edges.Single().ToMarker);
    }

    [Fact]
    public void An_unknown_line_style_reads_as_solid()
    {
        var json = CanvasSerializer.Serialize(WithEdge(e => e.Line = EdgeLine.Dashed))
            .Replace("\"Dashed\"", "\"Dotty\"");

        var (read, problem) = CanvasSerializer.Parse(json);

        Assert.True(problem is null, problem);
        Assert.Equal(EdgeLine.Solid, read!.Edges.Single().Line);
    }

    // ── drawn correctly whether or not the migration has run ─────────────

    /// <summary>
    /// A connector drawn a moment ago has no markers chosen yet, and must still draw its arrow.
    /// </summary>
    /// <remarks>
    /// This is the bug the snapshot tests caught. Reading the nullable fields directly meant the
    /// published picture of a FRESH board lost every arrowhead — the migration had not run, because
    /// the board had not been round-tripped. The effective properties answer the same whether or not
    /// it has, so the migration is a convenience rather than a precondition.
    /// </remarks>
    [Theory]
    [InlineData(EdgeArrow.None, EdgeMarker.None, EdgeMarker.None)]
    [InlineData(EdgeArrow.End, EdgeMarker.None, EdgeMarker.Arrow)]
    [InlineData(EdgeArrow.Both, EdgeMarker.Arrow, EdgeMarker.Arrow)]
    public void An_unmigrated_connector_still_draws_its_arrow(
        EdgeArrow arrow, EdgeMarker from, EdgeMarker to)
    {
        var edge = new CanvasEdge { Arrow = arrow };

        Assert.Equal(from, edge.EffectiveFromMarker);
        Assert.Equal(to, edge.EffectiveToMarker);
    }

    [Fact]
    public void A_chosen_marker_beats_the_old_switch_without_a_migration()
    {
        var edge = new CanvasEdge { Arrow = EdgeArrow.End, ToMarker = EdgeMarker.Diamond };

        Assert.Equal(EdgeMarker.Diamond, edge.EffectiveToMarker);
    }

    /// <summary>Choosing None is a real choice, and outranks the old switch.</summary>
    [Fact]
    public void Choosing_nothing_is_not_mistaken_for_not_having_chosen()
    {
        var edge = new CanvasEdge { Arrow = EdgeArrow.End, ToMarker = EdgeMarker.None };

        Assert.Equal(EdgeMarker.None, edge.EffectiveToMarker);
    }

    // ── the edit actually happens ────────────────────────────────────────

    /// <summary>
    /// Changing any connector style is a real edit, and one undo takes it back.
    /// </summary>
    /// <remarks>
    /// <c>CanvasStore.UpdateEdge</c> compares an <c>EdgeSnapshot</c> before and after and rejects the
    /// edit when nothing changed. A field missing from that snapshot therefore fails twice: the change
    /// never happens at all, and undo could not reverse it if it did. Every select in the properties
    /// panel silently did nothing until the snapshot learned these four fields — found in the browser,
    /// pinned here.
    /// </remarks>
    [Fact]
    public void Changing_a_connector_style_is_a_real_edit_that_undoes()
    {
        var store = TestBoards.Store();
        var a = TestBoards.Node(CanvasNodeType.Card);
        var b = TestBoards.Node(CanvasNodeType.Card, x: 400);
        store.AddNode(a);
        store.AddNode(b);
        var edge = store.Connect(a.Id, b.Id)!;

        Assert.True(store.UpdateEdge(edge.Id, e => e.Route = EdgeRoute.Elbow), "the route change was rejected");
        Assert.True(store.UpdateEdge(edge.Id, e => e.Line = EdgeLine.Dashed), "the line change was rejected");
        Assert.True(store.UpdateEdge(edge.Id, e => e.ToMarker = EdgeMarker.Diamond), "the marker change was rejected");
        Assert.True(store.UpdateEdge(edge.Id, e => e.Icon = "m"), "the icon change was rejected");

        Assert.Equal(EdgeRoute.Elbow, store.FindEdge(edge.Id)!.Route);
        Assert.Equal("m", store.FindEdge(edge.Id)!.Icon);

        store.Undo();
        Assert.Null(store.FindEdge(edge.Id)!.Icon);

        store.Undo();
        Assert.Equal(EdgeMarker.Arrow, store.FindEdge(edge.Id)!.EffectiveToMarker);
    }

    /// <summary>Setting a style to what it already is still records nothing.</summary>
    [Fact]
    public void Setting_a_style_to_what_it_already_is_records_nothing()
    {
        var store = TestBoards.Store();
        var a = TestBoards.Node(CanvasNodeType.Card);
        var b = TestBoards.Node(CanvasNodeType.Card, x: 400);
        store.AddNode(a);
        store.AddNode(b);
        var edge = store.Connect(a.Id, b.Id)!;

        Assert.False(store.UpdateEdge(edge.Id, e => e.Route = EdgeRoute.Curve));
    }

    [Fact]
    public void A_connector_style_survives_a_clone()
    {
        var edge = new CanvasEdge
        {
            Line = EdgeLine.Dashed, Route = EdgeRoute.Elbow,
            FromMarker = EdgeMarker.Diamond, ToMarker = EdgeMarker.Dot, Icon = "m",
        };

        var copy = edge.Clone();

        Assert.Equal(EdgeLine.Dashed, copy.Line);
        Assert.Equal(EdgeRoute.Elbow, copy.Route);
        Assert.Equal(EdgeMarker.Diamond, copy.FromMarker);
        Assert.Equal(EdgeMarker.Dot, copy.ToMarker);
        Assert.Equal("m", copy.Icon);
    }
}
