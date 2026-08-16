using System.Net;
using Ben.Video.Core.SidecarContracts;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// Proves every defense in DESIGN-item38-long-form-memory.md §5.4's threat model actually holds
/// against the real ASP.NET pipeline — not just described in a doc. This is the test class that
/// justifies the security mandate: if any of these regress, the sidecar is no longer safe to ship.
/// </summary>
public sealed class SecurityTests : IClassFixture<SidecarWebApplicationFactory>
{
    private readonly SidecarWebApplicationFactory _factory;

    public SecurityTests(SidecarWebApplicationFactory factory) => _factory = factory;

    // ── Health is the one endpoint reachable without a token or Origin ──────

    [Fact]
    public async Task Health_NoOriginNoToken_Succeeds()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_NeverExposesTheToken()
    {
        using var client = _factory.CreateClient();
        var body = await client.GetStringAsync("/v1/health");
        var realToken = _factory.ReadGeneratedPairingToken();
        Assert.DoesNotContain(realToken, body);
    }

    // ── Status requires both Origin and token ────────────────────────────────

    [Theory]
    [InlineData("/v1/status")]
    [InlineData("/v1/sources/11111111-2222-3333-4444-555555555555")]
    public async Task MissingToken_ValidOrigin_Returns401(string path)
    {
        using var client = _factory.CreateAuthenticatedClient(token: null);
        var response = await client.GetAsync(path + (path.Contains('?') ? "&" : "?") + "ext=mp4");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongToken_Returns401()
    {
        using var client = _factory.CreateAuthenticatedClient(token: "not-the-real-token");
        var response = await client.GetAsync("/v1/status");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CorrectToken_ValidOrigin_Returns200()
    {
        var token = _factory.ReadGeneratedPairingToken();
        using var client = _factory.CreateAuthenticatedClient(token: token);
        var response = await client.GetAsync("/v1/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Origin allowlist enforced on every request, not just preflight ──────

    [Theory]
    [InlineData("/v1/health")]
    [InlineData("/v1/status")]
    public async Task DisallowedOrigin_Returns403(string path)
    {
        var token = _factory.ReadGeneratedPairingToken();
        using var client = _factory.CreateAuthenticatedClient(origin: "http://evil.example", token: token);
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MissingOrigin_NonHealthEndpoint_Returns403()
    {
        var token = _factory.ReadGeneratedPairingToken();
        using var client = _factory.CreateAuthenticatedClient(origin: null, token: token);
        var response = await client.GetAsync("/v1/status");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Host header validation (DNS rebinding defense) ──────────────────────

    [Fact]
    public async Task DisallowedHostHeader_Returns403()
    {
        var token = _factory.ReadGeneratedPairingToken();
        using var client = _factory.CreateAuthenticatedClient(token: token);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/status");
        request.Headers.Host = "evil.example";

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("localhost")]
    public async Task AllowedHostHeader_IsAccepted(string host)
    {
        var token = _factory.ReadGeneratedPairingToken();
        using var client = _factory.CreateAuthenticatedClient(token: token);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/status");
        request.Headers.Host = host;

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── CORS preflight, including Private Network Access ────────────────────

    [Fact]
    public async Task Preflight_ValidOrigin_ReturnsPnaHeaderAndAllowedMethods()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/sources/11111111-2222-3333-4444-555555555555");
        request.Headers.Add("Origin", SidecarWebApplicationFactory.DefaultOrigin);
        request.Headers.Add("Access-Control-Request-Method", "PUT");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.Contains("Access-Control-Allow-Private-Network"));
        Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Private-Network").Single());
        Assert.Contains(SidecarProtocol.TokenHeaderName, response.Headers.GetValues("Access-Control-Allow-Headers").Single());
    }

    [Fact]
    public async Task Preflight_DisallowedOrigin_Returns403()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/v1/sources/11111111-2222-3333-4444-555555555555");
        request.Headers.Add("Origin", "http://evil.example");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Auth failure throttling ──────────────────────────────────────────────

    [Fact]
    public async Task RepeatedAuthFailures_EventuallyThrottled()
    {
        // Deliberately its own factory, not the class-shared _factory — this test intentionally
        // exhausts the throttle, which is a real, process-lifetime counter (correct for the real
        // single-user sidecar) and would otherwise poison every other test sharing this class's
        // fixture with spurious 429s regardless of xunit's run order.
        await using var factory = new SidecarWebApplicationFactory();
        using var client = factory.CreateAuthenticatedClient(token: "wrong");

        HttpStatusCode? lastStatus = null;
        for (var i = 0; i < 15; i++)
        {
            var response = await client.GetAsync("/v1/status");
            lastStatus = response.StatusCode;
            if (lastStatus == HttpStatusCode.TooManyRequests) break;
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, lastStatus);
    }

    // ── Common security response headers ─────────────────────────────────────

    [Fact]
    public async Task EveryResponse_HasNoStoreAndNoSniffHeaders()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/v1/health");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("nosniff", response.Content.Headers.Contains("X-Content-Type-Options")
            ? response.Content.Headers.GetValues("X-Content-Type-Options")
            : response.Headers.GetValues("X-Content-Type-Options"));
    }
}
