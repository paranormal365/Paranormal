using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

/// <summary>
/// Item #31 — how <see cref="CalloutShapeRenderer"/> lays text out inside the shape. The existing
/// <c>CalloutShapeRendererTests</c> cover the shape/text basics; these cover the alignment,
/// wrapping and text-shadow additions, plus the backward-compatibility guarantee that every new
/// default reproduces the old dead-centre output.
/// </summary>
public sealed class CalloutTextLayoutTests
{
    // 200x100 box at (100, 50) on a 1000x500 canvas.
    private static CalloutClip Clip(string text = "hello") => new()
    {
        Text = text, FontSize = 20, TextPadding = 10,
        X = 0.1, Y = 0.1, Width = 0.2, Height = 0.2,
    };

    private static string Render(CalloutClip c) => CalloutShapeRenderer.Render(c, 1000, 500);

    [Fact]
    public void Defaults_ReproduceTheOriginalCentredAnchor()
    {
        // The whole backward-compat guarantee in one assertion: an untouched clip must still be
        // anchored at the box centre with the original anchor/baseline pair.
        var svg = Render(Clip());

        Assert.Contains("text-anchor=\"middle\"", svg);
        Assert.Contains("dominant-baseline=\"middle\"", svg);
        Assert.Contains("x=\"200.000\"", svg); // 100 + 200/2
        Assert.Contains("y=\"100.000\"", svg); // 50 + 100/2
    }

    [Theory]
    [InlineData(TextHorizontalAlign.Left,   "start",  "110.000")] // pxX + pad
    [InlineData(TextHorizontalAlign.Center, "middle", "200.000")] // centre
    [InlineData(TextHorizontalAlign.Right,  "end",    "290.000")] // pxX + pxW - pad
    public void HorizontalAlign_SetsBothTheAnchorEdgeAndTextAnchor(
        TextHorizontalAlign align, string expectedAnchor, string expectedX)
    {
        var c = Clip();
        c.TextAlign = align;

        var svg = Render(c);

        Assert.Contains($"text-anchor=\"{expectedAnchor}\"", svg);
        Assert.Contains($"x=\"{expectedX}\"", svg);
    }

    [Theory]
    [InlineData(TextVerticalAlign.Top,    "hanging",    "60.000")]  // pxY + pad
    [InlineData(TextVerticalAlign.Middle, "middle",     "100.000")] // centre
    [InlineData(TextVerticalAlign.Bottom, "alphabetic", "140.000")] // pxY + pxH - pad
    public void VerticalAlign_SetsBothTheBaselineAndDominantBaseline(
        TextVerticalAlign align, string expectedBaseline, string expectedY)
    {
        var c = Clip();
        c.TextVerticalAlign = align;

        var svg = Render(c);

        Assert.Contains($"dominant-baseline=\"{expectedBaseline}\"", svg);
        Assert.Contains($"y=\"{expectedY}\"", svg);
    }

    [Fact]
    public void TextWrap_Off_LeavesALongLineIntact()
    {
        var c = Clip("the quick brown fox jumps over the lazy dog");
        var svg = Render(c);

        // One tspan => one line, i.e. nothing was wrapped.
        Assert.Equal(1, CountTspans(svg));
    }

    [Fact]
    public void TextWrap_On_BreaksALongLineIntoSeveral()
    {
        var c = Clip("the quick brown fox jumps over the lazy dog");
        c.TextWrap = true;

        var svg = Render(c);

        Assert.True(CountTspans(svg) > 1,
            "expected the long line to wrap inside a 200px-wide shape at 20px font");
    }

    [Fact]
    public void TextWrap_On_DoesNotChangeShortTextThatAlreadyFits()
    {
        var c = Clip("hi");
        c.TextWrap = true;

        Assert.Equal(1, CountTspans(Render(c)));
    }

    [Fact]
    public void TextShadow_Off_ByDefault_EvenWhenTheShapeHasABlur()
    {
        var c = Clip();
        c.ShadowBlur = 6;

        var svg = Render(c);
        var textEl = TextElement(svg);

        Assert.DoesNotContain("filter=", textEl);
    }

    [Fact]
    public void TextShadow_On_ReferencesTheExistingSharedFilter()
    {
        var c = Clip();
        c.ShadowBlur = 6;
        c.TextShadow = true;

        var svg = Render(c);

        Assert.Contains("filter=\"url(#bv-shadow)\"", TextElement(svg));
        // and reuses the one def rather than emitting a second
        Assert.Equal(1, CountOccurrences(svg, "id=\"bv-shadow\""));
    }

