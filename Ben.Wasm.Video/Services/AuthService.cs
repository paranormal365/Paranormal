using System.Net.Http.Json;

namespace Ben.Wasm.Video.Services;

/// <summary>
/// Signs in against the WebApi's Identity endpoints and keeps the <see cref="TokenStore"/> current.
/// </summary>
/// <remarks>
/// Talks to the same <c>/login</c> and <c>/refresh</c> the Blazor Server site uses — no new server
/// surface exists for this host. Uses a plain unnamed HttpClient on purpose: the bearer handler
/// must NOT be attached here, or an expired token would be sent to the very endpoint that exists
/// to replace it.
/// </remarks>
public sealed class AuthService
{
    private readonly HttpClient _http;
    private readonly TokenStore _tokens;

    public AuthService(HttpClient http, TokenStore tokens)
    {
        _http = http;
        _tokens = tokens;
    }

    /// <summary>Attempts sign-in. Returns null on success, or a human-readable failure reason.</summary>
    public async Task<string?> LoginAsync(string email, string password, CancellationToken ct = default)
        => (await SignInAsync(email, password, null, ct)).Problem;

    /// <summary>
    /// Attempts sign-in, saying whether a second factor is what is missing.
    /// </summary>
    /// <param name="twoFactorCode">
    /// The authenticator code, on the second attempt. A recovery code works here too — Identity
    /// accepts either against the same field.
    /// </param>
    /// <remarks>
    /// Identity answers a 2FA account with a 401 whose problem-detail is the literal string
    /// <c>RequiresTwoFactor</c> — the same status it uses for a wrong password. Mapping every 401
    /// to "check the address and password" meant anyone with two-factor turned on could not sign
    /// in here at all, and was told their password was wrong while they typed it correctly
    /// (2026-09-05 audit, F11).
    /// </remarks>
    public async Task<SignInResult> SignInAsync(
        string email, string password, string? twoFactorCode = null, CancellationToken ct = default)
    {
        HttpResponseMessage response;
        try
        {
            object body = string.IsNullOrWhiteSpace(twoFactorCode)
                ? new { email, password }
                : new { email, password, twoFactorCode = twoFactorCode.Trim() };

            response = await _http.PostAsJsonAsync("/login", body, ct);
        }
        catch (HttpRequestException)
        {
            return SignInResult.Failed("Could not reach the server. Is it running?");
        }

        if (!response.IsSuccessStatusCode)
        {
            if (await IsTwoFactorRequiredAsync(response, ct))
                return SignInResult.NeedsCode();

            // A 404 or a 5xx is the server being unreachable at that address, not a wrong
            // password — telling somebody their password is wrong sends them to reset one that
            // was right.
            var code = (int)response.StatusCode;
            if (code is 404 or >= 500)
                return SignInResult.Failed(
                    "The server did not answer at that address. Check the editor's configured API URL.");

            // Identity gives 401 for bad credentials AND for an unconfirmed email — deliberately
            // indistinguishable to the caller, so don't pretend to know which.
            return SignInResult.Failed(response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                ? "Too many attempts. Wait a minute and try again."
                : "Sign-in failed. Check the address and password — and note that new accounts must confirm their email first.");
        }

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        if (tokens?.AccessToken is null)
            return SignInResult.Failed("The server's response was not understood.");

        await _tokens.SetAsync(tokens.AccessToken, tokens.RefreshToken ?? "", tokens.ExpiresIn);
        return SignInResult.Ok();
    }

    private static async Task<bool> IsTwoFactorRequiredAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized) return false;

        try
        {
            var problem = await response.Content
                .ReadFromJsonAsync<System.Text.Json.JsonElement>(cancellationToken: ct);

            return problem.TryGetProperty("detail", out var detail)
                && detail.GetString() == "RequiresTwoFactor";
        }
        catch
        {
            // No body, or not JSON: an ordinary failed sign-in.
            return false;
        }
    }

    /// <summary>What happened when somebody tried to sign in.</summary>
    /// <param name="Problem">Null when it worked, otherwise something to show them.</param>
    /// <param name="RequiresTwoFactor">
    /// The password was right and a code is needed. Distinct from a failure: the form asks for the
    /// code rather than telling somebody their password was wrong.
    /// </param>
    public sealed record SignInResult(string? Problem, bool RequiresTwoFactor)
    {
        public static SignInResult Ok()               => new(null, false);
        public static SignInResult NeedsCode()        => new(null, true);
        public static SignInResult Failed(string why) => new(why, false);
    }

    /// <summary>Exchanges the refresh token for a new access token. False when re-login is needed.</summary>
    public async Task<bool> TryRefreshAsync(CancellationToken ct = default)
    {
        var refreshToken = await _tokens.GetRefreshTokenAsync();
        if (string.IsNullOrEmpty(refreshToken)) return false;

        try
        {
            var response = await _http.PostAsJsonAsync("/refresh", new { refreshToken }, ct);
            if (!response.IsSuccessStatusCode) return false;

            var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
            if (tokens?.AccessToken is null) return false;

            await _tokens.SetAsync(tokens.AccessToken, tokens.RefreshToken ?? refreshToken, tokens.ExpiresIn);
            return true;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public Task LogoutAsync() => _tokens.ClearAsync();

    /// <summary>Shape of MapIdentityApi's token responses (camelCase JSON).</summary>
    private sealed record TokenResponse(string? AccessToken, string? RefreshToken, int ExpiresIn);
}
