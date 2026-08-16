using System.Globalization;
using Ben.Video.Editor.Effects;

namespace Ben.Video.Editor.Models;

/// <summary>
/// Generates inline SVG markup for a <see cref="CalloutClip"/> at a given
/// pixel canvas size, applying the current control-point values.
///
/// <para>The SVG is passed to <see cref="Ben.Video.Editor.Services.SvgFrameRendererService"/>
/// which rasterises it to PNG for export, and also rendered directly as a
/// Blazor SVG element in the editor preview.</para>
/// </summary>
public static class CalloutShapeRenderer
{
    private static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    private static string FI(double v) => ((int)Math.Round(v)).ToString(CultureInfo.InvariantCulture);

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Render the callout as a complete SVG string.
    /// </summary>
    /// <param name="clip">The callout clip (provides shape, colours, geometry).</param>
    /// <param name="canvasW">Output canvas width in pixels.</param>
    /// <param name="canvasH">Output canvas height in pixels.</param>
    public static string Render(CalloutClip clip, int canvasW, int canvasH)
    {
        // Map clip canvas-fraction geometry to pixel coordinates
        var pxX = clip.X * canvasW;
        var pxY = clip.Y * canvasH;
        var pxW = clip.Width  * canvasW;
        var pxH = clip.Height * canvasH;

        var fill   = ColorHelper.ToRgbaCss(clip.FillColor);
        var stroke = ColorHelper.ToRgbaCss(clip.StrokeColor);
        var sw     = clip.StrokeWidth;
        var op     = clip.Opacity;

        var shadow = SvgShadowFilter.Build(clip.ShadowColor, clip.ShadowOffsetX, clip.ShadowOffsetY, clip.ShadowBlur);
        var shape  = clip.Shape switch
        {
            ShapeType.Arrow     => RenderArrow(clip, pxX, pxY, pxW, pxH, canvasW, canvasH, fill, stroke, sw),
            ShapeType.Line      => RenderLine(clip, pxX, pxY, pxW, pxH, canvasW, canvasH, stroke, sw),
            ShapeType.Star      => RenderStar(clip, pxX, pxY, pxW, pxH, fill, stroke, sw),
            ShapeType.Ellipse   => RenderEllipse(clip, pxX, pxY, pxW, pxH, fill, stroke, sw),
            ShapeType.Rectangle => RenderRectangle(clip, pxX, pxY, pxW, pxH, fill, stroke, sw),
            _                   => RenderRectangle(clip, pxX, pxY, pxW, pxH, fill, stroke, sw),
        };

        var rotate = clip.Rotation != 0
            ? $" transform=\"rotate({F(clip.Rotation)} {F(pxX + pxW / 2)} {F(pxY + pxH / 2)})\""
            : string.Empty;

        var text = RenderText(clip, pxX, pxY, pxW, pxH);

        return $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="{FI(canvasW)}" height="{FI(canvasH)}" viewBox="0 0 {FI(canvasW)} {FI(canvasH)}">
              {shadow}
              <g opacity="{F(op)}"{rotate}>
                {shape}
                {text}
              </g>
            </svg>
            """;
    }

    /// <summary>
    /// Renders <see cref="CalloutClip.Text"/> centered on the shape's overall bounding box (a reasonable,
    /// shape-agnostic anchor that works the same for filled shapes like Rectangle/Ellipse/Star and for
    /// path shapes like Arrow/Line, which have no other natural "center"). Supports multiple lines via
    /// <c>\n</c>, split into stacked <c>&lt;tspan&gt;</c> elements. Returns empty string when there is no
    /// text — zero-cost, zero-risk for existing shape-only callouts.
    /// </summary>
    private static string RenderText(CalloutClip clip, double pxX, double pxY, double pxW, double pxH)
    {
        if (string.IsNullOrEmpty(clip.Text)) return string.Empty;

        var fill       = ColorHelper.ToRgbaCss(clip.FontColor);
        var fontFamily = EscapeXml(clip.FontFamily);
        var lineHeight = clip.FontSize * 1.2;
        var pad        = clip.TextPadding;

        // Item #31 — horizontal anchor. SVG's text-anchor positions each line relative to the same
        // x, so alignment is "pick the anchor edge, then tell SVG which end of the line sits there".
        var (anchorX, textAnchor) = clip.TextAlign switch
        {
            TextHorizontalAlign.Left  => (pxX + pad,         "start"),
            TextHorizontalAlign.Right => (pxX + pxW - pad,   "end"),
            _                         => (pxX + pxW / 2,     "middle"),
        };

        string tspans;
        string weightAttr, underlineAttr;
        int    lineCount;

        if (clip.Runs is { Count: > 0 } runs)
        {
            // Item #16 — same per-run tspan generation as TextOverlayRenderer; see its own comment
            // for why the parent <text> carries neither whole-block attr once Runs is set.
            var runLines = RichTextTspanBuilder.SplitIntoLines(runs);
            if (clip.TextWrap)
            {
                // The rich-text editor always emits Runs, so this — not the plain-text branch
                // below — is the path real UI text takes. Wrapping only the plain path would make
                // the Wrap toggle dead in practice.
                runLines = RichTextTspanBuilder.WrapLines(runLines, pxW - 2 * pad, clip.FontSize);
            }
            var plain    = RichTextTspanBuilder.ToPlainLines(runLines);
            lineCount    = plain.Length;
            var firstDy  = FirstLineDy(clip, lineCount, lineHeight, pxH, pad);
            tspans        = RichTextTspanBuilder.BuildTspans(runLines, anchorX, firstDy, lineHeight, clip.FontSize);
            weightAttr    = string.Empty;
            underlineAttr = string.Empty;
        }
        else
        {
            var lines = clip.Text.Replace("\r\n", "\n").Split('\n');
            if (clip.TextWrap)
            {
                lines = CalloutTextWrapper.Wrap(lines, pxW - 2 * pad, clip.FontSize);
            }
            lineCount = lines.Length;

            var startDy = FirstLineDy(clip, lineCount, lineHeight, pxH, pad);

            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < lines.Length; i++)
            {
                var dy = i == 0 ? startDy : lineHeight;
                sb.Append($"""<tspan x="{F(anchorX)}" dy="{F(dy)}">{EscapeXml(lines[i])}</tspan>""");
            }
            tspans        = sb.ToString();
            weightAttr    = clip.FontBold ? " font-weight=\"bold\"" : string.Empty;
            underlineAttr = clip.FontUnderline ? " text-decoration=\"underline\"" : string.Empty;
        }

        // The shadow filter def is already emitted by Render() whenever blur > 0, so opting text in
        // is just a reference — no second filter, no extra defs.
        var shadowAttr = clip is { TextShadow: true, ShadowBlur: > 0 } ? " filter=\"url(#bv-shadow)\"" : string.Empty;

        // Baseline y anchors the whole block; the first tspan's dy (above) does the rest. Top and
        // Bottom switch to a text-edge baseline so the block sits against the padded edge, whereas
        // Middle keeps the original centred behaviour exactly.
        var (baseY, dominantBaseline) = clip.TextVerticalAlign switch
        {
            TextVerticalAlign.Top    => (pxY + pad,       "hanging"),
            TextVerticalAlign.Bottom => (pxY + pxH - pad, "alphabetic"),
            _                        => (pxY + pxH / 2,   "middle"),
        };

        return $"""
            <text x="{F(anchorX)}" y="{F(baseY)}" font-family="{fontFamily}" font-size="{FI(clip.FontSize)}"
                  fill="{fill}" text-anchor="{textAnchor}" dominant-baseline="{dominantBaseline}"{weightAttr}{underlineAttr}{shadowAttr}>{tspans}</text>
            """;
    }

    /// <summary>
    /// Vertical offset of the FIRST line relative to the <c>&lt;text&gt;</c> baseline, which is what
    /// turns a baseline anchor into a laid-out block. Centre keeps the original
    /// <c>-(n-1) × lineHeight / 2</c>; Top starts at the baseline and grows downward; Bottom lifts
    /// the block so its LAST line lands on the baseline.
    /// </summary>
    private static double FirstLineDy(CalloutClip clip, int lineCount, double lineHeight, double pxH, double pad) =>
        clip.TextVerticalAlign switch
        {
            TextVerticalAlign.Top    => 0,
            TextVerticalAlign.Bottom => -(lineCount - 1) * lineHeight,
            _                        => -(lineCount - 1) * lineHeight / 2,
        };

    // ── Shape renderers ───────────────────────────────────────────────────────

    private static string RenderArrow(
        CalloutClip clip,
        double pxX, double pxY, double pxW, double pxH,
        int canvasW, int canvasH,
        string fill, string stroke, double sw)
    {
        var cpv = clip.ControlPointValues;

        // Default: horizontal arrow across the bounding box
        var x1 = cpv.TryGetValue(CalloutControlPoints.StartX, out var v) ? v * canvasW : pxX;
        var y1 = cpv.TryGetValue(CalloutControlPoints.StartY, out v)     ? v * canvasH : pxY + pxH / 2;
        var x2 = cpv.TryGetValue(CalloutControlPoints.EndX,   out v)     ? v * canvasW : pxX + pxW;
        var y2 = cpv.TryGetValue(CalloutControlPoints.EndY,   out v)     ? v * canvasH : pxY + pxH / 2;
        var mx = cpv.TryGetValue(CalloutControlPoints.MidX,   out v)     ? v * canvasW : (x1 + x2) / 2;
        var my = cpv.TryGetValue(CalloutControlPoints.MidY,   out v)     ? v * canvasH : (y1 + y2) / 2;

        // Arrow head size proportional to stroke width
        var headSize = Math.Max(sw * 4, 12.0);

        // Angle of the END SEGMENT (from midpoint to tip) for arrowhead orientation
        var dx = x2 - mx;
        var dy = y2 - my;
        // Guard: if the end point equals the control point, fall back to start→end angle
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
        {
            dx = x2 - x1;
            dy = y2 - y1;
        }
        // Guard: completely degenerate arrow — skip the arrowhead
        if (Math.Abs(dx) < 0.5 && Math.Abs(dy) < 0.5)
        {
            var shadowOnly = clip.ShadowBlur > 0 ? " filter=\"url(#bv-shadow)\"" : string.Empty;
            return $"""<path d="M {F(x1)} {F(y1)} Q {F(mx)} {F(my)} {F(x2)} {F(y2)}"""
                + $""" fill="none" stroke="{stroke}" stroke-width="{F(sw)}" stroke-linecap="round"{shadowOnly} />""";
        }

