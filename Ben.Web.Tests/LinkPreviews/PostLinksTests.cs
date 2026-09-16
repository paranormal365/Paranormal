using Ben.Data.Common.Helpers;
using Xunit;

namespace Ben.Web.Tests.LinkPreviews;

public sealed class PostLinksTests
{
    [Theory]
    [InlineData("see https://example.com/deed", null, null)]                         // still being typed
    [InlineData("see https://example.com/deed ", null, "https://example.com/deed")]  // a space after it
    [InlineData("https://example.com/a\nmore", null, "https://example.com/a")]       // a new line after it
    [InlineData(null, "<p>see https://example.com/deed</p>", null)]                   // last thing in the editor
    [InlineData(null, "<p>see https://example.com/deed</p><p>next</p>", "https://example.com/deed")]
    [InlineData(null, "<p>the <a href=\"https://example.com/x\">deed</a></p>", "https://example.com/x")] // made with the link button
    [InlineData("no link at all ", null, null)]
    public void Only_a_finished_link_is_asked_about(string? text, string? html, string? expected) =>
        Assert.Equal(expected, PostLinks.FirstFinished(text, html));

    [Theory]
    [InlineData("see https://example.com/deed", null, "https://example.com/deed")]
    [InlineData(null, "<p>see https://example.com/deed</p>", "https://example.com/deed")]
    [InlineData("see https://exa", null, null)]                                       // not an obvious web link yet
    [InlineData("see https://example.c", null, null)]
    public void Leaving_the_box_finishes_the_link_at_the_end(string? text, string? html, string? expected) =>
        Assert.Equal(expected, PostLinks.FirstFinished(text, html, leftTheBox: true));
}
