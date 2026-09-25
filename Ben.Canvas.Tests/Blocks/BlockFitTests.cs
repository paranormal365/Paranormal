using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Commands;
using Ben.Canvas.Core.Geometry;
using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Tests.Blocks;

/// <summary>
/// A block that knows how big its content is, and the one undo step that carries both.
/// </summary>
/// <remarks>
/// <para><b>Where this came from.</b> Walking the board on 2026-09-18 found that pressing <b>Add
/// row</b> on a table produced a row nobody could see: the grid overflows its block, so the fourth row
/// of a four-row table sat below the bottom edge, reachable only through an inner scrollbar that nobody
/// looks for on a canvas. A button in a block's own toolbar has to produce something visible.</para>
///
/// <para>The same walk found the other half: an empty cell had no height, so a new table drew as two
/// nine-pixel hairlines inside a block five times taller than its own grid.</para>
/// </remarks>
public sealed class BlockFitTests
{
    private static TableData Grid(int rows, int columns)
    {
        var data = new TableData { Rows = [] };
        for (var r = 0; r < rows; r++) data.Rows.Add([.. Enumerable.Repeat("", columns)]);
        return data;
    }

    [Fact]
    public void A_grid_needs_room_for_every_row_it_has()
    {
        var two = BlockFit.For(Grid(2, 2))!.Value;
        var five = BlockFit.For(Grid(5, 2))!.Value;

        Assert.Equal(BlockFit.GridChrome + 2 * BlockFit.RowHeight, two.Height);
        Assert.Equal(BlockFit.GridChrome + 5 * BlockFit.RowHeight, five.Height);
        Assert.True(five.Height > two.Height, "three more rows need more room");
    }

    [Fact]
    public void A_grid_needs_room_for_every_column_it_has()
    {
        Assert.Equal(
            BlockFit.GridSideChrome + 6 * BlockFit.ColumnWidth,
            BlockFit.For(Grid(2, 6))!.Value.Width);
    }

    /// <summary>
    /// Nothing else declares a size. How tall a note should be is the author's business, and a card's
    /// fields are the same six however big the box is.
    /// </summary>
    [Theory]
    [InlineData(typeof(TextData))]
    [InlineData(typeof(CardData))]
    [InlineData(typeof(ImageData))]
    [InlineData(typeof(ShapeData))]
    [InlineData(typeof(MapData))]
    [InlineData(typeof(LinkData))]
    [InlineData(typeof(FileData))]
    [InlineData(typeof(BoardData))]
    public void No_other_block_claims_to_know_its_own_size(Type kind)
    {
        Assert.Null(BlockFit.For((NodeData)Activator.CreateInstance(kind)!));
    }

    [Fact]
    public void Nothing_at_all_needs_no_room()
    {
        Assert.Null(BlockFit.For(null));
    }

    /// <summary>
    /// Growing only ever grows. Shrinking would fight a size somebody chose by dragging a corner, and
    /// would snap a deliberately roomy table shut the moment a row came out of it.
    /// </summary>
    [Fact]
    public void A_block_bigger_than_its_content_is_left_alone()
    {
        Assert.Equal((900d, 700d), BlockFit.Grown(Grid(2, 2), 900, 700));
    }

    [Fact]
    public void A_block_smaller_than_its_content_grows_only_on_the_axis_that_needs_it()
    {
        var (width, height) = BlockFit.Grown(Grid(6, 2), 900, 100);

        Assert.Equal(900, width);
        Assert.Equal(BlockFit.GridChrome + 6 * BlockFit.RowHeight, height);
    }

    [Fact]
    public void A_block_whose_content_declares_nothing_is_never_resized()
    {
        Assert.Equal((10d, 10d), BlockFit.Grown(new TextData { Text = "x" }, 10, 10));
    }

    // ── one undo step, content and room together ──────────────────────────

    private static (CanvasStore Store, CanvasNode Node) Board()
    {
        var node = new CanvasNode
        {
            Type = CanvasNodeType.Table,
            X = 40, Y = 60, Width = 420, Height = 140,
            Data = Grid(2, 2),
        };

        var store = new CanvasStore();
        store.Load(new CanvasDocument { Nodes = [node], NextZ = 1 });
        return (store, store.Document.Nodes[0]);
    }

