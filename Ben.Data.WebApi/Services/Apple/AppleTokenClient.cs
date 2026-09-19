using System.Text.Json;

namespace Ben.Data.WebApi.Services.Apple;

/// <summary>What an authorization code bought: the refresh token to keep, or why not.</summary>
/// <param name="RefreshToken">Apple's long-lived token for this person and client. Revocable later.</param>
/// <param name="Subject">Apple's stable identifier for the person, from the identity token in the answer.</param>
/// <param name="Error">Apple's error word (<c>invalid_grant</c>, <c>invalid_client</c>…) or ours, when nothing was bought.</param>
public sealed record AppleTokenExchange(string? RefreshToken, string? Subject, string? Error)
{
    public bool Succeeded => RefreshToken is not null;
}

/// <summary>Apple's token endpoints, from this server's point of view.</summary>
public interface IAppleTokenClient
{
    /// <summary>Whether a client secret can be signed at all. False means nothing is ever sent.</summary>
    bool IsConfigured { get; }

    /// <summary>Exchanges the authorization code a sign-in produced for a refresh token. Never throws.</summary>
    Task<AppleTokenExchange> ExchangeCodeAsync(string code, string clientId, CancellationToken ct);

    /// <summary>Tells Apple the refresh token is finished with. True only when Apple said so. Never throws.</summary>
    Task<bool> RevokeAsync(string refreshToken, string clientId, CancellationToken ct);
}

/// <summary>
/// <c>POST /auth/token</c> and <c>POST /auth/revoke</c> at <c>appleid.apple.com</c>, each with a
/// freshly signed client secret.
/// </summary>
/// <remarks>
/// Neither call may fail a sign-in or a deletion, so neither throws: the exchange returns
/// Apple's error word for the log, the revoke returns false. Apple answers <c>invalid_grant</c>
/// for a code that is spent, expired or minted for another client, and <c>invalid_client</c>
/// for a secret it does not accept — the second is a configuration mistake and is logged as one.
/// </remarks>
public sealed class AppleTokenClient : IAppleTokenClient
{
    public const string TokenEndpoint  = "https://appleid.apple.com/auth/token";
    public const string RevokeEndpoint = "https://appleid.apple.com/auth/revoke";

    private readonly HttpClient _http;
    private readonly AppleClientSecret _secret;
    private readonly ILogger<AppleTokenClient> _log;
    private readonly TimeProvider _clock;

    public AppleTokenClient(HttpClient http, AppleClientSecret secret, ILogger<AppleTokenClient> log, TimeProvider? clock = null)
    {
        _http   = http;
        _secret = secret;
        _log    = log;
        _clock  = clock ?? TimeProvider.System;
    }

    public bool IsConfigured => _secret.IsConfigured;

    public async Task<AppleTokenExchange> ExchangeCodeAsync(string code, string clientId, CancellationToken ct)
    {
        if (!_secret.IsConfigured) return new AppleTokenExchange(null, null, "not_configured");
        if (string.IsNullOrWhiteSpace(code)) return new AppleTokenExchange(null, null, "no_code");

        try
        {
            using var response = await _http.PostAsync(TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"]    = "authorization_code",
                ["code"]          = code,
                ["client_id"]     = clientId,
                ["client_secret"] = _secret.Issue(clientId, _clock.GetUtcNow()),
            }), ct);

            var body = await response.Content.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            var root = json.RootElement;

            if (!response.IsSuccessStatusCode)
            {
                var error = root.TryGetProperty("error", out var e) ? e.GetString() : $"http_{(int)response.StatusCode}";
                if (error == "invalid_client")
                    _log.LogError("Apple refused our client secret for {ClientId}: check Apple:TeamId, Apple:KeyId and the key file.", clientId);
                else
                    _log.LogWarning("Apple would not exchange an authorization code for {ClientId}: {Error}.", clientId, error);
                return new AppleTokenExchange(null, null, error);
            }

            var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
            var subject = root.TryGetProperty("id_token", out var idToken) ? SubjectOf(idToken.GetString()) : null;
            return refresh is null
                ? new AppleTokenExchange(null, null, "no_refresh_token")
                : new AppleTokenExchange(refresh, subject, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Apple's token endpoint could not be reached for {ClientId}.", clientId);
            return new AppleTokenExchange(null, null, "unreachable");
        }
    }

    public async Task<bool> RevokeAsync(string refreshToken, string clientId, CancellationToken ct)
    {
        if (!_secret.IsConfigured || string.IsNullOrWhiteSpace(refreshToken)) return false;
        try
        {
            using var response = await _http.PostAsync(RevokeEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["token"]           = refreshToken,
                ["token_type_hint"] = "refresh_token",
                ["client_id"]       = clientId,
                ["client_secret"]   = _secret.Issue(clientId, _clock.GetUtcNow()),
            }), ct);

            if (response.IsSuccessStatusCode) return true;

            var body = await response.Content.ReadAsStringAsync(ct);
            _log.LogError("Apple refused to revoke a token for {ClientId}: {Status} {Body}.",
                clientId, (int)response.StatusCode, body.Length > 200 ? body[..200] : body);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogError(ex, "Apple's revoke endpoint could not be reached for {ClientId}.", clientId);
            return false;
        }
    }

    /// <summary>The <c>sub</c> of an identity token, read without verifying — the exchange itself is the proof.</summary>
    internal static string? SubjectOf(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken)) return null;
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("sub", out var sub) ? sub.GetString() : null;
        }
        catch { return null; }
    }
}
