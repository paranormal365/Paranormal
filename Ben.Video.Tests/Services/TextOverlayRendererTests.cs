using Ben.Video.Editor.Effects;
using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Services;

public sealed class TextOverlayRendererTests
{
    private static TextOverlay MakeOverlay() => new()
    {
        Name       = "title",
        Text       = "Hello",
        FontFamily = "Arial",
        FontSize   = 48,
        FontColor  = "#FFFFFF",
        OverrideX  = 0.25,
        OverrideY  = 0.75,
    };

    // ── SVG structure ─────────────────────────────────────────────────────────

    [Fact]
    public void Render_ProducesSvgElement()
    {
        var svg = TextOverlayRenderer.Render(MakeOverlay(), 1920, 1080);
        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);
    }

    [Fact]
    public void Render_ContainsTextElement()
    {
        var svg = TextOverlayRenderer.Render(MakeOverlay(), 1920, 1080);
        Assert.Contains("<text", svg);
        Assert.Contains("Hello", svg);
    }

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(1280, 720)]
    public void Render_SvgHasCorrectDimensions(int w, int h)
    {
        var svg = TextOverlayRenderer.Render(MakeOverlay(), w, h);
        Assert.Contains($"width=\"{w}\"", svg);
        Assert.Contains($"height=\"{h}\"", svg);
    }

    // ── Position ─────────────────────────────────────────────────────────────

    [Fact]
    public void Render_PositionReflectsOverrideXY()
    {
        var overlay = MakeOverlay() with { OverrideX = 0.5, OverrideY = 0.25 };
        var svg     = TextOverlayRenderer.Render(overlay, 1000, 400);

        // x = 0.5 * 1000 = 500.000, y = 0.25 * 400 = 100.000
        Assert.Contains("x=\"500.000\"", svg);
        Assert.Contains("y=\"100.000\"", svg);
    }

    [Theory]
    [InlineData(TextHorizontalAlign.Left,   TextVerticalAlign.Top,    "text-anchor=\"start\"",  "dominant-baseline=\"text-before-edge\"", "20.000",  "30.000")]
    [InlineData(TextHorizontalAlign.Center, TextVerticalAlign.Middle, "text-anchor=\"middle\"", "dominant-baseline=\"middle\"",           "500.000", "200.000")]
    [InlineData(TextHorizontalAlign.Right,  TextVerticalAlign.Bottom, "text-anchor=\"end\"",    "dominant-baseline=\"text-after-edge\"",  "980.000", "370.000")]
    public void Render_NoOverride_UsesAlignmentBasedPositioning(
        TextHorizontalAlign hAlign, TextVerticalAlign vAlign,
        string expectedAnchor, string expectedBaseline, string expectedX, string expectedY)
    {
        var overlay = MakeOverlay() with
        {
            OverrideX = null, OverrideY = null,
            HorizontalAlign = hAlign, VerticalAlign = vAlign,
            OffsetX = 20, OffsetY = 30,
        };
        var svg = TextOverlayRenderer.Render(overlay, 1000, 400);

        Assert.Contains(expectedAnchor, svg);
        Assert.Contains(expectedBaseline, svg);
        Assert.Contains($"x=\"{expectedX}\"", svg);
        Assert.Contains($"y=\"{expectedY}\"", svg);
    }

    [Fact]
    public void Render_HasOverride_UsesTopLeftAnchorRegardlessOfAlignment()
    {
        var overlay = MakeOverlay() with
        {
            OverrideX = 0.5, OverrideY = 0.25,
            HorizontalAlign = TextHorizontalAlign.Right, VerticalAlign = TextVerticalAlign.Bottom,
        };
        var svg = TextOverlayRenderer.Render(overlay, 1000, 400);

        Assert.Contains("text-anchor=\"start\"", svg);
        Assert.Contains("dominant-baseline=\"text-before-edge\"", svg);
    }

    // ── Multi-line ───────────────────────────────────────────────────────────

    [Fact]
    public void Render_MultiLineText_ProducesOneTspanPerLine()
    {
        var overlay = MakeOverlay() with { Text = "Line one\nLine two\nLine three" };
        var svg     = TextOverlayRenderer.Render(overlay, 1920, 1080);

        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(svg, "<tspan").Count);
        Assert.Contains("Line one", svg);
        Assert.Contains("Line two", svg);
        Assert.Contains("Line three", svg);
    }

    // ── Background box ───────────────────────────────────────────────────────

    [Fact]
    public void Render_NoBoxColor_OmitsRect()
    {
        var overlay = MakeOverlay() with { BoxColor = null };
        var svg     = TextOverlayRenderer.Render(overlay, 1920, 1080);
        Assert.DoesNotContain("<rect", svg);
    }

    [Fact]
    public void Render_BoxColorSet_IncludesRect()
    {
        var overlay = MakeOverlay() with { BoxColor = "#000000@0.5" };
        var svg     = TextOverlayRenderer.Render(overlay, 1920, 1080);
        Assert.Contains("<rect", svg);
        Assert.Contains("rgba(0,0,0,0.500)", svg);
    }

    // ── Shadow filter ─────────────────────────────────────────────────────────

    [Fact]
    public void Render_ShadowBlur_IncludesFilterDefAndAttr()
    {
        var overlay = MakeOverlay() with { ShadowBlur = 5.0 };
        var svg     = TextOverlayRenderer.Render(overlay, 1920, 1080);
        Assert.Contains("<defs>", svg);
        Assert.Contains("feDropShadow", svg);
        Assert.Contains("filter=\"url(#bv-shadow)\"", svg);
    }

    [Fact]
    public void Render_NoShadow_OmitsFilterDefAndAttr()
    {
        var overlay = MakeOverlay() with { ShadowBlur = 0 };
        var svg     = TextOverlayRenderer.Render(overlay, 1920, 1080);
        Assert.DoesNotContain("<defs>", svg);
        Assert.DoesNotContain("filter=\"url(#bv-shadow)\"", svg);
    }

    // ── Opacity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Render_OpacityInOutput()
    {
        var overlay = MakeOverlay() with { Opacity = 0.6 };
        var svg     = TextOverlayRenderer.Render(overlay, 1920, 1080);
        Assert.Contains("opacity=\"0.600\"", svg);
    }

    // ── XML escaping ─────────────────────────────────────────────────────────

    [Fact]
    public void Render_EscapesSpecialCharacters_InText()
    {
        var overlay = MakeOverlay() with { Text = "<script>&\"'</script>" };
        var svg     = TextOverlayRenderer.Render(overlay, 1920, 1080);

        Assert.DoesNotContain("<script>", svg);
        Assert.Contains("&lt;script&gt;", svg);
        Assert.Contains("&amp;", svg);
        Assert.Contains("&quot;", svg);
        Assert.Contains("&apos;", svg);
    }

    [Fact]
    public void Render_EscapesAmpersand_InFontFamily()
    {
        var overlay = MakeOverlay() with { FontFamily = "A & B" };
        var svg     = TextOverlayRenderer.Render(overlay, 1920, 1080);
        Assert.Contains("A &amp; B", svg);
    }

    // ── Bold / underline (item #16) ─────────────────────────────────────────────

    [Fact]
    public void Render_FontBoldFalse_OmitsFontWeightAttribute()
    {
        var svg = TextOverlayRenderer.Render(MakeOverlay(), 1920, 1080);
        Assert.DoesNotContain("font-weight", svg);
    }

    [Fact]
    public void Render_FontBoldTrue_EmitsFontWeightBold()
    {
        var overlay = MakeOverlay() with { FontBold = true };
        var svg     = TextOverlayRenderer.Render(overlay, 1920, 1080);
        Assert.Contains("font-weight=\"bold\"", svg);
    }

    [Fact]
    public void Render_FontUnderlineFalse_OmitsTextDecorationAttribute()
    {
        var svg = TextOverlayRenderer.Render(MakeOverlay(), 1920, 1080);
        Assert.DoesNotContain("text-decoration", svg);
    }

    [Fact]
    public void Render_FontUnderlineTrue_EmitsTextDecorationUnderline()
    {
        var overlay = MakeOverlay() with { FontUnderline = true };
        var svg     = TextOverlayRenderer.Render(overlay, 1920, 1080);
        Assert.Contains("text-decoration=\"underline\"", svg);
    }

    [Fact]
    public void Render_BoldAndUnderlineTogether_BothAttributesPresent()
    {
        var overlay = MakeOverlay() with { FontBold = true, FontUnderline = true };
        var svg     = TextOverlayRenderer.Render(overlay, 1920, 1080);
        Assert.Contains("font-weight=\"bold\"", svg);
        Assert.Contains("text-decoration=\"underline\"", svg);
    }

    // ── Inline runs (item #16, phase 115) ───────────────────────────────────────

    [Fact]
    public void Render_RunsPresent_IgnoresWholeBlockFontBoldAndUnderline()
    {
        // FontBold/FontUnderline are set but Runs is also set and its one run has neither flag —
        // Runs must win entirely, not merge with the legacy whole-block attrs.
        var overlay = MakeOverlay() with
        {
            FontBold = true, FontUnderline = true,
            Runs = [new TextRun { Text = "Hello" }],
        };
        var svg = TextOverlayRenderer.Render(overlay, 1920, 1080);
        Assert.DoesNotContain("font-weight", svg);
        Assert.DoesNotContain("text-decoration", svg);
    }

    [Fact]
    public void Render_MultiRunSingleLine_OnlyFirstTspanCarriesX()
    {
        var overlay = MakeOverlay() with
        {
            OverrideX = 0.25, OverrideY = 0.75, // x=250.000, y=300.000 on a 1000x400 canvas
            Runs = [new TextRun { Text = "Hello " }, new TextRun { Text = "World", Bold = true }],
        };
        var svg = TextOverlayRenderer.Render(overlay, 1000, 400);

        Assert.Contains("""<tspan x="250.000" dy="0.000">Hello </tspan>""", svg);
        Assert.Contains("""<tspan font-weight="bold">World</tspan>""", svg);
        // Exactly one tspan on the line carries the positional x — the second run continues
        // immediately after it via SVG's own text-flow, not a second explicit position.
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(svg, "x=\"250.000\""));
    }

    [Fact]
    public void Render_MultiLineRuns_EachLineGetsOwnXAndDy()
    {
        var overlay = MakeOverlay() with
        {
            OverrideX = 0.25, OverrideY = 0.75,
            FontSize = 48, // lineHeight = 48 * 1.2 = 57.6
            Runs = [new TextRun { Text = "Line1\nLine2" }],
        };
        var svg = TextOverlayRenderer.Render(overlay, 1000, 400);

        Assert.Contains("""<tspan x="250.000" dy="0.000">Line1</tspan>""", svg);
        Assert.Contains("""<tspan x="250.000" dy="57.600">Line2</tspan>""", svg);
    }

    [Fact]
    public void Render_RunSubscript_EmitsBaselineShiftSubAndSmallerFontSize()
    {
        var overlay = MakeOverlay() with
        {
            OverrideX = 0.25, OverrideY = 0.75, FontSize = 48,
            Runs = [new TextRun { Text = "2", Subscript = true }],
        };
        var svg = TextOverlayRenderer.Render(overlay, 1000, 400);

        // subFontSize = round(48 * 0.65) = 31
        Assert.Contains("""<tspan x="250.000" dy="0.000" baseline-shift="sub" font-size="31">2</tspan>""", svg);
    }

    [Fact]
    public void Render_RunSuperscript_EmitsBaselineShiftSuper()
    {
        var overlay = MakeOverlay() with
        {
            OverrideX = 0.25, OverrideY = 0.75, FontSize = 48,
            Runs = [new TextRun { Text = "2", Superscript = true }],
        };
        var svg = TextOverlayRenderer.Render(overlay, 1000, 400);

        Assert.Contains("baseline-shift=\"super\" font-size=\"31\"", svg);
    }

    [Fact]
    public void Render_RunColor_OverridesFillOnItsOwnTspan()
    {
        var overlay = MakeOverlay() with
        {
            OverrideX = 0.25, OverrideY = 0.75,
            Runs = [new TextRun { Text = "Red", Color = "#FF0000" }],
        };
        var svg = TextOverlayRenderer.Render(overlay, 1000, 400);

        Assert.Contains("""<tspan x="250.000" dy="0.000" fill="#FF0000">Red</tspan>""", svg);
    }

    [Fact]
    public void Render_RunColorNull_TspanHasNoFillAttribute_InheritsParent()
    {
        var overlay = MakeOverlay() with
        {
            OverrideX = 0.25, OverrideY = 0.75,
            Runs = [new TextRun { Text = "Plain" }],
        };
        var svg = TextOverlayRenderer.Render(overlay, 1000, 400);

        // Exact match — no fill= attribute snuck onto this tspan; the parent <text> element still
        // carries the overlay's own FontColor as fill, which this tspan inherits.
        Assert.Contains("""<tspan x="250.000" dy="0.000">Plain</tspan>""", svg);
    }

    [Fact]
    public void Render_RunsNull_ProducesSameOutputAsBeforePhase115()
    {
        // Legacy path sanity check: Runs left at its default (null) must still hit the exact
        // per-line tspan path, unaffected by any of the new per-run machinery.
        var overlay = MakeOverlay() with { Text = "Line1\nLine2" };
        var svg     = TextOverlayRenderer.Render(overlay, 1000, 400);

        Assert.DoesNotContain("baseline-shift", svg);
        Assert.Contains("Line1", svg);
        Assert.Contains("Line2", svg);
    }
}
