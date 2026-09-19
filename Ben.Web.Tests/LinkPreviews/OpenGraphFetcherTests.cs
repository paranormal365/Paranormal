using System.Net;
using System.Text;
using Ben.Data.WebApi.Services.LinkPreviews;
using Xunit;

namespace Ben.Web.Tests.LinkPreviews;

public sealed class OpenGraphFetcherTests
{
    /// <summary>Answers from a table of address → response, and records what was asked for.</summary>
    private sealed class Pages(Func<Uri, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public readonly List<Uri> Asked = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Asked.Add(request.RequestUri!);
            return Task.FromResult(answer(request.RequestUri!));
        }
    }

    private static HttpResponseMessage Html(string html) =>
        new(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };

    private static HttpResponseMessage Redirect(string to)
    {
        var r = new HttpResponseMessage(HttpStatusCode.Found);
        r.Headers.Location = new Uri(to, UriKind.RelativeOrAbsolute);
        return r;
    }

    [Fact]
    public void Open_graph_tags_are_read_and_a_relative_picture_is_made_absolute()
    {
        var summary = OpenGraphFetcher.Parse("""
            <html><head>
              <title>Fallback title</title>
              <meta property="og:title" content="Rest Haven Cemetery &amp; Grounds">
              <meta property="og:description" content="  Founded   1850. ">
              <meta property="og:site_name" content="Find a Grave">
              <meta property="og:image" content="/img/gate.jpg">
            </head></html>
            """, new Uri("https://findagrave.example/memorial/1"));

        Assert.Equal("Rest Haven Cemetery & Grounds", summary.Title);
        Assert.Equal("Founded 1850.", summary.Description);
        Assert.Equal("Find a Grave", summary.SiteName);
        Assert.Equal("https://findagrave.example/img/gate.jpg", summary.ImageUrl?.AbsoluteUri);
    }

    [Fact]
    public void Without_open_graph_the_page_title_and_description_are_used()
    {
        var summary = OpenGraphFetcher.Parse(
            """<html><head><title>County Deeds</title><meta name="description" content="Search deeds."></head></html>""",
            new Uri("https://deeds.example/"));
        Assert.Equal("County Deeds", summary.Title);
        Assert.Equal("Search deeds.", summary.Description);
        Assert.Null(summary.ImageUrl);
    }

    [Fact]
    public void A_picture_address_that_is_not_web_is_ignored()
    {
        var summary = OpenGraphFetcher.Parse("""<meta property="og:image" content="javascript:alert(1)">""", new Uri("https://x.example/"));
        Assert.Null(summary.ImageUrl);
    }

    [Fact]
    public async Task A_page_is_fetched_and_summarised()
    {
        var pages = new Pages(_ => Html("<title>Example Domain</title>"));
        var summary = await new OpenGraphFetcher(new HttpClient(pages)).FetchAsync(new Uri("https://example.com/"), default);
        Assert.Equal("Example Domain", summary.Title);
    }

    [Fact]
    public async Task Something_that_is_not_a_web_page_is_refused()
    {
        var pages = new Pages(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) { Headers = { ContentType = new("application/pdf") } } });
        await Assert.ThrowsAsync<PageFetchRefusedException>(() =>
            new OpenGraphFetcher(new HttpClient(pages)).FetchAsync(new Uri("https://example.com/file.pdf"), default));
    }

    [Fact]
    public async Task Redirects_are_followed_by_hand_and_each_one_is_checked()
    {
        var pages = new Pages(u => u.AbsolutePath switch
        {
            "/start" => Redirect("/middle"),
            "/middle" => Redirect("https://example.org/end"),
            _ => Html("<title>Arrived</title>"),
        });
        var summary = await new OpenGraphFetcher(new HttpClient(pages)).FetchAsync(new Uri("https://example.com/start"), default);
        Assert.Equal("Arrived", summary.Title);
        Assert.Equal("https://example.org/end", summary.FinalUrl.AbsoluteUri);

        var inward = new Pages(_ => Redirect("http://169.254.169.254/latest/meta-data/"));
        await Assert.ThrowsAsync<PageFetchRefusedException>(() =>
            new OpenGraphFetcher(new HttpClient(inward)).FetchAsync(new Uri("https://example.com/"), default));
        Assert.Single(inward.Asked);   // the metadata address was never asked for
    }

    [Fact]
    public async Task More_than_three_redirects_are_refused()
    {
        var hop = 0;
        var pages = new Pages(_ => Redirect($"https://example.com/{++hop}"));
        await Assert.ThrowsAsync<PageFetchRefusedException>(() =>
            new OpenGraphFetcher(new HttpClient(pages)).FetchAsync(new Uri("https://example.com/0"), default));
        Assert.Equal(OpenGraphFetcher.MaxRedirects + 1, pages.Asked.Count);
    }

    [Fact]
    public async Task Only_the_head_of_a_huge_page_is_read()
    {
        var html = "<title>Big</title>" + new string('x', 3 * 1024 * 1024);
        var pages = new Pages(_ => Html(html));
        var summary = await new OpenGraphFetcher(new HttpClient(pages)).FetchAsync(new Uri("https://example.com/"), default);
        Assert.Equal("Big", summary.Title);
    }
}
