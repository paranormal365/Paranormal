using Ben.Video.Editor.Models;

namespace Ben.Video.Tests.Services;

public sealed class TextRunTests
{
    // ── ToHtml ───────────────────────────────────────────────────────────────

    [Fact]
    public void ToHtml_PlainRun_NoTags()
    {
        var html = TextRun.ToHtml([new TextRun { Text = "Hello" }]);
        Assert.Equal("Hello", html);
    }

    [Fact]
    public void ToHtml_Bold_WrapsInStrong()
    {
        var html = TextRun.ToHtml([new TextRun { Text = "Hello", Bold = true }]);
        Assert.Equal("<strong>Hello</strong>", html);
    }

    [Fact]
    public void ToHtml_Underline_WrapsInU()
    {
        var html = TextRun.ToHtml([new TextRun { Text = "Hello", Underline = true }]);
        Assert.Equal("<u>Hello</u>", html);
    }

    [Fact]
    public void ToHtml_Subscript_WrapsInSub()
    {
        var html = TextRun.ToHtml([new TextRun { Text = "2", Subscript = true }]);
        Assert.Equal("<sub>2</sub>", html);
    }

    [Fact]
    public void ToHtml_Superscript_WrapsInSup()
    {
        var html = TextRun.ToHtml([new TextRun { Text = "2", Superscript = true }]);
        Assert.Equal("<sup>2</sup>", html);
    }

    [Fact]
    public void ToHtml_Color_WrapsInColoredSpan()
    {
        var html = TextRun.ToHtml([new TextRun { Text = "Red", Color = "#ff0000" }]);
        Assert.Equal("""<span style="color:#ff0000">Red</span>""", html);
    }

    [Fact]
    public void ToHtml_AllFlagsTogether_NestsInFixedOrder()
    {
        // color(span) > strong > u > sub — deterministic nesting order.
        var html = TextRun.ToHtml([new TextRun
        {
            Text = "X", Bold = true, Underline = true, Subscript = true, Color = "#00ff00",
        }]);
        Assert.Equal("""<span style="color:#00ff00"><strong><u><sub>X</sub></u></strong></span>""", html);
    }

    [Fact]
    public void ToHtml_EmbeddedNewline_BecomesBr()
    {
        var html = TextRun.ToHtml([new TextRun { Text = "Line1\nLine2" }]);
        Assert.Equal("Line1<br>Line2", html);
    }

    [Fact]
    public void ToHtml_MultipleRuns_ConcatenatedInOrder()
    {
        var html = TextRun.ToHtml(
        [
            new TextRun { Text = "Hello " },
            new TextRun { Text = "World", Bold = true },
        ]);
        Assert.Equal("""Hello <strong>World</strong>""", html);
    }

    [Fact]
    public void ToHtml_EscapesHtmlSpecialCharacters()
    {
        var html = TextRun.ToHtml([new TextRun { Text = "A & B < C" }]);
        Assert.Equal("A &amp; B &lt; C", html);
    }

    [Fact]
    public void ToHtml_EmptyRunList_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, TextRun.ToHtml([]));
    }
}