    [Fact]
    public void TextShadow_On_ButZeroBlur_EmitsNoFilterReference()
    {
        // Render() only emits the filter def when blur > 0, so referencing it here would point at
        // a non-existent id and (in some renderers) blank the text entirely.
        var c = Clip();
        c.ShadowBlur = 0;
        c.TextShadow = true;

        Assert.DoesNotContain("filter=", TextElement(Render(c)));
    }

    [Fact]
    public void BottomAligned_MultilineText_LiftsTheBlockSoTheLastLineSitsOnTheBaseline()
    {
        var c = Clip("one\ntwo\nthree");
        c.TextVerticalAlign = TextVerticalAlign.Bottom;

        var svg = Render(c);

        // lineHeight = 20 * 1.2 = 24; first line dy = -(3-1)*24 = -48
        Assert.Contains("dy=\"-48.000\"", svg);
    }

    [Fact]
    public void TopAligned_MultilineText_StartsAtTheBaselineAndGrowsDown()
    {
        var c = Clip("one\ntwo");
        c.TextVerticalAlign = TextVerticalAlign.Top;

        Assert.Contains("dy=\"0.000\"", Render(c));
    }

    // ── Rich-text (Runs) path ────────────────────────────────────────────────
    // These matter more than the plain-text wrap tests: the rich-text editor ALWAYS populates Runs,
    // so this is the path real UI text actually takes. The first cut of this feature wrapped only
    // the plain-text branch, which made the Wrap toggle inert in the app while the plain-text unit
    // tests still passed — caught by live verification, pinned here.

    private static CalloutClip RichClip(params TextRun[] runs)
    {
        var c = Clip(string.Concat(runs.Select(r => r.Text)));
        c.Runs = [.. runs];
        return c;
    }

    [Fact]
    public void TextWrap_On_WrapsTheRunsPathToo_NotJustPlainText()
    {
        var c = RichClip(new TextRun { Text = "the quick brown fox jumps over the lazy dog" });
        c.TextWrap = true;

        Assert.True(CountTspans(Render(c)) > 1,
            "the Runs path must wrap — it is the path the rich-text editor always produces");
    }

    [Fact]
    public void TextWrap_Off_LeavesTheRunsPathUnwrapped()
    {
        var c = RichClip(new TextRun { Text = "the quick brown fox jumps over the lazy dog" });
        Assert.Equal(1, CountTspans(Render(c)));
    }

    [Fact]
    public void WrappingRuns_PreservesEveryCharacterOfTheOriginalText()
    {
        // The wrap must only insert line breaks — never drop, duplicate or reorder text.
        var c = RichClip(
            new TextRun { Text = "alpha beta " },
            new TextRun { Text = "gamma delta", Bold = true });
        c.TextWrap = true;

        var svg = Render(c);
        var rendered = string.Concat(System.Text.RegularExpressions.Regex
            .Matches(svg, "<tspan[^>]*>([^<]*)</tspan>")
            .Select(m => m.Groups[1].Value));

        Assert.Equal("alpha beta gamma delta".Replace(" ", ""), rendered.Replace(" ", ""));
    }

    [Fact]
    public void WrappingRuns_KeepsPerRunStyling()
    {
        var c = RichClip(
            new TextRun { Text = "plain words here " },
            new TextRun { Text = "bolded words there", Bold = true });
        c.TextWrap = true;

        var svg = Render(c);

        // The bold run must still carry its own weight after being re-flowed onto new lines.
        Assert.Contains("font-weight=\"bold\"", svg);
    }

    [Fact]
    public void WrappingRuns_KeepsAWordThatStraddlesAStyleBoundaryIntact()
    {
        // "makeBOLDagain" is one word split across three runs; it must not gain a line break in
        // the middle purely because the formatting changes there.
        var c = RichClip(
            new TextRun { Text = "make" },
            new TextRun { Text = "BOLD", Bold = true },
            new TextRun { Text = "again" });
        c.TextWrap = true;

        var svg = Render(c);
        // All three fragments sit on one line => exactly one line-starting tspan (one with an x).
        var xAnchoredTspans = CountOccurrences(svg, "<tspan x=");
        Assert.Equal(1, xAnchoredTspans);
    }

    private static string TextElement(string svg)
    {
        var i = svg.IndexOf("<text", StringComparison.Ordinal);
        Assert.True(i >= 0, "no <text> element rendered");
        return svg[i..svg.IndexOf('>', i)];
    }

    private static int CountTspans(string svg) => CountOccurrences(svg, "<tspan");

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        for (var i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
