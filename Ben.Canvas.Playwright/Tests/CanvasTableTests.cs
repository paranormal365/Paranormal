using Ben.Canvas.Playwright.Support;
using NUnit.Framework;

namespace Ben.Canvas.Playwright.Tests;

/// <summary>
/// A table on a real board: added from the rail, typed into, grown, and pasted in from a spreadsheet.
/// </summary>
/// <remarks>
/// <para><b>Why a browser test as well as the unit ones.</b> M3's notes record a renderer whose tests all
/// passed while a double-click on it went to the board instead, because the board holds pointer capture.
/// A table is the first block whose body is a grid of focusable inputs — exactly the shape that finds
/// gesture bugs — so the click into a cell and the typing are driven here rather than assumed.</para>
/// </remarks>
[TestFixture]
[Category("Editing")]
public class CanvasTableTests : CanvasTestBase
{
    [Test]
    public async Task A_table_is_a_real_table_and_can_be_typed_into()
    {
        await StartCleanAsync();
        var table = await AddNodeAsync("table");

        // At rest it is a real table element, not a stack of divs that looks like one: that is what
        // makes it readable to a screen reader and copyable back out as a grid.
        await Expect(table.Locator("table.bc-table__grid")).ToBeVisibleAsync();

        await table.DblClickAsync();
        var cells = table.Locator(".bc-table__input");
        await Expect(cells.First).ToBeVisibleAsync();

        await cells.Nth(0).FillAsync("Name");
        await cells.Nth(1).FillAsync("Born");

        // Out of edit, and the words are on the board rather than only in the boxes.
        await Page.Keyboard.PressAsync("Escape");
        await Expect(table).ToContainTextAsync("Name");
        await Expect(table).ToContainTextAsync("Born");
    }

    /// <summary>Every cell says where it is, which is the whole of using a grid without sight.</summary>
    [Test]
    public async Task Every_cell_names_where_it_is()
    {
        await StartCleanAsync();
        var table = await AddNodeAsync("table");
        await table.DblClickAsync();

        await table.Locator(".bc-table__input").Nth(0).FillAsync("Name");
        await Expect(table.Locator("[aria-label='Column 1 heading']")).ToHaveCountAsync(1);
        await Expect(table.Locator("[aria-label='Name, row 1']")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Rows_and_columns_can_be_added_and_removed()
    {
        await StartCleanAsync();
        var table = await AddNodeAsync("table");
        await table.DblClickAsync();

        var cells = table.Locator(".bc-table__input");
        await Expect(cells).ToHaveCountAsync(4);            // a header and one row, two columns

        await table.Locator("button[aria-label='Add row']").ClickAsync();
        await Expect(cells).ToHaveCountAsync(6);

        await table.Locator("button[aria-label='Add column']").ClickAsync();
        await Expect(cells).ToHaveCountAsync(9);

        await table.Locator("button[aria-label='Remove row']").ClickAsync();
        await Expect(cells).ToHaveCountAsync(6);
    }

    /// <summary>
    /// Tab-separated text is what every spreadsheet puts on the clipboard, and it should not land as a
    /// note full of tab characters.
    /// </summary>
    [Test]
    public async Task Pasting_a_grid_makes_a_table_and_not_a_note()
    {
        await StartCleanAsync();

        await Page.PasteAsync([new Flavour("text/plain", "Name\tBorn\nWalt\t1901\nRoy\t1893")]);

        var table = Page.Locator(".bc-node--table");
        await Expect(table).ToHaveCountAsync(1);
        await Expect(table).ToContainTextAsync("Walt");
        await Expect(Page.Locator(".bc-node--text")).ToHaveCountAsync(0);
    }

    /// <summary>And a single column is still prose, or this would steal ordinary pastes.</summary>
    [Test]
    public async Task Pasting_a_list_of_lines_is_still_a_note()
    {
        await StartCleanAsync();

        await Page.PasteAsync([new Flavour("text/plain", "Walt Disney\nRoy Disney\nLillian Disney")]);

        await Expect(Page.Locator(".bc-node--text")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bc-node--table")).ToHaveCountAsync(0);
    }
}
