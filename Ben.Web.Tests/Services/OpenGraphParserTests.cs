using Ben.Data.WebApi.Services.LinkUnfurl;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>Reading a page's own description of itself for a preview card (canvas plan M6-09).</summary>
public sealed class OpenGraphParserTests
{
    private static readonly Uri Page = new("https://news.example.com/2026/09/haunted-inn");

    [Fact]
    public void Og_tags_win_over_title_and_description_falls_back_to_meta()
    {
        var html = """
            <html><head>
              <title>Plain title | News</title>
              <meta property="og:title" content="The Haunted Inn">
              <meta name="twitter:title" content="Twitter title">
              <meta name="description" content="  A    ghost   story &amp; more.  ">
            </head><body></body></html>
            """;

        var data = OpenGraphParser.Parse(html, Page);

        Assert.Equal("The Haunted Inn", data.Title);
        Assert.Equal("A ghost story & more.", data.Description);
    }

    [Fact]
    public void Twitter_tags_come_before_the_title_element()
    {
        var html = """<head><title>Plain</title><meta name="twitter:title" content="From Twitter"><meta name="twitter:description" content="Tw desc"></head>""";

        var data = OpenGraphParser.Parse(html, Page);

        Assert.Equal("From Twitter", data.Title);
        Assert.Equal("Tw desc", data.Description);
    }

    [Fact]
    public void The_title_element_is_the_last_resort_and_whitespace_is_collapsed()
    {
        var data = OpenGraphParser.Parse("<title>\n  Just   a\tpage\n</title>", Page);
        Assert.Equal("Just a page", data.Title);
    }

    [Fact]
    public void A_relative_og_image_resolves_against_the_page()
    {
        var data = OpenGraphParser.Parse("""<meta property="og:image" content="/img/inn.jpg">""", Page);
        Assert.Equal("https://news.example.com/img/inn.jpg", data.ImageSourceUrl);
    }

    [Fact]
    public void Secure_url_is_preferred()
    {
        var html = """<meta property="og:image" content="https://cdn.example.com/a.jpg"><meta property="og:image:secure_url" content="https://cdn.example.com/secure.jpg">""";
        Assert.Equal("https://cdn.example.com/secure.jpg", OpenGraphParser.Parse(html, Page).ImageSourceUrl);
    }

    [Theory]
    [InlineData("http://cdn.example.com/a.jpg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://localhost/a.jpg")]
    public void An_image_the_proxy_would_refuse_is_dropped(string image)
    {
        var data = OpenGraphParser.Parse($"""<meta property="og:image" content="{image}">""", Page);
        Assert.Null(data.ImageSourceUrl);
    }

    [Fact]
    public void Site_name_falls_back_to_host()
    {
        Assert.Equal("news.example.com", OpenGraphParser.Parse("<title>x</title>", Page).SiteName);
        Assert.Equal("Example News", OpenGraphParser.Parse("""<meta property="og:site_name" content="Example News">""", Page).SiteName);
    }

    [Fact]
    public void A_long_description_is_cut_to_500_characters()
    {
        var data = OpenGraphParser.Parse($"""<meta property="og:description" content="{new string('a', 900)}">""", Page);
        Assert.Equal(500, data.Description!.Length);
    }

    [Fact]
    public void A_page_that_says_nothing_yields_nulls_not_blanks()
    {
        var data = OpenGraphParser.Parse("<html><body><p>nothing here</p></body></html>", Page);
        Assert.Null(data.Title);
        Assert.Null(data.Description);
        Assert.Null(data.ImageSourceUrl);
    }

    [Fact]
    public void Script_in_a_title_is_text_not_markup()
    {
        var data = OpenGraphParser.Parse("""<meta property="og:title" content="&lt;script&gt;alert(1)&lt;/script&gt; Inn">""", Page);
        // Decoded to the characters the page meant; the card renders it as text, never as HTML.
        Assert.Equal("<script>alert(1)</script> Inn", data.Title);
    }
}
