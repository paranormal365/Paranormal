using System.Net;
using Ben.Web.Services.WebApi;
using Moq;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A session that expires is renewed; one the API refuses and cannot renew is ended — and an outage
/// never ends one (10/14/2026).
/// </summary>
/// <remarks>
/// Ben came back to the site to a menu showing him signed in and every page saying he had been
/// signed out: nothing ever renewed the access token. These drive <see cref="SessionRenewalHandler"/>
/// over a stub network, with the real token store and a mocked refresh endpoint.
/// </remarks>
public sealed class SessionRenewalTests
{
    private static readonly Guid Ben = Guid.NewGuid();

    private sealed class Network(Func<HttpRequestMessage, int, HttpStatusCode> answer) : HttpMessageHandler
    {
        public List<string?> TokensSent { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            TokensSent.Add(request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(answer(request, TokensSent.Count)));
        }
    }

    private static (HttpClient Client, WebApiTokenStore Store, Mock<IWebApiIdentityClient> Identity, Network Net, SessionRenewer Renewer)
        Arrange(Func<HttpRequestMessage, int, HttpStatusCode> answer, DateTimeOffset? expires = null)
    {
        var store = new WebApiTokenStore
        {
            AccessToken = "old",
            RefreshToken = "refresh-1",
            AccessTokenExpiresAtUtc = expires ?? DateTimeOffset.UtcNow.AddMinutes(30),
            UserId = Ben,
            IsSuperAdmin = true,
        };
        var identity = new Mock<IWebApiIdentityClient>();
        var net = new Network(answer);
        var client = new HttpClient(new SessionRenewalHandler { InnerHandler = net }) { BaseAddress = new Uri("http://api.test") };
        return (client, store, identity, net, new SessionRenewer(store, identity.Object));
    }

    private static HttpRequestMessage Get(SessionRenewer renewer, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/api/me/desk");
        req.Headers.Authorization = new("Bearer", token);
        req.Options.Set(SessionRenewalHandler.Key, renewer);
        return req;
    }

    private static RefreshOutcome Renewed(string access) =>
        new(new WebApiTokenResponse { AccessToken = access, RefreshToken = "refresh-2", ExpiresIn = 3600 }, false);

    [Fact]
    public async Task A_token_about_to_expire_is_renewed_before_the_request_goes()
    {
        var (client, store, identity, net, renewer) = Arrange((_, _) => HttpStatusCode.OK, DateTimeOffset.UtcNow.AddSeconds(10));
        identity.Setup(i => i.TryRefreshAsync("refresh-1", It.IsAny<CancellationToken>())).ReturnsAsync(Renewed("new"));

        var response = await client.SendAsync(Get(renewer, "old"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["new"], net.TokensSent);
        Assert.Equal("new", store.AccessToken);
        Assert.Equal("refresh-2", store.RefreshToken);
        // Only the tokens change: who the person is stays exactly as sign-in established it.
        Assert.Equal(Ben, store.UserId);
        Assert.True(store.IsSuperAdmin);
    }

    [Fact]
    public async Task A_refused_request_is_renewed_and_sent_again()
    {
        var (client, store, identity, net, renewer) = Arrange((_, n) => n == 1 ? HttpStatusCode.Unauthorized : HttpStatusCode.OK);
        identity.Setup(i => i.TryRefreshAsync("refresh-1", It.IsAny<CancellationToken>())).ReturnsAsync(Renewed("new"));
        var ended = 0;
        store.SessionEnded += () => ended++;

        var response = await client.SendAsync(Get(renewer, "old"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["old", "new"], net.TokensSent);
        Assert.Equal(0, ended);
    }

    [Fact]
    public async Task A_refused_session_that_cannot_be_renewed_is_ended()
    {
        var (client, store, identity, _, renewer) = Arrange((_, _) => HttpStatusCode.Unauthorized);
        identity.Setup(i => i.TryRefreshAsync("refresh-1", It.IsAny<CancellationToken>())).ReturnsAsync(new RefreshOutcome(null, Refused: true));
        var ended = 0;
        store.SessionEnded += () => ended++;

        var response = await client.SendAsync(Get(renewer, "old"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task An_outage_during_renewal_never_ends_the_session()
    {
        // A 5xx, a 429, a 408 or no connection says nothing about the refresh token.
        var (client, store, identity, _, renewer) = Arrange((_, _) => HttpStatusCode.Unauthorized);
        identity.Setup(i => i.TryRefreshAsync("refresh-1", It.IsAny<CancellationToken>())).ReturnsAsync(RefreshOutcome.Unanswered);
        var ended = 0;
        store.SessionEnded += () => ended++;

        await client.SendAsync(Get(renewer, "old"));

        Assert.Equal(0, ended);
        Assert.Equal("old", store.AccessToken);
        Assert.Equal("refresh-1", store.RefreshToken);
    }

    [Fact]
    public void Ending_is_only_for_the_token_that_was_refused()
    {
        // A request that left with "old" can come back refused after the person signed in again
        // as "current"; that refusal says nothing about the session there is now.
        var (_, store, _, _, renewer) = Arrange((_, _) => HttpStatusCode.Unauthorized);
        store.AccessToken = "current";
        var ended = 0;
        store.SessionEnded += () => ended++;

        renewer.End("old");
        Assert.Equal(0, ended);

        renewer.End("current");
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task Many_requests_with_an_expired_token_renew_it_once()
    {
        var (client, _, identity, net, renewer) = Arrange((_, _) => HttpStatusCode.OK, DateTimeOffset.UtcNow.AddSeconds(-5));
        var calls = 0;
        identity.Setup(i => i.TryRefreshAsync("refresh-1", It.IsAny<CancellationToken>()))
            .Returns(async () => { Interlocked.Increment(ref calls); await Task.Delay(50); return Renewed("new"); });

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.SendAsync(Get(renewer, "old"))));

        Assert.Equal(1, calls);
        Assert.All(net.TokensSent, t => Assert.Equal("new", t));
    }

    [Fact]
    public async Task A_refused_request_with_a_body_is_not_sent_twice()
    {
        // A body may already have been read; the renewed session is there for the next try.
        var (client, store, identity, net, renewer) = Arrange((_, _) => HttpStatusCode.Unauthorized);
        identity.Setup(i => i.TryRefreshAsync("refresh-1", It.IsAny<CancellationToken>())).ReturnsAsync(Renewed("new"));
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/things") { Content = new StringContent("{}") };
        req.Options.Set(SessionRenewalHandler.Key, renewer);

        var response = await client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Single(net.TokensSent);
        Assert.Equal("new", store.AccessToken);
    }

    [Fact]
    public async Task A_request_with_no_session_passes_straight_through()
    {
        var (client, _, identity, net, _) = Arrange((_, _) => HttpStatusCode.Unauthorized);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/public/things"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal([null], net.TokensSent);
        identity.VerifyNoOtherCalls();
    }
}
