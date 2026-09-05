using System.Globalization;
using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Models;

/// <summary>
/// Generates inline SVG markup for a <see cref="TextOverlay"/> at a given pixel canvas size.
/// Used for every export render — both static (no motion path) and animated (has a motion path)
/// overlays, via the per-frame SVG rasterization pipeline. Font-family names resolve against the
/// browser's own installed fonts at render time, not an ffmpeg-native font file — this is what makes
/// font selection actually work cross-OS (ffmpeg.wasm's native <c>drawtext</c> filter, used previously,
/// has no working font-file mechanism at all — see backlog item #16's phase-74 notes).
///
/// <para>Position (<see cref="TextOverlay.OverrideX"/>/<see cref="TextOverlay.OverrideY"/>) is treated as
/// the text's top-left anchor when set — matching how the overlay behaves once dragged on the preview or
/// driven by a motion path (see <see cref="Services.ExportArgBuilders.ApplyMotionFrame(TextOverlay, MotionFrame)"/>).
/// When neither override is set (the common, non-dragged static case), position instead comes from
/// <see cref="TextOverlay.HorizontalAlign"/>/<see cref="TextOverlay.VerticalAlign"/>/
/// <see cref="TextOverlay.OffsetX"/>/<see cref="TextOverlay.OffsetY"/>, using SVG's own <c>text-anchor</c>/
/// <c>dominant-baseline</c> to resolve alignment — no ffmpeg-style runtime text measurement needed.</para>
/// </summary>
public static class TextOverlayRenderer
{
    private static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    private static string FI(double v) => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Render the text overlay as a complete SVG string.
    /// </summary>
    /// <param name="overlay">The text overlay (provides text, font, colour, geometry).</param>
    /// <param name="canvasW">Output canvas width in pixels.</param>
    /// <param name="canvasH">Output canvas height in pixels.</param>
    public static string Render(TextOverlay overlay, int canvasW, int canvasH)
    {
        double x, y;
        string anchor, baseline;

        // Which point of the text box the position refers to. Alignment decides that, and it
        // decides it whether or not the position was set by dragging.
        //
        // It used to change underneath the drag. Without an override, a centred bottom title was
        // anchored middle/after-edge — the drag handle sat at the middle of its bottom edge, which
        // is where the text was. The first drag wrote OverrideX/Y and the renderer switched to
        // start/before-edge, so the same numbers now meant the box's TOP-LEFT: the title jumped
        // right by half its width and down by its whole height, usually clean off the frame
        // (2026-09-05 audit, titles-2).
        //
        // Anchoring by alignment in both cases means the handle marks the same point before and
        // after, so a drag moves the title by exactly the distance dragged. A title dragged before
        // this change moves once, to where its handle always claimed it was.
        anchor = overlay.HorizontalAlign switch
        {
            TextHorizontalAlign.Left  => "start",
            TextHorizontalAlign.Right => "end",
            _                         => "middle",
        };

        baseline = overlay.VerticalAlign switch
        {
            TextVerticalAlign.Top    => "text-before-edge",
            TextVerticalAlign.Bottom => "text-after-edge",
            _                        => "middle",
        };

        if (overlay.OverrideX.HasValue || overlay.OverrideY.HasValue)
        {
            x = (overlay.OverrideX ?? MotionEffectiveGeometry.TextAnchorX(overlay, canvasW)) * canvasW;
            y = (overlay.OverrideY ?? MotionEffectiveGeometry.TextAnchorY(overlay, canvasH)) * canvasH;
        }
        else
        {
            x = overlay.HorizontalAlign switch
            {
                TextHorizontalAlign.Left  => overlay.OffsetX,
                TextHorizontalAlign.Right => canvasW - overlay.OffsetX,
                _                         => canvasW / 2.0,
            };
            y = overlay.VerticalAlign switch
            {
                TextVerticalAlign.Top    => overlay.OffsetY,
                TextVerticalAlign.Bottom => canvasH - overlay.OffsetY,
                _                        => canvasH / 2.0,
            };
        }

        var lines      = overlay.Text.Replace("\r\n", "\n").Split('\n');
        var lineHeight = overlay.FontSize * 1.2;

        // How wide the title may draw, in pixels. Null means no limit, which is what titles have
        // always done: a long sentence drew as one line and ran off both sides of the frame
        // (2026-09-05 audit, titles-6). Callouts have wrapped for a while and the wrapping code is
        // shared — only titles had no way to ask for it.
        var wrapWidthPx = overlay.MaxWidth is { } fraction && fraction > 0
            ? fraction * canvasW
            : 0.0;

        var shadow     = SvgShadowFilter.Build(overlay.ShadowColor, overlay.ShadowOffsetX, overlay.ShadowOffsetY, overlay.ShadowBlur);
        var shadowAttr = overlay.ShadowBlur > 0 ? " filter=\"url(#bv-shadow)\"" : string.Empty;

        string tspans;
        string weightAttr, underlineAttr;

        if (overlay.Runs is { Count: > 0 } runs)
        {
            // Item #16 — inline runs are the rendering source of truth once set. Per-run
            // font-weight/text-decoration/fill live on each run's own tspan (RichTextTspanBuilder),
            // so the parent <text> carries neither whole-block attr; fill stays the inherited
            // default for any run whose own Color is null.
            var runLines = RichTextTspanBuilder.SplitIntoLines(runs);
            if (wrapWidthPx > 0)
                runLines = RichTextTspanBuilder.WrapLines(runLines, wrapWidthPx, overlay.FontSize);
            lines = RichTextTspanBuilder.ToPlainLines(runLines);
            var firstDy = baseline == "middle" ? -(lines.Length - 1) * lineHeight / 2 : 0.0;
            tspans        = RichTextTspanBuilder.BuildTspans(runLines, x, firstDy, lineHeight, overlay.FontSize);
            weightAttr    = string.Empty;
            underlineAttr = string.Empty;
        }
        else
        {
            if (wrapWidthPx > 0)
                lines = CalloutTextWrapper.Wrap(lines, wrapWidthPx, overlay.FontSize);

            var sb = new System.Text.StringBuilder();
            // First tspan carries no dy for start/top-anchored text (grows naturally downward from the
            // anchor); for middle-baseline text the whole block is centered around y by offsetting the
            // first line upward by half the total block height, matching CalloutShapeRenderer's approach.
            var firstDy = baseline == "middle" ? -(lines.Length - 1) * lineHeight / 2 : 0.0;
            for (var i = 0; i < lines.Length; i++)
            {
                var dy = i == 0 ? firstDy : lineHeight;
                sb.Append($"""<tspan x="{F(x)}" dy="{F(dy)}">{EscapeXml(lines[i])}</tspan>""");
            }
            tspans        = sb.ToString();
            weightAttr    = overlay.FontBold ? " font-weight=\"bold\"" : string.Empty;
            underlineAttr = overlay.FontUnderline ? " text-decoration=\"underline\"" : string.Empty;
        }

        var box = BuildBox(overlay, x, y, anchor, baseline, lines, lineHeight);

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{FI(canvasW)}" height="{FI(canvasH)}" viewBox="0 0 {FI(canvasW)} {FI(canvasH)}">
              {shadow}
              {box}
              <text y="{F(y)}" font-family="{EscapeXml(overlay.FontFamily)}" font-size="{overlay.FontSize}"
                    fill="{EscapeXml(overlay.FontColor)}" opacity="{F(overlay.Opacity)}"
                    text-anchor="{anchor}" dominant-baseline="{baseline}"{shadowAttr}{weightAttr}{underlineAttr}>{tspans}</text>
            </svg>
            """;
    }

    /// <summary>
    /// Renders an approximate background box behind the text when <see cref="TextOverlay.BoxColor"/> is
    /// set. The size is a padding-based estimate from character count and font size (average-character-
    /// width heuristic), not exact glyph measurement — deliberately simple: this already closes the real
    /// gap (animated/unified text previously had no box at all, backlog item #23), and exact-fit sizing
    /// (e.g. via a browser <c>getBBox()</c> measurement pass) stays a possible future refinement.
    /// </summary>
    private static string BuildBox(
        TextOverlay overlay, double x, double y, string anchor, string baseline,
        string[] lines, double lineHeight)
    {
        if (overlay.BoxColor is null) return string.Empty;

        var maxChars    = lines.Max(l => l.Length);
        var avgCharW    = overlay.FontSize * 0.55;
        var padding     = overlay.FontSize * 0.15;
        var textW       = maxChars * avgCharW;
        var textH       = lines.Length * lineHeight;
        var boxW        = textW + padding * 2;
        var boxH        = textH + padding * 2;

        var boxX = anchor switch
        {
            "start" => x - padding,
            "end"   => x - boxW + padding,
            _       => x - boxW / 2,   // middle
        };
        var boxY = baseline switch
        {
            "text-before-edge" => y - padding,
            "text-after-edge"  => y - boxH + padding,
            _                  => y - boxH / 2,   // middle
        };

        var fill = BoxColorToCss(overlay.BoxColor);
        return $"""<rect x="{F(boxX)}" y="{F(boxY)}" width="{F(boxW)}" height="{F(boxH)}" fill="{fill}" />""";
    }

    /// <summary>Parses the model's <c>"#rrggbb"</c> or <c>"#rrggbb@opacity"</c> box-colour string into a
    /// CSS <c>rgba()</c> string for SVG fill.</summary>
    private static string BoxColorToCss(string boxColor)
    {
        var parts   = boxColor.Split('@');
        var (r, g, b, _) = ColorHelper.Unpack(ColorHelper.FromHex(parts[0]));
        var opacity = parts.Length > 1 &&
                      double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var op)
            ? op : 1.0;
        return $"rgba({r},{g},{b},{opacity.ToString("F3", CultureInfo.InvariantCulture)})";
    }

    /// <summary>Escapes text for safe inclusion as SVG/XML element content or attribute values.</summary>
    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}
