namespace Ben.Video.Editor.Models;

/// <summary>
/// Item #31 — greedy word-wrap for callout text.
///
/// <para><b>The measurement caveat, stated plainly:</b> this estimates each line's width as
/// <c>charCount × fontSize × <see cref="AverageAdvanceRatio"/></c>. It does not measure glyphs. Real
/// advance widths depend on the font, the specific characters, kerning and hinting, none of which
/// are available here — this renderer is pure C# that must produce identical SVG for the live
/// preview and for export, and the only true measurement source (the browser's text metrics) is
/// reachable from neither deterministically nor synchronously.</para>
///
/// <para>The consequence is honest and bounded: wrapping is approximate. Wide-glyph text
/// (uppercase, "WWW") wraps later than ideal and can overhang slightly; narrow text ("iii") wraps
/// earlier than necessary. It is chosen for being deterministic and unit-testable rather than
/// pixel-exact — a wrong-by-a-few-percent break is a far better failure than preview and export
/// disagreeing, which is what any browser-measured approach would risk.</para>
/// </summary>
public static class CalloutTextWrapper
{
    /// <summary>
    /// Mean glyph advance as a fraction of font size for common proportional UI fonts (Arial,
    /// Helvetica, Segoe UI) over mixed-case Latin text. Deliberately a shade generous so wrapping
    /// errs toward breaking early (text staying inside the shape) rather than overhanging it.
    /// </summary>
    public const double AverageAdvanceRatio = 0.52;

    /// <summary>Estimated rendered width, in pixels, of <paramref name="text"/>.</summary>
    public static double EstimateWidth(string text, double fontSize) =>
        string.IsNullOrEmpty(text) ? 0 : text.Length * fontSize * AverageAdvanceRatio;

    /// <summary>
    /// Wraps each of <paramref name="lines"/> to <paramref name="maxWidthPx"/>, preserving the
    /// caller's existing explicit breaks (an explicit <c>\n</c> always stays a break; wrapping only
    /// ever adds breaks, never joins lines).
    ///
    /// <para>A single word longer than the limit is emitted on its own line rather than being split
    /// mid-word or dropped — overflowing is strictly better than mangling, and character-level
    /// breaking of an unbroken token is rarely what anyone wants in a callout label.</para>
    /// </summary>
    public static string[] Wrap(IEnumerable<string> lines, double maxWidthPx, double fontSize)
    {
        var result = new List<string>();
        // A non-positive budget means the shape is too small (or degenerate) to lay text out
        // against — fall back to the caller's own line breaks rather than emitting one word
        // per line, which is what a naive loop would do here.
        if (maxWidthPx <= 0 || fontSize <= 0)
        {
            result.AddRange(lines);
            return [.. result];
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line)) { result.Add(string.Empty); continue; }

            var words   = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) { result.Add(string.Empty); continue; }

            var current = string.Empty;
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (EstimateWidth(candidate, fontSize) <= maxWidthPx || current.Length == 0)
                {
                    current = candidate;
                }
                else
                {
                    result.Add(current);
                    current = word;
                }
            }
            if (current.Length > 0) result.Add(current);
        }

        return [.. result];
    }
}
