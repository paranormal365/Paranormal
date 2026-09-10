using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Ben.Data.WebApi.Client.Auth;

namespace Ben.Data.WebApi.Client.External;

/// <summary>
/// Trades an authorization code for a Microsoft access token, and renews it later.
/// </summary>
/// <remarks>
/// Talks to Entra, not to Ben.Data.WebApi. The token it returns is then presented to our own API,
/// which validates it under its "Entra" scheme — so this client never mints anything our server
/// trusts on its own say-so.
/// </remarks>
public sealed class EntraTokenClient
{
    private readonly HttpClient _http;
    private readonly EntraOptions _options;

    public EntraTokenClient(HttpClient http, EntraOptions options)
    {
        _http = http;
        _options = options;
    }

    /// <summary>The result of asking Entra for a token.</summary>
    /// <param name="Reason">A sentence to show, when it failed.</param>
    public readonly record struct Result(StoredTokens? Tokens, string? Reason)
    {
        public bool Succeeded => Tokens is not null;

        public static Result Ok(StoredTokens tokens) => new(tokens, null);
        public static Result Failed(string reason) => new(null, reason);
    }

    /// <summary>Redeems the code the browser handed back.</summary>
    public Task<Result> RedeemCodeAsync(string code, EntraPkce pkce, CancellationToken token = default) =>
        PostAsync(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri,
            ["scope"] = $"{_options.Scope} offline_access openid profile email",

            // The proof. Entra hashes this and compares it with the challenge it was given when
            // the browser opened; a code stolen from the redirect cannot be redeemed without it.
            ["code_verifier"] = pkce.CodeVerifier,
        }, token);

    /// <summary>Renews an expired access token without sending anybody back through a browser.</summary>
    public Task<Result> RefreshAsync(string refreshToken, CancellationToken token = default) =>
        PostAsync(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = $"{_options.Scope} offline_access openid profile email",
        }, token);

    private async Task<Result> PostAsync(Dictionary<string, string> form, CancellationToken token)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsync(_options.TokenEndpoint, new FormUrlEncodedContent(form), token);
        }
        catch (HttpRequestException)
        {
            return Result.Failed("Couldn't reach Microsoft to finish signing in.");
        }

        using (response)
        {
            // Entra answers a refusal with a JSON body carrying error and error_description, and
            // the description is written for a developer but is still the only specific thing
            // available — "invalid_grant" alone cannot be told apart from a dozen causes.
            if (!response.IsSuccessStatusCode)
            {
                var problem = await ReadErrorAsync(response, token);
                return Result.Failed(problem ?? $"Microsoft refused the sign-in ({(int)response.StatusCode}).");
            }

            EntraTokenResponse? body;
            try
            {
                body = await response.Content.ReadFromJsonAsync<EntraTokenResponse>(cancellationToken: token);
            }
            catch (System.Text.Json.JsonException)
            {
                return Result.Failed("Microsoft's answer couldn't be read.");
            }

            if (body is null || string.IsNullOrWhiteSpace(body.AccessToken))
                return Result.Failed("Microsoft answered without an access token.");

            // Same 30-second slack as every other session here, so a token about to expire is
            // renewed rather than sent and refused.
            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(body.ExpiresIn) - StoredTokens.ExpirySlack;
            return Result.Ok(new StoredTokens(body.AccessToken, body.RefreshToken, expiresAt));
        }
    }

    private static async Task<string?> ReadErrorAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<EntraErrorResponse>(cancellationToken: token);
            if (problem is null) return null;

            return string.IsNullOrWhiteSpace(problem.ErrorDescription)
                ? problem.Error
                : problem.ErrorDescription;
        }
        catch
        {
            return null;
        }
    }

    private sealed record EntraTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("token_type")] string? TokenType);

    private sealed record EntraErrorResponse(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? ErrorDescription);
}
