using Ben.Canvas.Core.Model;

namespace Ben.Canvas.Core.Blocks;

/// <summary>
/// The room a block's own content needs.
/// </summary>
/// <remarks>
/// <para><b>Almost no block knows this about itself.</b> How tall a note should be is the author's
/// business, and a card's fields are the same six however big the box is — so <see cref="For"/>
/// answers null for everything except the one kind whose content has a size it can count: a grid.</para>
///
/// <para><b>Why it exists at all.</b> Walking the board on 2026-09-18 found that pressing <b>Add
/// row</b> on a table produced a row nobody could see. The block does not grow, so the fourth row of a
/// four-row grid sat below the block's bottom edge, reachable only through an inner scrollbar that
/// nobody looks for on a canvas. A button in a block's own toolbar has to produce something visible.</para>
///
/// <para>Pure and in Core, so the arithmetic is readable from a test rather than measured in a
/// browser. The numbers match <c>TableNode.razor.css</c>; a test pins them together.</para>
/// </remarks>
public static class BlockFit
{
    /// <summary>
    /// A grid row at rest.
    /// </summary>
    /// <remarks>
    /// MEASURED in a browser, not derived. The cell's stylesheet minimum is 1.75rem, but its padding
    /// and the text box inside it settle a row at 38.5 pixels — the first version of this constant was
    /// the CSS minimum of 28, and the block grew several rows short; the second was 38, and six rows
    /// came out three pixels over. These numbers are rounded UP on purpose: a few pixels of slack is
    /// invisible, and being short by one hides a row behind a scrollbar nobody looks for.
    /// </remarks>
    public const double RowHeight = 39;

    /// <summary>A column narrow enough to be worth having and wide enough to read a word in.</summary>
    public const double ColumnWidth = 80;

    /// <summary>The block's own furniture around a grid: the title bar and the body's padding.</summary>
    public const double GridChrome = 58;

    /// <summary>
    /// The strip of row and column buttons, which only exists while the grid is being edited.
    /// </summary>
    /// <remarks>
    /// Not part of <see cref="For"/>, which measures CONTENT: these buttons are an affordance, they
    /// are gone the moment editing ends, and a template sizing a grid must not leave room for them.
    /// But a block being edited has to show them AND the rows, so the growth adds this on.
    /// </remarks>
    public const double EditingControls = 68;

    /// <summary>The same, across.</summary>
    public const double GridSideChrome = 24;

    /// <summary>
    /// The smallest rectangle that shows all of this block's content, or null when the block's size is
    /// the author's business alone.
    /// </summary>
    public static (double Width, double Height)? For(NodeData? data) => data switch
    {
        TableData table => (
            GridSideChrome + Math.Max(table.ColumnCount, TableData.MinColumns) * ColumnWidth,
            GridChrome + Math.Max(table.Rows.Count, TableData.MinRows) * RowHeight),
        _ => null,
    };

    /// <summary>
    /// How big a block should be to show <paramref name="data"/> without getting smaller than it is.
    /// </summary>
    /// <remarks>
    /// Only ever grows. Shrinking would fight a size somebody chose by dragging a corner — and would
    /// snap a deliberately roomy table shut the moment a row came out of it.
    /// </remarks>
    /// <param name="editing">
    /// True while the block is open for editing, which adds room for <see cref="EditingControls"/> so
    /// the row and column buttons cannot be pushed out of sight by the rows they add.
    /// </param>
    public static (double Width, double Height) Grown(NodeData? data, double width, double height, bool editing = false)
    {
        if (For(data) is not { } needed) return (width, height);

        var tall = needed.Height + (editing ? EditingControls : 0);
        return (Math.Max(width, needed.Width), Math.Max(height, tall));
    }
}