        var angle = Math.Atan2(dy, dx);
        // ±30° (PI/6) gives a clean, proportional arrowhead
        const double headHalfAngle = Math.PI / 6.0;
        var a1 = angle + Math.PI - headHalfAngle;
        var a2 = angle + Math.PI + headHalfAngle;

        var h1x = x2 + headSize * Math.Cos(a1);
        var h1y = y2 + headSize * Math.Sin(a1);
        var h2x = x2 + headSize * Math.Cos(a2);
        var h2y = y2 + headSize * Math.Sin(a2);

        var shadowAttr = clip.ShadowBlur > 0 ? " filter=\"url(#bv-shadow)\"" : string.Empty;

        return $"""
            <path d="M {F(x1)} {F(y1)} Q {F(mx)} {F(my)} {F(x2)} {F(y2)}"
                  fill="none" stroke="{stroke}" stroke-width="{F(sw)}"
                  stroke-linecap="round"{shadowAttr} />
            <polygon points="{F(x2)},{F(y2)} {F(h1x)},{F(h1y)} {F(h2x)},{F(h2y)}"
                     fill="{stroke}"{shadowAttr} />
            """;
    }

    private static string RenderLine(
        CalloutClip clip,
        double pxX, double pxY, double pxW, double pxH,
        int canvasW, int canvasH,
        string stroke, double sw)
    {
        var cpv = clip.ControlPointValues;
        var x1 = cpv.TryGetValue(CalloutControlPoints.StartX, out var v) ? v * canvasW : pxX;
        var y1 = cpv.TryGetValue(CalloutControlPoints.StartY, out v)     ? v * canvasH : pxY + pxH / 2;
        var x2 = cpv.TryGetValue(CalloutControlPoints.EndX,   out v)     ? v * canvasW : pxX + pxW;
        var y2 = cpv.TryGetValue(CalloutControlPoints.EndY,   out v)     ? v * canvasH : pxY + pxH / 2;
        var mx = cpv.TryGetValue(CalloutControlPoints.MidX,   out v)     ? v * canvasW : (x1 + x2) / 2;
        var my = cpv.TryGetValue(CalloutControlPoints.MidY,   out v)     ? v * canvasH : (y1 + y2) / 2;

        var shadow = clip.ShadowBlur > 0 ? " filter=\"url(#bv-shadow)\"" : string.Empty;
        return $"""
            <path d="M {F(x1)} {F(y1)} Q {F(mx)} {F(my)} {F(x2)} {F(y2)}"
                  fill="none" stroke="{stroke}" stroke-width="{F(sw)}"
                  stroke-linecap="round"{shadow} />
            """;
    }

    private static string RenderEllipse(
        CalloutClip clip,
        double pxX, double pxY, double pxW, double pxH,
        string fill, string stroke, double sw)
    {
        var cx = pxX + pxW / 2;
        var cy = pxY + pxH / 2;
        var shadowAttr = clip.ShadowBlur > 0 ? " filter=\"url(#bv-shadow)\"" : string.Empty;
        return $"""
            <ellipse cx="{F(cx)}" cy="{F(cy)}" rx="{F(pxW / 2)}" ry="{F(pxH / 2)}"
                     fill="{fill}" stroke="{stroke}" stroke-width="{F(sw)}"{shadowAttr} />
            """;
    }

    private static string RenderRectangle(
        CalloutClip clip,
        double pxX, double pxY, double pxW, double pxH,
        string fill, string stroke, double sw)
    {
        var rx = clip.ControlPointValues.TryGetValue(CalloutControlPoints.CornerRadius, out var cr)
            ? cr : 4.0;
        var shadowAttr = clip.ShadowBlur > 0 ? " filter=\"url(#bv-shadow)\"" : string.Empty;
        return $"""
            <rect x="{F(pxX)}" y="{F(pxY)}" width="{F(pxW)}" height="{F(pxH)}"
                  rx="{F(rx)}" ry="{F(rx)}"
                  fill="{fill}" stroke="{stroke}" stroke-width="{F(sw)}"{shadowAttr} />
            """;
    }

    private static string RenderStar(
        CalloutClip clip,
        double pxX, double pxY, double pxW, double pxH,
        string fill, string stroke, double sw)
    {
        var cpv     = clip.ControlPointValues;
        var outer   = cpv.TryGetValue(CalloutControlPoints.OuterRadius, out var v) ? v : 0.9;
        var inner   = cpv.TryGetValue(CalloutControlPoints.InnerRadius, out v)     ? v : 0.4;
        var nPoints = (int)Math.Round(cpv.TryGetValue(CalloutControlPoints.Points, out v) ? v : 5);
        nPoints     = Math.Max(3, nPoints);

        var cx = pxX + pxW / 2;
        var cy = pxY + pxH / 2;
        var r1 = Math.Min(pxW, pxH) / 2 * outer;
        var r2 = Math.Min(pxW, pxH) / 2 * inner;

        var pts = new System.Text.StringBuilder();
        for (var i = 0; i < nPoints * 2; i++)
        {
            var angle = (Math.PI * i / nPoints) - Math.PI / 2;
            var r     = i % 2 == 0 ? r1 : r2;
            var x     = cx + r * Math.Cos(angle);
            var y     = cy + r * Math.Sin(angle);
            if (i > 0) pts.Append(' ');
            pts.Append($"{F(x)},{F(y)}");
        }

        var shadowAttr = clip.ShadowBlur > 0 ? " filter=\"url(#bv-shadow)\"" : string.Empty;
        return $"""
            <polygon points="{pts}"
                     fill="{fill}" stroke="{stroke}" stroke-width="{F(sw)}"{shadowAttr} />
            """;
    }

    // ── Default control-point values for a new clip ───────────────────────────

    /// <summary>
    /// Populate <see cref="CalloutClip.ControlPointValues"/> with sensible defaults
    /// when a new callout is added to the timeline.
    /// </summary>
    public static void SetDefaults(CalloutClip clip)
    {
        switch (clip.Shape)
        {
            case ShapeType.Arrow:
            case ShapeType.Line:
                clip.ControlPointValues[CalloutControlPoints.StartX] = clip.X;
                clip.ControlPointValues[CalloutControlPoints.StartY] = clip.Y + clip.Height / 2;
                clip.ControlPointValues[CalloutControlPoints.EndX]   = clip.X + clip.Width;
                clip.ControlPointValues[CalloutControlPoints.EndY]   = clip.Y + clip.Height / 2;
                clip.ControlPointValues[CalloutControlPoints.MidX]   = clip.X + clip.Width / 2;
                clip.ControlPointValues[CalloutControlPoints.MidY]   = clip.Y + clip.Height / 2;
                break;
            case ShapeType.Star:
                clip.ControlPointValues[CalloutControlPoints.OuterRadius] = 0.9;
                clip.ControlPointValues[CalloutControlPoints.InnerRadius] = 0.4;
                clip.ControlPointValues[CalloutControlPoints.Points]      = 5;
                break;
            case ShapeType.Rectangle:
                clip.ControlPointValues[CalloutControlPoints.CornerRadius] = 4.0;
                break;
            // Ellipse has no adjustable control points — explicit no-op
            case ShapeType.Ellipse:
            case ShapeType.Custom:
                break;
        }
    }

    /// <summary>Escapes text for safe inclusion as SVG/XML element content or attribute values.</summary>
    private static string EscapeXml(string text) =>
        text.Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
}
