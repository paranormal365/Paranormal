using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// The pair a case message is stored as: plain <c>Body</c> for the iPhone app, sanitized <c>BodyHtml</c> for the website.
/// </summary>
public sealed class CaseMessageBodiesTests
{
    private static readonly ICmsMarkupSanitizer Sanitizer = new CmsMarkupSanitizer();

    [Fact]
    public void Html_gives_both_forms_with_the_same_words()
    {
        var pair = CaseMessageBodies.Normalise(null, "<p>We found <em>two</em> cold spots.</p><p>More soon.</p>", Sanitizer);
        Assert.NotNull(pair);
        Assert.Equal("We found two cold spots.\n\nMore soon.", pair!.Value.Body);
        Assert.Equal("<p>We found <em>two</em> cold spots.</p><p>More soon.</p>", pair.Value.BodyHtml);
    }

    [Fact]
    public void Html_wins_over_a_plain_body_sent_beside_it() =>
        Assert.Equal("From the editor", CaseMessageBodies.Normalise("stale", "<p>From the editor</p>", Sanitizer)!.Value.Body);

    [Fact]
    public void Plain_only_is_kept_exactly_and_has_no_html()
    {
        var pair = CaseMessageBodies.Normalise("  Tuesday works.\nThanks  ", null, Sanitizer);
        Assert.Equal("Tuesday works.\nThanks", pair!.Value.Body);
        Assert.Null(pair.Value.BodyHtml);
    }

    [Fact]
    public void A_script_is_in_neither_form()
    {
        var pair = CaseMessageBodies.Normalise(null, "<p>ok</p><script>alert('x')</script>", Sanitizer)!.Value;
        Assert.DoesNotContain("script", pair.BodyHtml!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alert", pair.Body);
    }

    [Fact]
    public void A_link_keeps_its_words_in_the_plain_form() =>
        Assert.Equal("See the report",
            CaseMessageBodies.Normalise(null, "<p>See <a href=\"https://example.com/r\">the report</a></p>", Sanitizer)!.Value.Body);

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", "<p></p>")]
    [InlineData(null, "<p>&nbsp;</p>")]
    [InlineData("", "<script>alert(1)</script>")]
    public void Nothing_to_read_is_nothing(string? body, string? html) =>
        Assert.Null(CaseMessageBodies.Normalise(body, html, Sanitizer));

    [Fact]
    public void An_emptied_editor_falls_back_to_a_plain_body_when_there_is_one() =>
        Assert.Equal("plain", CaseMessageBodies.Normalise("plain", "<p></p>", Sanitizer)!.Value.Body);
}
