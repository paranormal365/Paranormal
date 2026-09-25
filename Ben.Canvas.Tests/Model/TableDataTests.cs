using Ben.Canvas.Core.Blocks;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Serialization;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Model;

/// <summary>
/// A table on the board: a grid of plain cells that survives a save, a copy and a reopen.
/// </summary>
/// <remarks>
/// <para><b>Ben asked to be reminded</b> (2026-09-14): "Remind me later to have you create the table for
/// the note generator." It arrives as a board block rather than as a stack block, because research is
/// boards now — so a table is a kind of card, with its own data and its own renderer.</para>
///
/// <para><b>Rows of cells, and nothing cleverer.</b> No merged cells, no formulas, no sorting. A merged
/// cell turns a rectangle of strings into a graph and every one of those operations has to learn about
/// it; none of the four boards Ben sent needs one. That restraint is what these tests pin: the shape is
/// rectangular and stays rectangular whatever is done to it.</para>
/// </remarks>
public sealed class TableDataTests
{
    private static TableData Filled() => new()
    {
        HasHeaderRow = true,
        Rows =
        [
            ["Name", "Born", "Died"],
            ["Walt Disney", "1901", "1966"],
            ["Roy Disney Sr.", "1893", "1971"],
        ],
    };

    [Fact]
    public void A_new_table_is_a_small_rectangle_with_a_header()
    {
        var table = (TableData)BlockRegistry.Get(CanvasNodeType.Table).CreateDefaultData(DateTime.UtcNow);

        Assert.True(table.HasHeaderRow);
        Assert.True(table.Rows.Count >= 2, "a new table has a header and at least one row to type in");
        Assert.True(table.Rows[0].Count >= 2, "a new table has at least two columns");
        Assert.True(table.Rows.All(r => r.Count == table.Rows[0].Count), "a new table is rectangular");
    }

    [Fact]
    public void A_table_round_trips_every_cell()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node(CanvasNodeType.Table);
        node.Data = Filled();
        document.Nodes.Add(node);

        var (read, problem) = CanvasSerializer.Parse(CanvasSerializer.Serialize(document));

        Assert.True(problem is null, problem);
        var table = Assert.IsType<TableData>(read!.Nodes.Single().Data);
        Assert.True(table.HasHeaderRow);
        Assert.Equal(3, table.Rows.Count);
        Assert.Equal(["Walt Disney", "1901", "1966"], table.Rows[1]);
    }

    /// <summary>
    /// Its discriminator is part of the file format. A rename would make every board carrying a table
    /// unreadable, so the string is asserted rather than trusted to a refactor.
    /// </summary>
    [Fact]
    public void Its_kind_is_written_as_table()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node(CanvasNodeType.Table);
        node.Data = Filled();
        document.Nodes.Add(node);

        Assert.Contains("\"kind\": \"table\"", CanvasSerializer.Serialize(document));
    }

    /// <summary>
    /// Commands keep copies for undo. A shared row list would let a later edit rewrite history — the
    /// reason <see cref="NodeData.Clone"/> is documented as deep.
    /// </summary>
    [Fact]
    public void Clone_copies_the_rows_not_the_reference()
    {
        var original = Filled();

        var copy = (TableData)original.Clone();
        copy.Rows[1][0] = "changed";
        copy.Rows.Add(["another", "row", "here"]);

        Assert.Equal("Walt Disney", original.Rows[1][0]);
        Assert.Equal(3, original.Rows.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_column_can_be_added_anywhere_and_every_row_grows(int at)
    {
        var table = Filled();

        table.InsertColumn(at);

        Assert.True(table.Rows.All(r => r.Count == 4), "the table stayed rectangular");
        Assert.True(table.Rows.All(r => r[at].Length == 0), "the new column is empty");
        Assert.Equal("Name", table.Rows[0][at == 0 ? 1 : 0]);
    }

    [Fact]
    public void Removing_a_column_takes_it_from_every_row()
    {
        var table = Filled();

        table.RemoveColumn(1);

        Assert.True(table.Rows.All(r => r.Count == 2), "the table stayed rectangular");
        Assert.Equal(["Name", "Died"], table.Rows[0]);
    }

    [Fact]
    public void A_row_is_added_with_a_cell_per_column()
    {
        var table = Filled();

        table.InsertRow(1);

        Assert.Equal(4, table.Rows.Count);
        Assert.Equal(3, table.Rows[1].Count);
        Assert.True(table.Rows[1].All(c => c.Length == 0), "the new row is empty");
        Assert.Equal("Walt Disney", table.Rows[2][0]);
    }

    /// <summary>
    /// A table with no cells cannot be typed into and cannot be got out of, so the last row and the
    /// last column refuse to go. The buttons are hidden at that point; this is the rule underneath.
    /// </summary>
    [Fact]
    public void The_last_row_and_the_last_column_cannot_be_removed()
    {
        var table = new TableData { HasHeaderRow = false, Rows = [["only"]] };

        table.RemoveRow(0);
        table.RemoveColumn(0);

        Assert.Single(table.Rows);
        Assert.Single(table.Rows[0]);
    }

    /// <summary>
    /// A hand-edited file, or one from a newer editor, can carry ragged rows. It is squared up on read
    /// rather than refused: the rule the migrations file states is that a value which cannot be right is
    /// corrected, and a table missing a cell is still a readable table.
    /// </summary>
    [Fact]
    public void A_ragged_table_is_squared_up_on_read()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node(CanvasNodeType.Table);
        node.Data = new TableData { Rows = [["a", "b", "c"], ["d"], []] };
        document.Nodes.Add(node);

        var (read, _) = CanvasSerializer.Parse(CanvasSerializer.Serialize(document));
        var table = (TableData)read!.Nodes.Single().Data;

        Assert.True(table.Rows.All(r => r.Count == 3), "every row has the widest row's width");
        Assert.Equal("d", table.Rows[1][0]);
        Assert.Equal("", table.Rows[1][1]);
    }

    /// <summary>A table with no rows at all reads as the smallest usable one rather than as nothing.</summary>
    [Fact]
    public void An_empty_table_reads_as_one_usable_cell()
    {
        var document = new CanvasDocument();
        var node = TestBoards.Node(CanvasNodeType.Table);
        node.Data = new TableData { Rows = [] };
        document.Nodes.Add(node);

        var (read, _) = CanvasSerializer.Parse(CanvasSerializer.Serialize(document));
        var table = (TableData)read!.Nodes.Single().Data;

        Assert.NotEmpty(table.Rows);
        Assert.NotEmpty(table.Rows[0]);
    }
}
