namespace Ben.Canvas.Core.Paste;

/// <summary>
/// Reads tab-separated text as a grid, or decides it is prose.
/// </summary>
/// <remarks>
/// <para>This is what every spreadsheet puts on the clipboard as <c>text/plain</c>: rows separated by
/// newlines, cells by tabs. Landing that as one note full of tab characters is the wrong answer for the
/// paste people most want to work — a research plan is a grid, and getting it onto a board should not
/// mean typing it twice.</para>
///
/// <para><b>Two columns and two rows, or it is prose.</b> A single column of lines is a list somebody
/// wants as a note; one row of tabbed words is a sentence that has been through a formatter. That
/// threshold is the whole of what stops this stealing ordinary pastes.</para>
///
/// <para>Pure and allocation-light: it is on the paste path, which runs before anything is drawn.</para>
/// </remarks>
public static class TableDetector
{
    /// <summary>Below this a paste is prose, not a grid.</summary>
    public const int MinRows = 2;

    /// <summary>The same, across.</summary>
    public const int MinColumns = 2;

    /// <summary>
    /// Reads <paramref name="text"/> as a grid, or returns false.
    /// </summary>
    /// <param name="maxCells">
    /// The most cells a paste may bring. A grid past it is refused whole rather than truncated: half a
    /// table is worse than none, because nobody can see which half is missing.
    /// </param>
    public static bool TryParse(string? text, int maxCells, out List<List<string>> rows)
    {
        rows = [];
        if (string.IsNullOrEmpty(text) || !text.Contains('\t')) return false;

        // Trailing newlines are how a spreadsheet ends a copy, not an empty row.
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
        if (lines.Length < MinRows) return false;

        var width = 0;
        var parsed = new List<List<string>>(lines.Length);

        foreach (var line in lines)
        {
            var cells = line.Split('\t').ToList();
            width = Math.Max(width, cells.Count);
            parsed.Add(cells);
        }

        if (width < MinColumns) return false;
        if ((long)width * parsed.Count > maxCells) return false;

        // Square it up here, so nothing downstream has to wonder.
        foreach (var row in parsed)
            while (row.Count < width) row.Add("");

        rows = parsed;
        return true;
    }
}
