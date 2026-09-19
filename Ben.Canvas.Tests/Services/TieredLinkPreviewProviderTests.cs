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

    /// <summary>What the server answers when it has kept a preview: our own copy of the picture.</summary>
    private const string Kept =
        """{"kind":"example.com","title":"Example Domain","subtitle":null,"path":"https://example.com/a","description":"For examples.","imageUrl":"/media/link-preview/3fa85f64-5717-4562-b3fc-2c963f66afa6","siteName":"Example","domain":"example.com"}""";

    /// <summary>
    /// The picture comes back as an address any browser can load, which is the whole point: the proxy
    /// needs a bearer token and a published board is read by people who have none (Ben, 2026-09-17).
    /// </summary>
    /// <summary>
    /// A board with no account behind it asks the anonymous endpoint, which answers with a preview
    /// somebody signed-in had kept earlier — so the card has its picture without this board being
    /// able to make the server read anybody's page (Ben, 2026-09-17).
    /// </summary>
    [Fact]
    public async Task Signed_out_a_kept_card_still_arrives_whole()
    {
        var ours = new StubHandler(HttpStatusCode.OK, Kept);
        var api = new StubHandler(HttpStatusCode.OK, Unfurled);

        var card = await Provider(ours, api, signedIn: false).GetAsync("https://example.com/a");

        Assert.Equal(LinkPreviewTier.Unfurled, card.Tier);
        Assert.Equal("Example Domain", card.Title);
        Assert.Equal("For examples.", card.Description);
        Assert.Equal("/api/public/link-previews/3fa85f64-5717-4562-b3fc-2c963f66afa6/thumbnail", card.ImageUrl);
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task A_kept_preview_brings_our_own_copy_of_the_picture()
    {
        var ours = new StubHandler(HttpStatusCode.NotFound, "");
        var api = new StubHandler(HttpStatusCode.OK, Kept);

        var card = await Provider(ours, api).GetAsync("https://example.com/a");

        Assert.Equal(LinkPreviewTier.Unfurled, card.Tier);
        Assert.Equal("Example Domain", card.Title);
        Assert.Equal("Example", card.SiteName);
        Assert.Equal("/api/public/link-previews/3fa85f64-5717-4562-b3fc-2c963f66afa6/thumbnail", card.ImageUrl);
        Assert.Null(card.ImageSourceUrl);
        Assert.Equal($"{Api}/api/link-previews", api.LastUrl);
        Assert.Equal(HttpMethod.Post, api.LastMethod);
    }

    /// <summary>A picture address of any other shape is refused rather than passed to an img src.</summary>
    [Theory]
    [InlineData("https://example.com/og.png")]
    [InlineData("/media/link-preview/not-a-guid")]
    [InlineData("/media/other/3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    [InlineData("")]
    [InlineData(null)]
    public void Only_a_kept_previews_own_path_becomes_a_picture(string? imageUrl) =>
        Assert.Null(TieredLinkPreviewProvider.ThumbnailPath(imageUrl));

    [Fact]
    public async Task A_strangers_page_is_unfurled_with_its_picture_address_unchanged()
    {
        var ours = new StubHandler(HttpStatusCode.NotFound, "");
        // Nothing kept, so the older unfurl answers: the keeping service is asked first and this is
        // the fallback, which needs no storage.
        var unfurl = new StubHandler(HttpStatusCode.NotFound, "").Then(HttpStatusCode.OK, Unfurled);

        var card = await Provider(ours, unfurl).GetAsync("https://example.com/a");

        Assert.Equal(LinkPreviewTier.Unfurled, card.Tier);
        Assert.Equal("Example Domain", card.Title);
        Assert.Equal("https://example.com/og.png", card.ImageSourceUrl);
        Assert.Null(card.ImageUrl);
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
