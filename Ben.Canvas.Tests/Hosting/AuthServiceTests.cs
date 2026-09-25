using System.Net;
using Ben.Canvas.Tests.Support;
using Ben.Wasm.Canvas.Services;

namespace Ben.Canvas.Tests.Hosting;

/// <summary>
/// Signing in to the standalone canvas editor.
/// </summary>
/// <remarks>
/// Copied from Ben.Wasm.Video.Tests/AuthServiceTests.cs, plus the facts that pin the /webapi base
/// path. The video editor's copy of this service posts "/login" with a leading slash against a base of
/// https://ishaunted.com/webapi, which resolves to the website's own /login - proven live on
/// 2026-09-14. Its tests never caught it because they used an origin-only base address.
/// </remarks>
public sealed class AuthServiceTests
{
    private static AuthService Create(HttpMessageHandler handler, string baseAddress = "https://example.test/", TokenStore? tokens = null) =>
        new(new HttpClient(handler) { BaseAddress = new Uri(baseAddress) },
            tokens ?? new TokenStore(new NoJs()));

    // ── The /webapi mount ─────────────────────────────────────────────────────

    [Fact]
    public async Task Sign_in_keeps_the_webapi_base_path()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"accessToken":"a","refreshToken":"r","expiresIn":3600}""");

        await Create(handler, "https://ishaunted.test/webapi/").SignInAsync("a@b.test", "pw");

        Assert.Equal("https://ishaunted.test/webapi/login", handler.LastUrl);
    }

    [Fact]
    public async Task Refresh_keeps_the_webapi_base_path()
    {
        var tokens = new TokenStore(new NoJs());
        await tokens.SetAsync("expired", "refresh-me", 0);
        var handler = new StubHandler(HttpStatusCode.OK, """{"accessToken":"a2","refreshToken":"r2","expiresIn":3600}""");

        await Create(handler, "https://ishaunted.test/webapi/", tokens).TryRefreshAsync();

        Assert.Equal("https://ishaunted.test/webapi/refresh", handler.LastUrl);
    }

    [Fact]
    public async Task Tokens_are_kept_under_the_canvas_key()
    {
        var js = new RecordingJs();
        var handler = new StubHandler(HttpStatusCode.OK, """{"accessToken":"a","refreshToken":"r","expiresIn":3600}""");

        await Create(handler, tokens: new TokenStore(js)).SignInAsync("a@b.test", "pw");

        var set = Assert.Single(js.Calls, c => c.Identifier == "sessionStorage.setItem");
        Assert.Equal("bwc-auth", set.Args?[0]);
    }

    // ── Two factor ────────────────────────────────────────────────────────────

    /// <summary>
    /// Identity answers a two-factor account with a 401 whose problem detail is the literal string
    /// <c>RequiresTwoFactor</c> - the same status it uses for a wrong password.
    /// </summary>
    [Fact]
    public async Task A_two_factor_account_is_asked_for_a_code_not_told_it_failed()
    {
        var service = Create(new StubHandler(HttpStatusCode.Unauthorized,
            """{"type":"...","title":"Unauthorized","status":401,"detail":"RequiresTwoFactor"}"""));

        var result = await service.SignInAsync("a@b.test", "correct horse");

        Assert.True(result.RequiresTwoFactor);
        Assert.Null(result.Problem);
    }

    [Fact]
    public async Task The_code_is_sent_on_the_second_attempt()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"accessToken":"a","refreshToken":"r","expiresIn":3600}""");

        await Create(handler).SignInAsync("a@b.test", "pw", "123456");

        Assert.Contains("twoFactorCode", handler.LastBody);
        Assert.Contains("123456", handler.LastBody);
    }

    [Fact]
    public async Task No_code_is_sent_on_the_first_attempt()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"accessToken":"a","refreshToken":"r","expiresIn":3600}""");

        await Create(handler).SignInAsync("a@b.test", "pw");

        Assert.DoesNotContain("twoFactorCode", handler.LastBody);
    }

    // ── Ordinary failures ─────────────────────────────────────────────────────

    [Fact]
    public async Task A_wrong_password_is_still_a_wrong_password()
    {
        var result = await Create(new StubHandler(HttpStatusCode.Unauthorized, "")).SignInAsync("a@b.test", "wrong");

        Assert.False(result.RequiresTwoFactor);
        Assert.Contains("Check the address and password", result.Problem);
    }

    [Fact]
    public async Task Being_rate_limited_says_to_wait_rather_than_blaming_the_password()
    {
        var result = await Create(new StubHandler(HttpStatusCode.TooManyRequests, "")).SignInAsync("a@b.test", "pw");

        Assert.Contains("Too many attempts", result.Problem);
    }

    /// <summary>
    /// Telling somebody their password is wrong sends them to reset one that was right. A 404 or a 5xx
    /// is the API not being where the editor thinks it is.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task An_unreachable_server_is_not_reported_as_a_bad_password(HttpStatusCode status)
    {
        var result = await Create(new StubHandler(status, "")).SignInAsync("a@b.test", "pw");

        Assert.Contains("did not answer", result.Problem);
        Assert.DoesNotContain("password", result.Problem);
    }

    [Fact]
    public async Task A_connection_that_never_lands_says_so()
    {
        var result = await Create(new ThrowingHandler()).SignInAsync("a@b.test", "pw");

        Assert.Contains("Could not reach the server", result.Problem);
    }

    [Fact]
    public async Task A_response_without_a_token_is_not_treated_as_a_sign_in()
    {
        var result = await Create(new StubHandler(HttpStatusCode.OK, """{"somethingElse":true}""")).SignInAsync("a@b.test", "pw");

        Assert.NotNull(result.Problem);
    }
}
