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
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("/login", new { email, password }, ct);
        }
        catch (HttpRequestException)
        {
            return "Could not reach the server. Is the WebApi running?";
        }

        if (!response.IsSuccessStatusCode)
        {
            // Identity's /login gives 401 for bad credentials AND for an unconfirmed email —
            // deliberately indistinguishable to the caller, so don't pretend to know which.
            return response.StatusCode == System.Net.HttpStatusCode.TooManyRequests
                ? "Too many attempts. Wait a minute and try again."
                : "Sign-in failed. Check the address and password — and note that new accounts must confirm their email first.";
        }

        var tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: ct);
        if (tokens?.AccessToken is null) return "The server's response was not understood.";

        await _tokens.SetAsync(tokens.AccessToken, tokens.RefreshToken ?? "", tokens.ExpiresIn);
        return null;
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
