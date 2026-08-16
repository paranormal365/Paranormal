using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Models;

/// <summary>
/// Item #31 — <see cref="CalloutTextWrapper"/>'s greedy wrap. These pin behaviour, not pixel
/// fidelity: the width model is an explicit approximation (see the class's own summary), so every
/// expectation here is derived from <see cref="CalloutTextWrapper.EstimateWidth"/> rather than from
/// any assumption about real glyph metrics.
/// </summary>
public sealed class CalloutTextWrapperTests
{
    private const double FontSize = 20.0;

    /// <summary>Width that fits exactly <paramref name="chars"/> characters under the estimator.</summary>
    private static double WidthFor(int chars) => CalloutTextWrapper.EstimateWidth(new string('x', chars), FontSize);

    [Fact]
    public void ShortLine_IsNotWrapped()
    {
        var result = CalloutTextWrapper.Wrap(["hello world"], WidthFor(40), FontSize);
        Assert.Equal(["hello world"], result);
    }

    [Fact]
    public void LongLine_BreaksOnWordBoundaries()
    {
        // Budget fits ~11 chars, so "aaa bbb ccc" (11) fits but adding " ddd" does not.
        var result = CalloutTextWrapper.Wrap(["aaa bbb ccc ddd"], WidthFor(11), FontSize);

        Assert.Equal(["aaa bbb ccc", "ddd"], result);
        Assert.All(result, line => Assert.DoesNotContain("  ", line));
    }

    [Fact]
    public void ExplicitNewlines_AreAlwaysPreserved()
    {
        // Wrapping may only ADD breaks. Two short lines that would comfortably fit on one must
        // still come back as two.
        var result = CalloutTextWrapper.Wrap(["one", "two"], WidthFor(80), FontSize);
        Assert.Equal(["one", "two"], result);
    }

    [Fact]
    public void WordLongerThanTheLimit_GetsItsOwnLine_RatherThanBeingSplitOrDropped()
    {
        var giant  = new string('W', 40);
        var result = CalloutTextWrapper.Wrap([$"hi {giant} bye"], WidthFor(5), FontSize);

        Assert.Contains(giant, result);                      // never mangled
        Assert.Equal(giant, result.Single(l => l == giant)); // and alone on its line
    }

    [Fact]
    public void NonPositiveWidth_FallsBackToTheCallerLines()
    {
        // A degenerate/too-small shape must not turn into one word per line.
        var result = CalloutTextWrapper.Wrap(["alpha beta gamma"], 0, FontSize);
        Assert.Equal(["alpha beta gamma"], result);
    }

    [Fact]
    public void NonPositiveFontSize_FallsBackToTheCallerLines()
    {
        var result = CalloutTextWrapper.Wrap(["alpha beta gamma"], 500, 0);
        Assert.Equal(["alpha beta gamma"], result);
    }

    [Fact]
    public void EmptyLine_IsPreservedAsABlankLine()
    {
        // A blank line is deliberate vertical spacing in a label; dropping it would silently
        // re-flow the user's text.
        var result = CalloutTextWrapper.Wrap(["a", "", "b"], WidthFor(20), FontSize);
        Assert.Equal(["a", "", "b"], result);
    }

    [Fact]
    public void EstimateWidth_ScalesWithBothLengthAndFontSize()
    {
        Assert.Equal(0, CalloutTextWrapper.EstimateWidth("", 20));
        Assert.True(CalloutTextWrapper.EstimateWidth("abcd", 20) > CalloutTextWrapper.EstimateWidth("ab", 20));
        Assert.True(CalloutTextWrapper.EstimateWidth("ab", 40) > CalloutTextWrapper.EstimateWidth("ab", 20));
    }
}
