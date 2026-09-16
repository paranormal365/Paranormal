using System.Net;
using Ben.Canvas.Core.Model;
using Ben.Canvas.Core.Options;
using Ben.Canvas.Editor.Extensions;
using Ben.Canvas.Editor.Services;
using Ben.Canvas.Tests.Support;

namespace Ben.Canvas.Tests.Services;

/// <summary>
/// Link cards: our own records win, a stranger's page is unfurled by the API only for a signed-in person on https,
/// and anything that does not answer leaves the site and address (R34), never an exception.
/// </summary>
public sealed class TieredLinkPreviewProviderTests
{
    private const string Api = "https://ishaunted.test/webapi";

    private sealed class SignIn(bool signedIn) : ICanvasSignInState
    {
        public bool IsSignedIn => signedIn;
        public string? DisplayName => null;
    }

    /// <summary>Hands out a separate handler per client name, so a test can see which tier was asked.</summary>
    private sealed class TwoClients(HttpMessageHandler anonymous, HttpMessageHandler persistence) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => name == CanvasEditorServiceCollectionExtensions.PublicHttpClientName
            ? new HttpClient(anonymous, disposeHandler: false)
            : new HttpClient(persistence, disposeHandler: false);
    }

    private static TieredLinkPreviewProvider Provider(HttpMessageHandler anonymous, HttpMessageHandler persistence, bool signedIn = true, bool unfurl = true, string? api = Api) =>
        new(new TwoClients(anonymous, persistence),
            Microsoft.Extensions.Options.Options.Create(new CanvasEditorOptions { ApiBaseUrl = api, LinkUnfurl = unfurl }),
            new SignIn(signedIn));

    private const string Unfurled =
        """{"url":"https://example.com/a","host":"example.com","title":"Example Domain","description":"For examples.","imageSourceUrl":"https://example.com/og.png","siteName":"Example"}""";

    [Fact]
    public async Task Our_records_win_and_unfurl_is_never_asked()
    {
        var ours = new StubHandler(HttpStatusCode.OK, """{"kind":"Case","title":"Shelby Street Bridge","subtitle":"Nashville","path":"/cases/x"}""");
        var unfurl = new StubHandler(HttpStatusCode.OK, Unfurled);

        var card = await Provider(ours, unfurl).GetAsync("https://ishaunted.com/cases/x");

        Assert.Equal(LinkPreviewTier.OurRecords, card.Tier);
        Assert.Equal("Shelby Street Bridge", card.Title);
        Assert.Equal("IsHaunted", card.SiteName);
        Assert.Equal($"{Api}/api/public/link-preview?url=https%3A%2F%2Fishaunted.com%2Fcases%2Fx", ours.LastUrl);
        Assert.Empty(unfurl.Requests);
    }

    [Fact]
    public async Task A_strangers_page_is_unfurled_with_its_picture_address_unchanged()
    {
        var ours = new StubHandler(HttpStatusCode.NotFound, "");
        var unfurl = new StubHandler(HttpStatusCode.OK, Unfurled);

        var card = await Provider(ours, unfurl).GetAsync("https://example.com/a");

        Assert.Equal(LinkPreviewTier.Unfurled, card.Tier);
        Assert.Equal("Example Domain", card.Title);
        Assert.Equal("https://example.com/og.png", card.ImageSourceUrl);
        Assert.Equal($"{Api}/api/link-unfurl?url=https%3A%2F%2Fexample.com%2Fa", unfurl.LastUrl);
    }

    [Fact]
    public async Task Nothing_found_keeps_the_site_and_the_address()
    {
        var card = await Provider(new StubHandler(HttpStatusCode.NotFound, ""), new StubHandler(HttpStatusCode.NotFound, ""))
            .GetAsync("https://example.com/a/b");

        Assert.Equal(LinkPreviewTier.HostOnly, card.Tier);
        Assert.Equal("example.com", card.Host);
        Assert.Equal("https://example.com/a/b", card.Url);
        Assert.Null(card.ImageSourceUrl);
    }

    [Fact]
    public async Task Signed_out_never_asks_for_an_unfurl()
    {
        var unfurl = new StubHandler(HttpStatusCode.OK, Unfurled);
        var card = await Provider(new StubHandler(HttpStatusCode.NotFound, ""), unfurl, signedIn: false).GetAsync("https://example.com/a");

        Assert.Equal(LinkPreviewTier.HostOnly, card.Tier);
        Assert.Empty(unfurl.Requests);
    }

    [Fact]
    public async Task An_http_address_is_never_unfurled()
    {
        var unfurl = new StubHandler(HttpStatusCode.OK, Unfurled);
        var card = await Provider(new StubHandler(HttpStatusCode.NotFound, ""), unfurl).GetAsync("http://example.com/a");

        Assert.Equal(LinkPreviewTier.HostOnly, card.Tier);
        Assert.Empty(unfurl.Requests);
    }

    [Fact]
    public async Task Unfurl_switched_off_or_no_api_is_host_only()
    {
        var unfurl = new StubHandler(HttpStatusCode.OK, Unfurled);
        Assert.Equal(LinkPreviewTier.HostOnly, (await Provider(new StubHandler(HttpStatusCode.NotFound, ""), unfurl, unfurl: false).GetAsync("https://example.com/a")).Tier);

        var ours = new StubHandler(HttpStatusCode.OK, "{}");
        Assert.Equal(LinkPreviewTier.HostOnly, (await Provider(ours, unfurl, api: null).GetAsync("https://example.com/a")).Tier);
        Assert.Empty(ours.Requests);
        Assert.Empty(unfurl.Requests);
    }

    [Fact]
    public async Task A_broken_connection_is_host_only_not_a_crash()
    {
        var card = await Provider(new ThrowingHandler(), new ThrowingHandler()).GetAsync("https://example.com/a");
        Assert.Equal(LinkPreviewTier.HostOnly, card.Tier);
    }

    [Fact]
    public async Task Text_that_is_not_an_address_is_its_own_title()
    {
        var card = await Provider(new ThrowingHandler(), new ThrowingHandler()).GetAsync("not a link");
        Assert.Equal(LinkPreviewTier.HostOnly, card.Tier);
        Assert.Equal("not a link", card.Title);
    }

    [Fact]
    public async Task The_same_address_is_asked_once_per_session()
    {
        var ours = new StubHandler(HttpStatusCode.NotFound, "");
        var unfurl = new StubHandler(HttpStatusCode.OK, Unfurled);
        var provider = Provider(ours, unfurl);

        await provider.GetAsync("https://example.com/a");
        await provider.GetAsync("https://example.com/a");

        Assert.Single(unfurl.Requests);
    }

    [Fact]
    public void The_proxy_address_keeps_the_webapi_mount()
    {
        Assert.Equal("https://ishaunted.test/webapi/api/link-unfurl/image?url=https%3A%2F%2Fexample.com%2Fog.png",
            TieredLinkPreviewProvider.ProxyImageUrl(Api + "/", "https://example.com/og.png"));
        Assert.Null(TieredLinkPreviewProvider.ProxyImageUrl(null, "https://example.com/og.png"));
        Assert.Null(TieredLinkPreviewProvider.ProxyImageUrl(Api, null));
    }
}
