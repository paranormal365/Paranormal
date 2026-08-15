namespace Ben.Web.WebApp.Services.WebApi;

public sealed class WebApiAuthService : IWebApiAuthService
{
    private readonly IWebApiIdentityClient _identityClient;
    private readonly IWebApiClient _apiClient;
    private readonly IWebApiTokenStore _tokenStore;

    public WebApiAuthService(IWebApiIdentityClient identityClient, IWebApiClient apiClient, IWebApiTokenStore tokenStore)
    {
        _identityClient = identityClient;
        _apiClient = apiClient;
        _tokenStore = tokenStore;
    }

    public async Task<bool> LoginAsync(string email, string password, CancellationToken token = default)
    {
        var response = await _identityClient.LoginAsync(email, password, token);
        if (response is null || string.IsNullOrWhiteSpace(response.AccessToken))
            return false;

        ApplyTokenResponse(response);
        _tokenStore.UserEmail = email;
        _tokenStore.IsEntraSession = false; // local login supersedes any prior Entra session

        // The Identity API issues opaque data-protected tokens, not JWTs.
        // JwtClaimsParser cannot extract claims from them, so we call /api/me
        // (which runs server-side with the bearer token) to get role information.
        try
        {
            var me = await _apiClient.GetAsync<MeResult>("/api/me", token);
            if (me is not null)
            {
                _tokenStore.IsSuperAdmin = me.IsSuperAdmin;
                _tokenStore.IsAdmin = me.IsAdmin;
                _tokenStore.UserId = me.UserId;
            }
        }
        catch { /* non-fatal — IsSuperAdmin stays false */ }

        _tokenStore.NotifyStateChanged();
        return true;
    }

    public async Task<bool> RefreshIfNeededAsync(CancellationToken token = default)
    {
        if (_tokenStore.AccessTokenExpiresAtUtc is { } expiry && expiry > DateTimeOffset.UtcNow)
            return true;

        if (string.IsNullOrWhiteSpace(_tokenStore.RefreshToken))
            return false;

        var response = await _identityClient.RefreshAsync(_tokenStore.RefreshToken, token);
        if (response is null || string.IsNullOrWhiteSpace(response.AccessToken))
            return false;

        ApplyTokenResponse(response);
        _tokenStore.NotifyStateChanged();
        return true;
    }

    public void Logout()
    {
        _tokenStore.AccessToken = null;
        _tokenStore.RefreshToken = null;
        _tokenStore.AccessTokenExpiresAtUtc = null;
        _tokenStore.UserEmail = null;
        _tokenStore.UserDisplayName = null;
        _tokenStore.UserId = null;
        _tokenStore.IsSuperAdmin = false;
        _tokenStore.IsAdmin = false;
        _tokenStore.IsImpersonating = false;
        _tokenStore.OriginalAccessToken = null;
        _tokenStore.OriginalRefreshToken = null;
        _tokenStore.OriginalUserId = null;
        _tokenStore.OriginalUserEmail = null;
        _tokenStore.NotifyStateChanged();
    }

    public async Task<bool> ImpersonateAsync(Guid targetUserId, string targetUserEmail, CancellationToken token = default)
    {
        var response = await _apiClient.ImpersonateAsync(targetUserId, token);
        if (response is null || string.IsNullOrWhiteSpace(response.AccessToken))
            return false;

        // Save current (SuperAdmin) session
        _tokenStore.OriginalAccessToken = _tokenStore.AccessToken;
        _tokenStore.OriginalRefreshToken = _tokenStore.RefreshToken;
        _tokenStore.OriginalUserId = _tokenStore.UserId;
        _tokenStore.OriginalUserEmail = _tokenStore.UserEmail;

        // Apply impersonated user's token
        ApplyTokenResponse(response);
        _tokenStore.UserEmail = targetUserEmail;
        _tokenStore.IsImpersonating = true;
        _tokenStore.NotifyStateChanged();
        return true;
    }

    public async Task StopImpersonatingAsync(CancellationToken token = default)
    {
        if (!_tokenStore.IsImpersonating) return;

        _tokenStore.AccessToken = _tokenStore.OriginalAccessToken;
        _tokenStore.RefreshToken = _tokenStore.OriginalRefreshToken;
        _tokenStore.UserEmail = _tokenStore.OriginalUserEmail;
        _tokenStore.UserId = _tokenStore.OriginalUserId;
        _tokenStore.IsSuperAdmin = false;
        _tokenStore.IsAdmin = false;

        // Same reason as LoginAsync: the Identity API's opaque data-protected tokens
        // aren't JWTs, so JwtClaimsParser can't read IsSuperAdmin back out of the
        // restored original token — it silently returns false, which used to leave a
        // SuperAdmin permanently stripped of Administration access after returning
        // from impersonation until they logged out and back in. Ask the server instead.
        if (_tokenStore.OriginalAccessToken is not null)
        {
            try
            {
                var me = await _apiClient.GetAsync<MeResult>("/api/me", token);
                if (me is not null)
                {
                    _tokenStore.IsSuperAdmin = me.IsSuperAdmin;
                    _tokenStore.IsAdmin = me.IsAdmin;
                    _tokenStore.UserId = me.UserId;
                }
            }
            catch { /* non-fatal — IsSuperAdmin stays false */ }
        }

        _tokenStore.IsImpersonating = false;
        _tokenStore.OriginalAccessToken = null;
        _tokenStore.OriginalRefreshToken = null;
        _tokenStore.OriginalUserId = null;
        _tokenStore.OriginalUserEmail = null;

        // Restore expiry from the re-applied token (unknown, set to now so refresh triggers)
        _tokenStore.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow;
        _tokenStore.NotifyStateChanged();
    }

    private void ApplyTokenResponse(WebApiTokenResponse response)
    {
        _tokenStore.AccessToken = response.AccessToken;
        _tokenStore.RefreshToken = response.RefreshToken;
        _tokenStore.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, response.ExpiresIn - 30));

        // Note: JwtClaimsParser cannot extract claims from opaque Identity API tokens.
        // UserId and IsSuperAdmin are set via /api/me after login instead.
        var (userId, isSuperAdmin, isAdmin) = JwtClaimsParser.ParseClaims(response.AccessToken);
        _tokenStore.UserId = userId;
        _tokenStore.IsSuperAdmin = isSuperAdmin;
        _tokenStore.IsAdmin = isAdmin;
    }
}

/// <summary>Matches the JSON shape of MeResponse in Ben.Data.WebApi.</summary>
internal sealed record MeResult(Guid UserId, string Email, bool IsSuperAdmin, bool IsAdmin);

