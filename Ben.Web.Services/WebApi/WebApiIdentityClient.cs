using System.Net.Http.Json;

namespace Ben.Web.Services.WebApi;

public sealed class WebApiIdentityClient : IWebApiIdentityClient
{
    private readonly HttpClient _httpClient;

    public WebApiIdentityClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<WebApiTokenResponse?> LoginAsync(string email, string password, CancellationToken token = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/login",
            new WebApiLoginRequest(email, password),
            token);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<WebApiTokenResponse>(cancellationToken: token);
    }

    public async Task<WebApiTokenResponse?> RefreshAsync(string refreshToken, CancellationToken token = default)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            "/refresh",
            new WebApiRefreshRequest(refreshToken),
            token);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<WebApiTokenResponse>(cancellationToken: token);
    }
}
