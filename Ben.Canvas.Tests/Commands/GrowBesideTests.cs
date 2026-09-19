using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Commands;

/// <summary>
/// Where the next block lands when it is grown out of an existing one's side.
/// </summary>
/// <remarks>
/// Ben, 2026-09-16: "Can we make it like Miro?" The whole gesture is placement: ask for the next
/// block on the right and it has to appear on the right, lined up, and not on top of whatever is
/// already sitting there.
/// </remarks>
public sealed class BesidePlacementTests
{
    private static readonly WorldRect From = new(100, 100, 200, 120);

    [Fact]
    public void To_the_right_it_sits_a_gap_away_and_lined_up()
    {
        var placed = BesidePlacement.For(From, CanvasSide.Right, 200, 120, []);

        Assert.Equal(From.Right + BesidePlacement.Gap, placed.X);
        Assert.Equal(From.CenterY, placed.CenterY);
    }

    [Fact]
    public void To_the_left_the_gap_is_measured_from_its_far_edge()
    {
        var placed = BesidePlacement.For(From, CanvasSide.Left, 200, 120, []);

        Assert.Equal(From.X - BesidePlacement.Gap, placed.Right);
        Assert.Equal(From.CenterY, placed.CenterY);
    }

    [Theory]
    [InlineData(CanvasSide.Top)]
    [InlineData(CanvasSide.Bottom)]
    public void Above_and_below_it_lines_up_on_the_other_axis(CanvasSide side)
    {
        var placed = BesidePlacement.For(From, side, 200, 120, []);

        Assert.Equal(From.CenterX, placed.CenterX);
        if (side == CanvasSide.Bottom) Assert.Equal(From.Bottom + BesidePlacement.Gap, placed.Y);
        else Assert.Equal(From.Y - BesidePlacement.Gap, placed.Bottom);
    }

    [Fact]
    public void It_does_not_land_on_something_that_is_already_there()
    {
        var occupied = BesidePlacement.For(From, CanvasSide.Right, 200, 120, []);

        var placed = BesidePlacement.For(From, CanvasSide.Right, 200, 120, [From, occupied]);

        Assert.False(placed.Intersects(occupied));
        // Still to the right: a busy board moves it along the other axis, never back across the source.
        Assert.True(placed.X >= From.Right);
    }

    [Fact]
    public void A_crowded_row_keeps_going_rather_than_stacking()
    {
        var taken = new List<WorldRect> { From };
        for (var i = 0; i < 5; i++)
        {
            var placed = BesidePlacement.For(From, CanvasSide.Right, 200, 120, taken);
            Assert.DoesNotContain(taken, r => r.Intersects(placed));
            taken.Add(placed);
        }
    }

    [Theory]
    [InlineData(CanvasSide.Left, CanvasSide.Right)]
    [InlineData(CanvasSide.Right, CanvasSide.Left)]
    [InlineData(CanvasSide.Top, CanvasSide.Bottom)]
    [InlineData(CanvasSide.Bottom, CanvasSide.Top)]
    public void The_connector_lands_on_the_side_facing_back(CanvasSide from, CanvasSide expected) =>
        Assert.Equal(expected, BesidePlacement.Opposite(from));
}

/// <summary>
/// Growing a block out of another one is <b>one</b> undo step.
/// </summary>
/// <remarks>
/// It was one gesture, so one Undo has to take back the whole of it. Two commands would leave a
/// connector pointing at a block that is no longer there, and a second Undo needed to finish.
/// </remarks>
public sealed class AddConnectedTests
{
    [Fact]
    public void The_block_and_its_connector_arrive_together()
    {
        var from = TestBoards.Node(CanvasNodeType.Card, 100, 100);
        var store = TestBoards.StoreWith(from);

        var made = store.AddConnected(from.Id, CanvasSide.Right, TestBoards.Node(CanvasNodeType.Card));

        Assert.NotNull(made);
        Assert.Equal(2, store.Document.Nodes.Count);
        var edge = Assert.Single(store.Document.Edges);
        Assert.Equal(from.Id, edge.FromNodeId);
        Assert.Equal(made!.Value.Node.Id, edge.ToNodeId);
        Assert.Equal(CanvasSide.Right, edge.FromSide);
        Assert.Equal(CanvasSide.Left, edge.ToSide);
    }

