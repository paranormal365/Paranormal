using System.Globalization;

namespace Ben.Video.Editor.Models;

/// <summary>
/// Shared per-(line, run) <c>&lt;tspan&gt;</c> generation for <see cref="TextRun"/> lists, used by
/// both <see cref="TextOverlayRenderer"/> and <see cref="CalloutShapeRenderer"/> (item #16 — inline
/// mixed formatting + subscript/superscript). Relies on SVG's own text-chunk semantics: a
/// <c>&lt;tspan&gt;</c> with an explicit <c>x</c> starts a new chunk that <c>text-anchor</c> aligns
/// as a whole, so only the first run of each line needs an explicit <c>x</c> — every other run on
/// that line has no positional attributes at all and simply continues immediately after the
/// previous tspan, which is exactly correct multi-run alignment, not a workaround.
/// </summary>
internal static class RichTextTspanBuilder
{
    private static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    /// <summary>One contiguous piece of text on a single line, carrying its originating run's style.</summary>
    internal readonly record struct Fragment(string Text, TextRun Style);

    /// <summary>
    /// Splits an ordered <see cref="TextRun"/> list into lines of styled fragments — a run's own
    /// <c>'\n'</c> characters start a new line without ending the run's style early (each side of
    /// the break keeps the same style). A run contributing an empty string to a line (e.g. two
    /// consecutive <c>'\n'</c>) contributes no fragment for that line.
    /// </summary>
    internal static List<List<Fragment>> SplitIntoLines(IEnumerable<TextRun> runs)
    {
        var lines = new List<List<Fragment>> { new() };
        foreach (var run in runs)
        {
            var parts = run.Text.Replace("\r\n", "\n").Split('\n');
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0) lines.Add([]);
                if (parts[i].Length > 0)
                    lines[^1].Add(new Fragment(parts[i], run));
            }
        }
        return lines;
    }

    /// <summary>Plain concatenated text per line — for box-sizing heuristics that only need character counts.</summary>
    internal static string[] ToPlainLines(List<List<Fragment>> lines) =>
        [.. lines.Select(l => string.Concat(l.Select(f => f.Text)))];

    /// <summary>
    /// Item #31 — word-wraps styled lines to <paramref name="maxWidthPx"/>, preserving each
    /// fragment's style. Explicit line breaks are never joined; wrapping only adds breaks.
    ///
    /// <para>This exists because the rich-text editor ALWAYS produces <see cref="TextRun"/>s, so a
    /// wrap implemented only for the plain-text path would be dead code in the real UI — which is
    /// exactly what live verification caught. Measurement uses
    /// <see cref="CalloutTextWrapper.EstimateWidth"/>, so the same approximation caveat applies.</para>
    ///
    /// <para>A word that straddles a style boundary (e.g. "make<b>Bold</b>Again") is kept whole and
    /// wrapped as one unit — splitting it would silently change the rendered text's line structure
    /// mid-word purely because of a formatting change.</para>
    /// </summary>
    public static List<List<Fragment>> WrapLines(List<List<Fragment>> lines, double maxWidthPx, double fontSize)
    {
        if (maxWidthPx <= 0 || fontSize <= 0) return lines;

        var result = new List<List<Fragment>>();

        foreach (var line in lines)
        {
            if (line.Count == 0) { result.Add(line); continue; }

            // A "word" is one or more consecutive styled pieces with no space between them, so a
            // word spanning a style change stays a single unbreakable unit.
            var words       = new List<List<Fragment>>();
            var currentWord = new List<Fragment>();
            var pendingGap  = false; // whether a space separates the previous word from the next

            foreach (var frag in line)
            {
                var parts = frag.Text.Split(' ');
                for (var i = 0; i < parts.Length; i++)
                {
                    if (i > 0)
                    {
                        // A space inside this fragment ends the word in progress.
                        if (currentWord.Count > 0) { words.Add(currentWord); currentWord = []; }
                        pendingGap = true;
                    }
                    if (parts[i].Length > 0) currentWord.Add(new Fragment(parts[i], frag.Style));
                }
            }
            if (currentWord.Count > 0) words.Add(currentWord);
            _ = pendingGap;

            if (words.Count == 0) { result.Add(line); continue; }

            var outLine    = new List<Fragment>();
            var outPlain   = string.Empty;

            foreach (var word in words)
            {
                var wordText  = string.Concat(word.Select(f => f.Text));
                var candidate = outLine.Count == 0 ? wordText : outPlain + " " + wordText;

                if (CalloutTextWrapper.EstimateWidth(candidate, fontSize) <= maxWidthPx || outLine.Count == 0)
                {
                    if (outLine.Count > 0)
                    {
                        // Re-introduce the separating space, attached to the preceding fragment so
                        // it inherits that run's style rather than becoming its own tspan.
                        var last = outLine[^1];
                        outLine[^1] = last with { Text = last.Text + " " };
                    }
                    outLine.AddRange(word);
                    outPlain = candidate;
                }
                else
                {
                    result.Add(outLine);
                    outLine  = [.. word];
                    outPlain = wordText;
                }
            }
            if (outLine.Count > 0) result.Add(outLine);
        }

        return result;
    }

    /// <summary>
    /// Builds the full <c>&lt;tspan&gt;</c> markup for <paramref name="lines"/>. <paramref name="x"/>
    /// is the anchor x for every line's first tspan; <paramref name="firstDy"/>/<paramref name="lineHeight"/>
    /// match the legacy per-line callers' own vertical-centering math exactly. <paramref name="baseFontSize"/>
    /// is the containing overlay/callout's whole-block font size, used to size sub/superscript down.
    /// </summary>
    internal static string BuildTspans(
        List<List<Fragment>> lines, double x, double firstDy, double lineHeight, int baseFontSize)
    {
        var subFontSize = (int)Math.Round(baseFontSize * 0.65);
        var sb = new System.Text.StringBuilder();

        for (var li = 0; li < lines.Count; li++)
        {
            var dy = li == 0 ? firstDy : lineHeight;
            var fragments = lines[li];

            if (fragments.Count == 0)
            {
                // Empty line — still needs a positioning tspan so later lines' dy stays correct.
                sb.Append($"""<tspan x="{F(x)}" dy="{F(dy)}"></tspan>""");
                continue;
            }

            for (var fi = 0; fi < fragments.Count; fi++)
            {
                var (text, style) = fragments[fi];
                var pos = fi == 0 ? $" x=\"{F(x)}\" dy=\"{F(dy)}\"" : string.Empty;

                var attrs = new System.Text.StringBuilder(pos);
                if (style.Bold) attrs.Append(" font-weight=\"bold\"");
                if (style.Underline) attrs.Append(" text-decoration=\"underline\"");
                if (style.Subscript) attrs.Append($" baseline-shift=\"sub\" font-size=\"{subFontSize}\"");
                else if (style.Superscript) attrs.Append($" baseline-shift=\"super\" font-size=\"{subFontSize}\"");
                if (style.Color is not null) attrs.Append($" fill=\"{EscapeXml(style.Color)}\"");

                sb.Append($"<tspan{attrs}>{EscapeXml(text)}</tspan>");
            }
        }

        return sb.ToString();
    }

    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}
