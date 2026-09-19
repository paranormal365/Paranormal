using System.Net.Http.Json;

namespace Ben.Wasm.Video.Services;

/// <summary>
/// Answers what the signed-in account is allowed to see, by asking the WebApi.
/// </summary>
/// <remarks>
/// <para>This host has no claims to read. Sign-in here goes through <c>MapIdentityApi</c>, which
/// returns tokens and nothing else — there is no <c>AuthenticationStateProvider</c> and no
/// principal, so a page cannot ask "is this person an administrator" the way the Blazor Server
/// site can. <c>GET /api/me</c> already answers exactly that and needed no new server surface.</para>
///
/// <para><b>This is not a security boundary and must not be treated as one.</b> It decides whether
/// the editor's diagnostics panel is drawn, which is a matter of showing operator tools to
/// operators. Every endpoint those tools reach is authorised on the server independently. A person
/// who tampered with the answer here would reveal a panel to themselves and gain no access.</para>
///
/// <para>Every failure — signed out, no API configured, the call throwing — resolves to
/// <b>not</b> an administrator. The safe answer is the restrictive one, and a host that cannot
/// determine the answer should behave like a host that determined "no".</para>
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

    /// <summary>
    /// True when the signed-in account administers the platform or a group.
    /// </summary>
    public async Task<bool> IsAdministratorAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_apiBaseUrl)) return false;
        if (await _tokens.GetAccessTokenAsync() is null) return false;

        try
        {
            var http = _httpClientFactory.CreateClient(
                Ben.Video.Editor.Extensions.ServiceCollectionExtensions.MediaLibraryHttpClientName);

            var me = await http.GetFromJsonAsync<MeResponse>($"{_apiBaseUrl}/api/me", ct);
            return me is { IsSuperAdmin: true } or { IsAdmin: true };
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The parts of the WebApi's <c>/api/me</c> response this host uses.</summary>
    private sealed record MeResponse(bool IsSuperAdmin, bool IsAdmin);
}
