using System.Net.Http.Headers;

namespace Ben.Web.WebApp.Services.WebApi;

public sealed class WebApiBearerTokenHandler : DelegatingHandler
{
    private readonly IWebApiIdentityClient _identityClient;
    private readonly IWebApiTokenStore _tokenStore;

    public WebApiBearerTokenHandler(IWebApiIdentityClient identityClient, IWebApiTokenStore tokenStore)
    {
        _identityClient = identityClient;
        _tokenStore = tokenStore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await RefreshIfNeededAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(_tokenStore.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tokenStore.AccessToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }

    private async Task RefreshIfNeededAsync(CancellationToken token)
    {
        if (_tokenStore.AccessTokenExpiresAtUtc is { } expiry && expiry > DateTimeOffset.UtcNow)
            return;

        if (string.IsNullOrWhiteSpace(_tokenStore.RefreshToken))
            return;

        var response = await _identityClient.RefreshAsync(_tokenStore.RefreshToken, token);
        if (response is null || string.IsNullOrWhiteSpace(response.AccessToken))
            return;

        _tokenStore.AccessToken = response.AccessToken;
        _tokenStore.RefreshToken = response.RefreshToken;
        _tokenStore.AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(0, response.ExpiresIn - 30));

        var (userId, isSuperAdmin) = JwtClaimsParser.ParseClaims(response.AccessToken);
        _tokenStore.UserId = userId;
        _tokenStore.IsSuperAdmin = isSuperAdmin;
        _tokenStore.NotifyStateChanged();
    }
}