    /// <summary>
    /// The room a grid made for itself undoes WITH the rows, not as a step of its own — otherwise one
    /// Ctrl+Z leaves a two-row table in a block sized for five, and a second is needed to finish.
    /// </summary>
    [Fact]
    public void A_grid_that_grew_itself_undoes_in_one_step()
    {
        var (store, node) = Board();
        var was = CanvasHitTester.RectOf(node);

        // What the editor does: the block grows itself live, then the session commits once.
        var draft = Grid(5, 2);
        (node.Width, node.Height) = BlockFit.Grown(draft, node.Width, node.Height);

        Assert.True(store.ReplaceNodeData(node.Id, draft, was));

        var grown = store.Document.Nodes[0];
        Assert.Equal(5, ((TableData)grown.Data).Rows.Count);
        Assert.Equal(BlockFit.GridChrome + 5 * BlockFit.RowHeight, grown.Height);

        Assert.True(store.Undo());

        var back = store.Document.Nodes[0];
        Assert.Equal(2, ((TableData)back.Data).Rows.Count);
        Assert.Equal(was, CanvasHitTester.RectOf(back));
        Assert.False(store.CanUndo, "the rows and the room for them were two steps, not one");
    }

    /// <summary>And redo puts both back, or the step is only half a step.</summary>
    [Fact]
    public void Redo_puts_the_rows_and_the_room_back()
    {
        var (store, node) = Board();
        var was = CanvasHitTester.RectOf(node);

        var draft = Grid(5, 2);
        (node.Width, node.Height) = BlockFit.Grown(draft, node.Width, node.Height);
        store.ReplaceNodeData(node.Id, draft, was);
        store.Undo();

        Assert.True(store.Redo());

        var again = store.Document.Nodes[0];
        Assert.Equal(5, ((TableData)again.Data).Rows.Count);
        Assert.Equal(BlockFit.GridChrome + 5 * BlockFit.RowHeight, again.Height);
    }

    /// <summary>
    /// An edit that moved nothing but the content still records only the content: every block now
    /// passes its entry rectangle, and a no-op resize must not start writing geometry into edits.
    /// </summary>
    [Fact]
    public void An_edit_that_did_not_resize_leaves_the_rectangle_alone()
    {
        var (store, node) = Board();
        var was = CanvasHitTester.RectOf(node);

        var draft = Grid(2, 2);
        draft.Rows[0][0] = "Owner";
        Assert.True(store.ReplaceNodeData(node.Id, draft, was));

        store.Undo();
        Assert.Equal(was, CanvasHitTester.RectOf(store.Document.Nodes[0]));
    }

    /// <summary>
    /// And an edit that changed nothing at all records nothing, entry rectangle or not — the rule the
    /// edit session has always had.
    /// </summary>
    [Fact]
    public void An_edit_that_changed_nothing_records_nothing()
    {
        var (store, node) = Board();

        Assert.False(store.ReplaceNodeData(node.Id, Grid(2, 2), CanvasHitTester.RectOf(node)));
        Assert.False(store.CanUndo);
    }

    /// <summary>
    /// A block that only GREW during an edit still commits, so the growth is not lost — a grid can gain
    /// room without a single cell changing.
    /// </summary>
    [Fact]
    public void A_block_that_only_grew_still_records_the_growth()
    {
        var (store, node) = Board();
        var was = CanvasHitTester.RectOf(node);
        node.Height = 500;

        Assert.True(store.ReplaceNodeData(node.Id, Grid(2, 2), was));

        store.Undo();
        Assert.Equal(140, store.Document.Nodes[0].Height);
    }

    /// <summary>
    /// The stylesheet still floors a cell's height, which is what stops an empty row collapsing to a
    /// hairline.
    /// </summary>
    /// <remarks>
    /// <see cref="BlockFit.RowHeight"/> is deliberately LARGER than that floor: a cell's padding and
    /// the text box inside it settle a row at about 37 pixels, and the first version of this constant
    /// was the CSS minimum — which made the block grow a row short every time. The real height is
    /// measured in a browser by the Playwright table fixture; what this holds is that the floor exists
    /// and that the constant is not below it.
    /// </remarks>
    [Fact]
    public void A_cell_still_has_a_floor_and_the_fit_is_not_below_it()
    {
        var css = File.ReadAllText(Path.Combine(
            Support.RepoFiles.EditorRoot(), "Components", "Nodes", "TableNode.razor.css"));

        Assert.Contains("height: 1.75rem", css);
        Assert.True(BlockFit.RowHeight >= 28, "a row cannot be shorter than the cell's own minimum");
    }

    /// <summary>
    /// The buttons a grid is edited with are counted separately from the grid itself: they are an
    /// affordance that disappears, and a template sizing a table must not leave a gap for them.
    /// </summary>
    [Fact]
    public void The_editing_buttons_are_extra_room_not_content()
    {
        var content = BlockFit.For(Grid(3, 2))!.Value.Height;

        Assert.Equal(content, BlockFit.Grown(Grid(3, 2), 0, 0, editing: false).Height);
        Assert.Equal(content + BlockFit.EditingControls,
            BlockFit.Grown(Grid(3, 2), 0, 0, editing: true).Height);
    }
}
