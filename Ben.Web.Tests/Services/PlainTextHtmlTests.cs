using Ben.Data.Common.Text;
using Xunit;

namespace Ben.Web.Tests.Services;

public sealed class PlainTextHtmlTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("<p></p>", false)]
    [InlineData("<p>&nbsp;</p>", false)]
    [InlineData("<p><br></p>", false)]
    [InlineData("<p>Knocking</p>", true)]
    [InlineData("plain words", true)]
    [InlineData("<p><img src=\"https://example.com/a.jpg\"></p>", true)]
    public void HasText_asks_about_the_words_not_the_tags(string? html, bool expected) =>
        Assert.Equal(expected, PlainTextHtml.HasText(html));

    [Theory]
    [InlineData("Heard steps on the stairs.", false)]
    [InlineData("3 < 4 and 5 > 2", false)]
    [InlineData("<p>Heard steps</p>", true)]
    [InlineData("line one<br>line two", true)]
    [InlineData("<STRONG>loud</STRONG>", true)]
    public void LooksLikeHtml_tells_editor_output_from_plain_text(string value, bool expected) =>
        Assert.Equal(expected, PlainTextHtml.LooksLikeHtml(value));

    [Fact]
    public void FromPlainText_keeps_paragraphs_and_line_breaks_and_encodes()
    {
        var html = PlainTextHtml.FromPlainText("First line\r\nsecond line\r\n\r\nA <new> paragraph & more");
        Assert.Equal("<p>First line<br>second line</p><p>A &lt;new&gt; paragraph &amp; more</p>", html);
    }

    [Fact]
    public void FromPlainText_of_nothing_is_nothing() => Assert.Equal("", PlainTextHtml.FromPlainText("  \n "));

    [Fact]
    public void ToText_separates_paragraphs_with_a_blank_line_and_decodes()
    {
        var text = PlainTextHtml.ToText("<p>Cold spot &amp; draft</p><ul><li>Hall</li><li>Stairs</li></ul><p>End<br>here</p>");
        Assert.Equal("Cold spot & draft\n\nHall\nStairs\n\nEnd\nhere", text);
    }

    [Fact]
    public void Plain_text_survives_a_round_trip()
    {
        const string original = "Upstairs, 2am.\nDoor opened.\n\nNobody there.";
        Assert.Equal(original, PlainTextHtml.ToText(PlainTextHtml.FromPlainText(original)));
    }
}
