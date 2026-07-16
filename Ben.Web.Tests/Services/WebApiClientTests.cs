using System.Net;
using System.Net.Http.Headers;
using Ben.Web.WebApp.Services.WebApi;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// Tests for WebApiClient's auth header injection.
///
/// Background: WebApiBearerTokenHandler was removed because IHttpClientFactory resolves
/// DelegatingHandlers from the root DI scope, not the Blazor circuit scope, so the injected
/// IWebApiTokenStore was always an empty, unrelated instance. Auth header injection was moved
/// into WebApiClient itself (which IS resolved from the circuit scope as a typed transient),
/// reading IWebApiTokenStore at request time via Auth().
/// </summary>
public class WebApiClientTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private const string BaseUrl   = "http://localhost:5252";
    private const string TestToken = "test-bearer-token-abc123";

    /// <summary>
    /// A minimal HttpMessageHandler that records the last request and returns a fixed response.
    /// </summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public HttpStatusCode ResponseStatus { get; set; } = HttpStatusCode.OK;
        public string ResponseJson { get; set; } = "null";

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(ResponseStatus)
            {
                Content = new StringContent(ResponseJson,
                    System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private static (WebApiClient Client, WebApiTokenStore Store, CapturingHandler Handler)
        Build(string? initialToken = null)
    {
        var handler = new CapturingHandler();
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri(BaseUrl) };
        var store = new WebApiTokenStore { AccessToken = initialToken };
        var client = new WebApiClient(httpClient, store);
        return (client, store, handler);
    }

    // ── GetAsync auth header ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_WhenTokenSet_SendsBearerAuthorizationHeader()
    {
        var (client, _, handler) = Build(TestToken);

        await client.GetAsync<object>("/api/test");

        var auth = handler.LastRequest!.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal(TestToken, auth.Parameter);
    }

    [Fact]
    public async Task GetAsync_WhenNoToken_SendsNoAuthorizationHeader()
    {
        var (client, _, handler) = Build(initialToken: null);

        await client.GetAsync<object>("/api/test");

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task GetAsync_TokenSetAfterConstruction_SendsNewToken()
    {
        // Key regression test: the token store is read at request time, not at construction
        // time. This verifies LoginAsync can set the token then immediately call /api/me
        // and have the correct bearer token sent.
        var (client, store, handler) = Build(initialToken: null);

        // Simulate what LoginAsync does: set token then call /api/me
        store.AccessToken = TestToken;

        await client.GetAsync<object>("/api/me");

        var auth = handler.LastRequest!.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal(TestToken, auth.Parameter);
    }

    [Fact]
    public async Task GetAsync_TokenClearedAfterConstruction_SendsNoHeader()
    {
        var (client, store, handler) = Build(TestToken);

        store.AccessToken = null; // simulate logout

        await client.GetAsync<object>("/api/test");

        Assert.Null(handler.LastRequest!.Headers.Authorization);
    }

    // ── PostAsync auth header ─────────────────────────────────────────────────

    [Fact]
    public async Task PostAsync_WhenTokenSet_SendsBearerAuthorizationHeader()
    {
        var (client, _, handler) = Build(TestToken);
        handler.ResponseJson = "{}";

        await client.PostAsync<object, object>("/api/test", new { });

        var auth = handler.LastRequest!.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal(TestToken, auth.Parameter);
    }

    // ── PutAsync auth header ──────────────────────────────────────────────────

    [Fact]
    public async Task PutAsync_WhenTokenSet_SendsBearerAuthorizationHeader()
    {
        var (client, _, handler) = Build(TestToken);
        handler.ResponseJson = "{}";

        await client.PutAsync<object, object>("/api/test", new { });

        var auth = handler.LastRequest!.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal(TestToken, auth.Parameter);
    }

    // ── DeleteAsync auth header ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_WhenTokenSet_SendsBearerAuthorizationHeader()
    {
        var (client, _, handler) = Build(TestToken);

        await client.DeleteAsync("/api/test");

        var auth = handler.LastRequest!.Headers.Authorization;
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal(TestToken, auth.Parameter);
    }

    // ── HTTP method correctness ───────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_UsesGetHttpMethod()
    {
        var (client, _, handler) = Build(TestToken);
        await client.GetAsync<object>("/api/test");
        Assert.Equal(HttpMethod.Get, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task PostAsync_UsesPostHttpMethod()
    {
        var (client, _, handler) = Build(TestToken);
        handler.ResponseJson = "{}";
        await client.PostAsync<object, object>("/api/test", new { });
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task PutAsync_UsesPutHttpMethod()
    {
        var (client, _, handler) = Build(TestToken);
        handler.ResponseJson = "{}";
        await client.PutAsync<object, object>("/api/test", new { });
        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
    }

    [Fact]
    public async Task DeleteAsync_UsesDeleteHttpMethod()
    {
        var (client, _, handler) = Build(TestToken);
        await client.DeleteAsync("/api/test");
        Assert.Equal(HttpMethod.Delete, handler.LastRequest!.Method);
    }

    // ── Non-success response handling ─────────────────────────────────────────

    [Fact]
    public async Task GetAsync_On401_ReturnsDefault()
    {
        var (client, _, handler) = Build(TestToken);
        handler.ResponseStatus = HttpStatusCode.Unauthorized;

        var result = await client.GetAsync<object>("/api/test");

        Assert.Null(result);
    }

    [Fact]
    public async Task PostAsync_On400_ReturnsDefault()
    {
        var (client, _, handler) = Build(TestToken);
        handler.ResponseStatus = HttpStatusCode.BadRequest;

        var result = await client.PostAsync<object, object>("/api/test", new { });

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_On404_ReturnsFalse()
    {
        var (client, _, handler) = Build(TestToken);
        handler.ResponseStatus = HttpStatusCode.NotFound;

        var result = await client.DeleteAsync("/api/test");

        Assert.False(result);
    }
}
