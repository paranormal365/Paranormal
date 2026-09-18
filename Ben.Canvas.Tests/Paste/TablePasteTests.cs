using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Core.Paste;

namespace Ben.Canvas.Tests.Paste;

/// <summary>
/// Tab-separated text pasted onto the board becomes a table.
/// </summary>
/// <remarks>
/// <para>This is what a copy out of Excel, Numbers, Google Sheets or a rendered HTML table puts on the
/// clipboard as <c>text/plain</c>: rows separated by newlines, cells by tabs. Landing it as one note
/// full of tab characters is the wrong answer for the one paste people most want to work — the research
/// plan Ben sent is a calendar grid, and getting it onto a board should not mean typing it twice.</para>
///
/// <para><b>Where it sits in the order matters.</b> The classifier tries our own JSON, files, a single
/// URL, coordinates, an address, then HTML, then plain text. A table is decided from plain text, so it
/// must be asked BEFORE the text fallback and AFTER everything more specific — a two-line address with a
/// tab in it is still an address, and a pasted board is still a board.</para>
///
/// <para><b>Two columns and two rows, or it is prose.</b> A single column of lines is a list somebody
/// wants as a note; one row of tabbed words is a sentence that has been through a formatter. The
/// threshold is what stops this stealing ordinary pastes, so it is pinned from both sides.</para>
/// </remarks>
public sealed class TablePasteTests
{
    private static readonly CanvasEditorOptions Options = new();

    private static PasteEnvelope Envelope(params PasteItem[] items) => new([.. items], 0, 0, "paste");
    private static PasteItem Plain(string text) => new("string", "text/plain", text);
    private static PasteItem Html(string html) => new("string", "text/html", html);

    private static PasteIntent.Table? TableOf(string plain, string? html = null)
    {
        var items = html is null ? new[] { Plain(plain) } : [Plain(plain), Html(html)];
        return PasteClassifier.Classify(Envelope(items), Options).Intents.OfType<PasteIntent.Table>().FirstOrDefault();
    }

    [Fact]
    public void Tab_separated_text_becomes_a_table()
    {
        var table = TableOf("Name\tBorn\tDied\nWalt Disney\t1901\t1966\nRoy Disney Sr.\t1893\t1971");

        Assert.NotNull(table);
        Assert.Equal(3, table!.Rows.Count);
        Assert.Equal(["Name", "Born", "Died"], table.Rows[0]);
        Assert.Equal(["Roy Disney Sr.", "1893", "1971"], table.Rows[2]);
    }

    /// <summary>
    /// Every spreadsheet puts its header in the first row, and a table that reads as data-with-a-header
    /// is what the board should show. The first row is treated as the header on the way in.
    /// </summary>
    [Fact]
    public void The_first_row_is_taken_as_the_header()
    {
        Assert.True(TableOf("Name\tBorn\nWalt\t1901")!.HasHeaderRow);
    }

    [Fact]
    public void A_single_column_of_text_stays_a_note()
    {
        Assert.Null(TableOf("Walt Disney\nRoy Disney\nLillian Disney"));
    }

    [Fact]
    public void A_single_row_of_tabbed_words_stays_a_note()
    {
        Assert.Null(TableOf("Walt\tRoy\tLillian"));
    }

    /// <summary>
    /// Copying out of a spreadsheet gives a short trailing newline; it is a blank line, not an empty row.
    /// </summary>
    [Fact]
    public void A_trailing_newline_does_not_make_an_empty_row()
    {
        var table = TableOf("Name\tBorn\nWalt\t1901\n");

        Assert.Equal(2, table!.Rows.Count);
    }

    /// <summary>
    /// Windows and older Mac clipboards end lines differently, and a stray carriage return inside a cell
    /// would show as a box on the board.
    /// </summary>
    [Fact]
    public void Carriage_returns_are_not_left_in_the_cells()
    {
        var table = TableOf("Name\tBorn\r\nWalt\t1901\r\n");

        Assert.Equal(["Name", "Born"], table!.Rows[0]);
        Assert.Equal(["Walt", "1901"], table.Rows[1]);
    }

    /// <summary>
    /// A ragged paste — a short last row, which Sheets produces when the selection is ragged — is squared
    /// up rather than refused, the same rule the reader applies to a hand-edited file.
    /// </summary>
    [Fact]
    public void A_ragged_paste_is_squared_up()
    {
        var table = TableOf("a\tb\tc\nd\te\nf");

        Assert.True(table!.Rows.All(r => r.Count == 3));
        Assert.Equal("", table.Rows[2][1]);
    }

    /// <summary>
    /// The HTML flavour usually arrives beside the plain text when copying from a spreadsheet, and the
    /// HTML branch would otherwise take it first and make a message out of a grid.
    /// </summary>
    [Fact]
    public void A_table_wins_over_the_html_that_came_with_it()
    {
        var table = TableOf(
            "Name\tBorn\nWalt\t1901",
            "<table><tr><td>Name</td><td>Born</td></tr><tr><td>Walt</td><td>1901</td></tr></table>");

        Assert.NotNull(table);
    }

    /// <summary>
    /// Everything more specific than plain text still wins. A pasted address with a tab in it is an
    /// address: the person wanted a map, and a two-cell table is not one.
    /// </summary>
    [Fact]
    public void An_address_is_still_an_address()
    {
        var plan = PasteClassifier.Classify(
            Envelope(Plain("1 Printers Alley\tNashville, TN 37201\n2 Printers Alley\tNashville, TN 37201")), Options);

        Assert.Empty(plan.Intents.OfType<PasteIntent.Table>());
    }

    /// <summary>A grid larger than the paste allowance is refused as a whole, not truncated silently.</summary>
    [Fact]
    public void A_grid_beyond_the_cell_allowance_does_not_become_a_table()
    {
        var wide = string.Join("\t", Enumerable.Range(0, 200).Select(i => $"c{i}"));
        var text = string.Join("\n", Enumerable.Range(0, 200).Select(_ => wide));

        Assert.Null(TableOf(text));
    }
}
