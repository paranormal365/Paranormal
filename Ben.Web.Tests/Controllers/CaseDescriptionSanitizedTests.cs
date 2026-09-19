using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services;
using Xunit;

namespace Ben.Web.Tests.Controllers;

/// <summary>
/// A case description is cleaned before it is stored, and an emptied editor stores no description.
/// </summary>
/// <remarks>
/// The description is rendered as markup on the case and, once published, on the public case page. It was stored
/// exactly as sent until 2026-09-14, when the formatting editor on Edit Case and New Case made the gap plain.
/// </remarks>
public sealed class CaseDescriptionSanitizedTests
{
    private static readonly ICmsMarkupSanitizer Sanitizer = new CmsMarkupSanitizer();

    [Fact]
    public void A_script_is_removed_and_the_words_stay()
    {
        var clean = CaseController.CleanDescription("<p>Footsteps</p><script>alert(1)</script>", Sanitizer);
        Assert.NotNull(clean);
        Assert.DoesNotContain("<script", clean, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Footsteps", clean);
    }

    [Fact]
    public void An_event_handler_is_removed()
    {
        var clean = CaseController.CleanDescription("<p onclick=\"steal()\">Cold spot</p><img src=x onerror=\"steal()\">", Sanitizer);
        Assert.NotNull(clean);
        Assert.DoesNotContain("onclick", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", clean, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_javascript_link_loses_its_href()
    {
        var clean = CaseController.CleanDescription("<p><a href=\"javascript:steal()\">see</a></p>", Sanitizer);
        Assert.DoesNotContain("javascript:", clean!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p></p>")]
    [InlineData("<p>&nbsp;</p>")]
    [InlineData("<script>alert(1)</script>")]
    public void Nothing_left_to_read_is_no_description(string? sent) =>
        Assert.Null(CaseController.CleanDescription(sent, Sanitizer));

    [Fact]
    public void Formatting_the_toolbar_offers_survives()
    {
        const string html = "<p><strong>Loud</strong> and <em>close</em></p><ul><li>Hall</li></ul><p><a href=\"https://example.com/\">notes</a></p>";
        var clean = CaseController.CleanDescription(html, Sanitizer)!;
        Assert.Contains("<strong>Loud</strong>", clean);
        Assert.Contains("<em>close</em>", clean);
        Assert.Contains("<li>Hall</li>", clean);
        Assert.Contains("href=\"https://example.com/\"", clean);
    }

    [Fact]
    public void Plain_text_from_before_the_editor_is_kept_as_it_was() =>
        Assert.Equal("Heard knocking in the hall", CaseController.CleanDescription("  Heard knocking in the hall ", Sanitizer));
}
