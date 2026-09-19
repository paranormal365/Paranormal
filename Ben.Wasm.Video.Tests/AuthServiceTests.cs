using System.Net;
using System.Text;
using Ben.Wasm.Video.Services;
using Microsoft.JSInterop;

namespace Ben.Wasm.Video.Tests;

/// <summary>
/// Signing in to the standalone editor.
/// </summary>
/// <remarks>
/// This host had no tests at all (2026-09-05 audit, wasm-16), and sign-in is where that mattered
/// most: every failure was mapped to "check the address and password", so an account with
/// two-factor turned on could not get in and was told its password was wrong while typing it
/// correctly, and an editor pointed at the wrong API address said the same thing.
/// </remarks>
public sealed class AuthServiceTests
{
    private static AuthService Create(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://example.test") },
            new TokenStore(new NoJs()));

    // ── Two factor ────────────────────────────────────────────────────────────

    /// <summary>
    /// Identity answers a two-factor account with a 401 whose problem-detail is the literal string
    /// <c>RequiresTwoFactor</c> — the same status it uses for a wrong password.
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
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"accessToken":"a","refreshToken":"r","expiresIn":3600}""");
        var service = Create(handler);

        await service.SignInAsync("a@b.test", "pw", "123456");

        Assert.Contains("twoFactorCode", handler.LastBody);
        Assert.Contains("123456", handler.LastBody);
    }

    [Fact]
    public async Task No_code_is_sent_on_the_first_attempt()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"accessToken":"a","refreshToken":"r","expiresIn":3600}""");
        var service = Create(handler);

        await service.SignInAsync("a@b.test", "pw");

        Assert.DoesNotContain("twoFactorCode", handler.LastBody);
    }

    // ── Ordinary failures ─────────────────────────────────────────────────────

    /// <summary>
    /// A 401 without that detail really is a failed sign-in, and must not turn into a code prompt.
    /// </summary>
    [Fact]
    public async Task A_wrong_password_is_still_a_wrong_password()
    {
        var service = Create(new StubHandler(HttpStatusCode.Unauthorized, ""));

        var result = await service.SignInAsync("a@b.test", "wrong");

        Assert.False(result.RequiresTwoFactor);
        Assert.Contains("Check the address and password", result.Problem);
    }

    [Fact]
    public async Task Being_rate_limited_says_to_wait_rather_than_blaming_the_password()
    {
        var service = Create(new StubHandler(HttpStatusCode.TooManyRequests, ""));

        var result = await service.SignInAsync("a@b.test", "pw");

        Assert.Contains("Too many attempts", result.Problem);
    }

    /// <summary>
    /// Telling somebody their password is wrong sends them to reset one that was right. A 404 or a
    /// 5xx is the API not being where the editor thinks it is.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task An_unreachable_server_is_not_reported_as_a_bad_password(HttpStatusCode status)
    {
        var service = Create(new StubHandler(status, ""));

        var result = await service.SignInAsync("a@b.test", "pw");

        Assert.Contains("did not answer", result.Problem);
        Assert.DoesNotContain("password", result.Problem);
    }

    [Fact]
    public async Task A_connection_that_never_lands_says_so()
    {
        var service = Create(new ThrowingHandler());

        var result = await service.SignInAsync("a@b.test", "pw");

        Assert.Contains("Could not reach the server", result.Problem);
    }

    [Fact]
    public async Task A_response_without_a_token_is_not_treated_as_a_sign_in()
    {
        var service = Create(new StubHandler(HttpStatusCode.OK, """{"somethingElse":true}"""));

        var result = await service.SignInAsync("a@b.test", "pw");

        Assert.NotNull(result.Problem);
    }

    // ── Support ───────────────────────────────────────────────────────────────

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content is not null) LastBody = await request.Content.ReadAsStringAsync(ct);

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("no route to host");
    }

    /// <summary>The token store touches JS only when it persists; these tests never get that far.</summary>
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => ValueTask.FromResult(default(TValue)!);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken ct, object?[]? args)
            => InvokeAsync<TValue>(identifier, args);
    }
}
