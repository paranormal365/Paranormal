using System.Net.Http.Json;
using Ben.Canvas.Editor.Extensions;

namespace Ben.Wasm.Canvas.Services;

// Copied from Ben.Wasm.Video/Services/AccountInfoService.cs. Differences: the persistence client name,
// and it remembers the email /api/me returned, which is what the editor shows as the signed-in name.

/// <summary>
/// Answers who is signed in and what they may see, by asking the Web API.
/// </summary>
/// <remarks>
/// <para>This host has no claims to read: sign-in returns opaque tokens and nothing else, so
/// <c>GET /api/me</c> is the only way to learn the account's email or whether it administers
/// anything.</para>
///
/// <para><b>Not a security boundary.</b> It decides what is drawn. Every endpoint authorises itself.
/// Every failure resolves to "not an administrator" and "no email".</para>
/// </remarks>
public sealed class AccountInfoService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenStore _tokens;
    private readonly string? _apiBaseUrl;

    public AccountInfoService(IHttpClientFactory httpClientFactory, TokenStore tokens, string? apiBaseUrl)
    {
        _httpClientFactory = httpClientFactory;
        _tokens = tokens;
        _apiBaseUrl = apiBaseUrl?.TrimEnd('/');
    }

    /// <summary>The signed-in account's email from the last successful /api/me read, or null.</summary>
    public string? Email { get; private set; }

    /// <summary>True when the signed-in account administers the platform or a group.</summary>
    public async Task<bool> IsAdministratorAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiBaseUrl)) { Email = null; return false; }
        if (await _tokens.GetAccessTokenAsync() is null) { Email = null; return false; }

        try
        {
            var http = _httpClientFactory.CreateClient(CanvasEditorServiceCollectionExtensions.PersistenceHttpClientName);

            var me = await http.GetFromJsonAsync<MeResponse>($"{_apiBaseUrl}/api/me", ct);
            Email = me?.Email;
            return me is { IsSuperAdmin: true } or { IsAdmin: true };
        }
        catch
        {
            Email = null;
            return false;
        }
    }

    /// <summary>The parts of the Web API's <c>/api/me</c> response this host uses.</summary>
    private sealed record MeResponse(string? Email, bool IsSuperAdmin, bool IsAdmin);
}
