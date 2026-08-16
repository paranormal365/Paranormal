namespace Ben.Video.Editor.Models;

/// <summary>
/// One inline-styled fragment of a <see cref="TextOverlay"/>/<see cref="CalloutClip"/>'s text
/// (backlog item #16 — inline mixed formatting + subscript/superscript). A run's <see cref="Text"/>
/// may contain <c>'\n'</c> line-break characters; a run never crosses a style boundary — every
/// change in Bold/Underline/Subscript/Superscript/Color starts a new run.
///
/// <para>Ordered lists of runs are the rendering source of truth once present — see
/// <see cref="TextOverlayRenderer.Render"/>/<see cref="CalloutShapeRenderer.RenderText"/>'s
/// per-(line, run) <c>&lt;tspan&gt;</c> generation. <c>Runs = null</c>/empty means "use the
/// containing overlay/callout's whole-block <c>Text</c>/<c>FontBold</c>/<c>FontUnderline</c>
/// exactly as before this phase" — fully backward compatible with every project saved before this
/// field existed.</para>
/// </summary>
public sealed record TextRun
{
    /// <summary>This run's text. May contain <c>'\n'</c> for a line break within the run's own style.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Bold weight for this run only.</summary>
    public bool Bold { get; set; }

    /// <summary>Underline for this run only.</summary>
    public bool Underline { get; set; }

    /// <summary>Subscript for this run only. Mutually exclusive with <see cref="Superscript"/> in
    /// practice (the editor toolbar toggles one or the other), but both flags are independent bools
    /// so nothing crashes if a hand-edited project sets both — <see cref="TextOverlayRenderer"/>
    /// treats Subscript as taking priority when both are set.</summary>
    public bool Subscript { get; set; }

    /// <summary>Superscript for this run only. See <see cref="Subscript"/>.</summary>
    public bool Superscript { get; set; }

    /// <summary>Fill colour for this run only, as <c>"#rrggbb"</c>. Null = inherit the containing
    /// overlay/callout's own whole-block <c>FontColor</c>.</summary>
    public string? Color { get; set; }

    /// <summary>
    /// Renders <paramref name="runs"/> back to HTML for populating a <c>TelerikEditor</c>'s
    /// <c>Value</c> — the reverse of <c>richTextRunsInterop.js</c>'s <c>htmlToRuns</c>. Pure C#
    /// (no DOM parser needed for this direction); nesting order is fixed
    /// (colour span &gt; strong &gt; u &gt; sub/sup) so output is deterministic and testable.
    /// Embedded <c>'\n'</c> characters become <c>&lt;br&gt;</c>.
    /// </summary>
    public static string ToHtml(IEnumerable<TextRun> runs)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var run in runs)
        {
            var lines = run.Text.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append("<br>");
                if (lines[i].Length == 0) continue;

                var inner = EscapeHtml(lines[i]);
                if (run.Subscript) inner = $"<sub>{inner}</sub>";
                else if (run.Superscript) inner = $"<sup>{inner}</sup>";
                if (run.Underline) inner = $"<u>{inner}</u>";
                if (run.Bold) inner = $"<strong>{inner}</strong>";
                if (run.Color is not null) inner = $"""<span style="color:{run.Color}">{inner}</span>""";

                sb.Append(inner);
            }
        }
        return sb.ToString();
    }

    private static string EscapeHtml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}
