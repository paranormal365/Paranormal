using System.Net;
using Ben.Web.Services.WebApi;
using Ben.Web.Website.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A link to a recording keeps working after the token it was drawn with has been renewed
/// (10/28/2026).
/// </summary>
/// <remarks>
/// A ticket used to hold the access token as it was when the page drew the link. The token lasts an
/// hour and is renewed only when the page next talks to the API, and a session that is simply
/// playing talks to nobody: the recording started answering "not signed in" partway through. These
/// use the real token store and renewer with a mocked refresh endpoint.
/// </remarks>
public sealed class MediaCallerTests
{
    private static readonly Guid Ben = Guid.NewGuid();
    private static readonly Guid File = Guid.NewGuid();

    private static (WebApiTokenStore Store, Mock<IWebApiIdentityClient> Identity, MediaTicketService Tickets, CircuitMediaTokenSource Source)
        Arrange(DateTimeOffset? expires = null)
    {
        var store = new WebApiTokenStore
        {
            AccessToken = "drawn-with",
            RefreshToken = "refresh-1",
            AccessTokenExpiresAtUtc = expires ?? DateTimeOffset.UtcNow.AddMinutes(30),
            UserId = Ben,
        };
        var identity = new Mock<IWebApiIdentityClient>();
        var tickets = new MediaTicketService(new BrowserTicketStore(new MemoryCache(new MemoryCacheOptions())));
        return (store, identity, tickets, new CircuitMediaTokenSource(store, new SessionRenewer(store, identity.Object)));
    }

    private static RefreshOutcome Renewed(string access) =>
        new(new WebApiTokenResponse { AccessToken = access, RefreshToken = "refresh-2", ExpiresIn = 3600 }, false);

    [Fact]
    public async Task A_link_drawn_before_a_renewal_is_sent_with_the_renewed_token()
    {
        var (store, _, tickets, source) = Arrange();
        var ticket = tickets.Protect(File, store.AccessToken!, source);

        store.AccessToken = "renewed-since";   // the page renewed it, making some other call

        var caller = await tickets.CallerAsync(File, ticket, default);
        Assert.Equal("renewed-since", caller!.Token);
    }

    [Fact]
    public async Task A_token_about_to_run_out_is_renewed_before_the_file_is_asked_for()
    {
        var (store, identity, tickets, source) = Arrange(expires: DateTimeOffset.UtcNow.AddSeconds(20));
        identity.Setup(i => i.TryRefreshAsync("refresh-1", It.IsAny<CancellationToken>())).ReturnsAsync(Renewed("fresh"));
        var ticket = tickets.Protect(File, store.AccessToken!, source);

        var caller = await tickets.CallerAsync(File, ticket, default);

        Assert.Equal("fresh", caller!.Token);
        Assert.Equal("fresh", store.AccessToken);
    }

    [Fact]
    public async Task After_somebody_else_signs_in_the_link_keeps_the_token_it_was_drawn_with()
    {
        var (store, _, tickets, source) = Arrange();
        var ticket = tickets.Protect(File, store.AccessToken!, source);

        store.UserId = Guid.NewGuid();
        store.AccessToken = "somebody-else";

        var caller = await tickets.CallerAsync(File, ticket, default);
        Assert.Equal("drawn-with", caller!.Token);
    }

    [Fact]
    public async Task A_ticket_for_another_file_is_still_refused()
    {
        var (store, _, tickets, source) = Arrange();
        var ticket = tickets.Protect(File, store.AccessToken!, source);
        Assert.Null(await tickets.CallerAsync(Guid.NewGuid(), ticket, default));
    }

    // ── The relay: one more try after a refusal ─────────────────────────────

    private sealed class Upstream(Func<string?, HttpStatusCode> answer) : HttpMessageHandler
    {
        public List<string?> TokensSent { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var token = request.Headers.Authorization?.Parameter;
            TokensSent.Add(token);
            return Task.FromResult(new HttpResponseMessage(answer(token)) { Content = new ByteArrayContent([1, 2, 3]) });
        }
    }

    private static async Task<(int Status, List<string?> Sent)> RelayAsync(MediaCaller? caller, Upstream upstream)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(upstream));
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        var result = await MediaProxy.StreamAsync("http://api.test/x", caller, factory.Object, ctx, default);
        var status = result is Microsoft.AspNetCore.Http.HttpResults.StatusCodeHttpResult refused
            ? refused.StatusCode : ctx.Response.StatusCode;
        return (status, upstream.TokensSent);
    }

    [Fact]
    public async Task A_refused_recording_is_asked_for_again_with_a_renewed_token()
    {
        var (store, identity, _, source) = Arrange();
        identity.Setup(i => i.TryRefreshAsync("refresh-1", It.IsAny<CancellationToken>())).ReturnsAsync(Renewed("fresh"));
        var upstream = new Upstream(token => token == "fresh" ? HttpStatusCode.PartialContent : HttpStatusCode.Unauthorized);

        var (status, sent) = await RelayAsync(new MediaCaller("drawn-with", source), upstream);

        Assert.Equal(206, status);
        Assert.Equal(["drawn-with", "fresh"], sent);
    }

    [Fact]
    public async Task A_bare_token_with_nothing_to_renew_it_gets_the_refusal()
    {
        var upstream = new Upstream(_ => HttpStatusCode.Unauthorized);
        var (status, sent) = await RelayAsync("just-a-token", upstream);
        Assert.Equal(401, status);
        Assert.Single(sent);
    }

    [Fact]
    public async Task A_recording_is_passed_on_with_its_length_and_tag()
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new Tagged()));
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Headers.Range = "bytes=0-2";

        await MediaProxy.StreamAsync("http://api.test/x", null, factory.Object, ctx, default);

        Assert.Equal(3, ctx.Response.ContentLength);
        Assert.Equal("\"v1\"", ctx.Response.Headers.ETag.ToString());
        Assert.Equal("bytes 0-2/10", ctx.Response.Headers.ContentRange.ToString());
    }

    private sealed class Tagged : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([1, 2, 3]) };
            response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 2, 10);
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"");
            return Task.FromResult(response);
        }
    }
}