    [Fact]
    public void One_undo_takes_back_both()
    {
        var from = TestBoards.Node(CanvasNodeType.Card, 100, 100);
        var store = TestBoards.StoreWith(from);
        store.AddConnected(from.Id, CanvasSide.Right, TestBoards.Node(CanvasNodeType.Card));

        Assert.True(store.Undo());

        Assert.Single(store.Document.Nodes);
        Assert.Empty(store.Document.Edges);
    }

    [Fact]
    public void Redo_puts_both_back()
    {
        var from = TestBoards.Node(CanvasNodeType.Card, 100, 100);
        var store = TestBoards.StoreWith(from);
        store.AddConnected(from.Id, CanvasSide.Right, TestBoards.Node(CanvasNodeType.Card));
        store.Undo();

        Assert.True(store.Redo());

        Assert.Equal(2, store.Document.Nodes.Count);
        Assert.Single(store.Document.Edges);
    }

    [Fact]
    public void A_block_dropped_at_a_point_is_centred_there_and_picks_its_own_side()
    {
        var from = TestBoards.Node(CanvasNodeType.Card, 100, 100);
        var store = TestBoards.StoreWith(from);

        var made = store.AddConnected(from.Id, CanvasSide.Right, TestBoards.Node(CanvasNodeType.Text), new CanvasPoint(900, 640));

        Assert.NotNull(made);
        var node = made!.Value.Node;
        Assert.Equal(900, node.X + node.Width / 2);
        Assert.Equal(640, node.Y + node.Height / 2);
        // Dropped anywhere at all, so the drawing chooses which side of it the connector meets.
        Assert.Null(made.Value.Edge.ToSide);
    }

    [Fact]
    public void Growing_from_a_block_that_is_not_there_adds_nothing()
    {
        var store = TestBoards.StoreWith(TestBoards.Node());

        Assert.Null(store.AddConnected(Guid.NewGuid(), CanvasSide.Right, TestBoards.Node()));
        Assert.Single(store.Document.Nodes);
        Assert.Empty(store.Document.Edges);
    }

    [Fact]
    public void A_full_board_adds_nothing_rather_than_a_connector_to_nowhere()
    {
        var from = TestBoards.Node(CanvasNodeType.Card, 0, 0);
        var store = TestBoards.StoreWith(from);
        // The store was built with room for one block, which the source already fills.
        var small = new Ben.Canvas.Core.Commands.CanvasStore { MaxNodes = 1 };
        small.Load(store.Document);

        Assert.Null(small.AddConnected(from.Id, CanvasSide.Right, TestBoards.Node()));
        Assert.Empty(small.Document.Edges);
    }

    /// <summary>
    /// A grown block and its connector survive the round trip the case save makes, and appear in the
    /// picture that publishing puts on the case.
    /// </summary>
    /// <remarks>
    /// Ben asked, 2026-09-16: "will this still be able to save and publish?" Nothing here is a new
    /// kind of thing — an ordinary block and an ordinary connector — but that is exactly the claim
    /// worth holding down, because a board whose newest half did not reach the case would look fine
    /// on the machine that made it.
    /// </remarks>
    [Fact]
    public void What_was_grown_is_saved_and_published_like_everything_else()
    {
        var from = TestBoards.Node(CanvasNodeType.Card, 100, 100);
        var store = TestBoards.StoreWith(from);
        var made = store.AddConnected(from.Id, CanvasSide.Right, TestBoards.Node(CanvasNodeType.Card));
        Assert.NotNull(made);

        // Saved: the document the server is sent reads back with both halves.
        var json = Ben.Canvas.Core.Serialization.CanvasSerializer.Serialize(store.Document, compact: true);
        var read = Ben.Canvas.Core.Serialization.CanvasSerializer.Deserialize(json);
        Assert.NotNull(read);
        Assert.Equal(2, read!.Nodes.Count);
        var readEdge = Assert.Single(read.Edges);
        Assert.Equal(from.Id, readEdge.FromNodeId);
        Assert.Equal(made!.Value.Node.Id, readEdge.ToNodeId);

        // Published: the picture drawn for the case has the block and the arrow between them.
        var scene = Ben.Canvas.Core.Persistence.BoardSnapshot.Build(read);
        Assert.Equal(2, scene.Blocks.Count);
        var connector = Assert.Single(scene.Connectors);
        Assert.Single(connector.Heads);
    }
}
