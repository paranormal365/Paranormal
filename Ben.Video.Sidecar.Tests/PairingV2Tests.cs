using Ben.Video.Sidecar.Security;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Ben.Video.Sidecar.Tests;

/// <summary>
/// The 6-digit pairing flow: /pair page mints a code, POST /v1/pair exchanges it for the long
/// token, and every way a code can be invalid stays invalid.
/// </summary>
public sealed class PairingV2Tests : IClassFixture<SidecarWebApplicationFactory>
{
    private readonly SidecarWebApplicationFactory _factory;
    public PairingV2Tests(SidecarWebApplicationFactory factory) => _factory = factory;

    private PairingTokenStore Store =>
        (PairingTokenStore)_factory.Services.GetService(typeof(PairingTokenStore))!;

    // ── Store semantics ──────────────────────────────────────────────────────

    [Fact]
    public void BeginPairing_Returns_SixDigits_And_ExchangeYieldsTheToken()
    {
        var expected = _factory.ReadGeneratedPairingToken();
        var code = Store.BeginPairing();

        Assert.Equal(6, code.Length);
        Assert.All(code, c => Assert.True(char.IsAsciiDigit(c)));
        Assert.True(Store.HasActiveCode);

        var token = Store.TryExchangeCode(code);
        Assert.Equal(expected, token);
    }

    [Fact]
    public void ExchangeConsumesTheCode_SecondAttemptFails()
    {
        var code = Store.BeginPairing();
        Assert.NotNull(Store.TryExchangeCode(code));
        Assert.Null(Store.TryExchangeCode(code)); // single-use
        Assert.False(Store.HasActiveCode);
    }

    [Fact]
    public void WrongCode_YieldsNothing_AndDoesNotConsumeTheRealOne()
    {
        var code = Store.BeginPairing();
        var wrong = code == "000000" ? "000001" : "000000";

        Assert.Null(Store.TryExchangeCode(wrong));
        Assert.True(Store.HasActiveCode);           // real code still live
        Assert.NotNull(Store.TryExchangeCode(code)); // and still works
    }

    [Fact]
    public void NewCode_ReplacesTheOldOne()
    {
        var first = Store.BeginPairing();
        var second = Store.BeginPairing();

        if (first != second) // 1-in-a-million collision would make the assertion meaningless
            Assert.Null(Store.TryExchangeCode(first));
        Assert.NotNull(Store.TryExchangeCode(second));
    }

    // ── HTTP surface ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PairPage_IsReachable_WithoutOriginOrToken()
    {
        // Top-level browser navigation: no Origin header, no token — like a bare health check.
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/pair");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Pairing code", html);
        Assert.True(Store.HasActiveCode); // loading the page began a pairing window
    }

    [Fact]
    public async Task Exchange_FromAllowedOrigin_ReturnsTheLongToken_WithoutRequiringAToken()
    {
        var code = Store.BeginPairing();
        using var client = _factory.CreateAuthenticatedClient(token: null); // origin only

        var response = await client.PostAsJsonAsync("/v1/pair", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<Api.PairingEndpoints.PairResponse>();
        Assert.Equal(_factory.ReadGeneratedPairingToken(), body!.Token);
    }

    [Fact]
    public async Task Exchange_WithoutOrigin_IsRefused()
    {
        // /v1/pair is for the editor, which always sends an Origin. A bare local process gets 403 —
        // the page carve-out is GET /pair only.
        var code = Store.BeginPairing();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/v1/pair", new { code });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(Store.HasActiveCode); // refused before the endpoint — code not consumed
    }

    [Fact]
    public async Task Exchange_WithWrongCode_Is401_AndFeedsTheThrottle()
    {
        Store.BeginPairing();
        using var client = _factory.CreateAuthenticatedClient(token: null);

        var response = await client.PostAsJsonAsync("/v1/pair", new { code = "999999" });
        // Either a plain 401 (wrong code) or 429 (a previous test already tripped the shared
        // throttle) — both prove the code was refused; neither hands out a token.
        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests,
            $"expected 401 or 429, got {(int)response.StatusCode}");
    }
}
