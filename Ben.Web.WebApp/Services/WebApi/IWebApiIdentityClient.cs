namespace Ben.Web.WebApp.Services.WebApi;

public interface IWebApiIdentityClient
{
    Task<WebApiTokenResponse?> LoginAsync(string email, string password, CancellationToken token = default);
    Task<WebApiTokenResponse?> RefreshAsync(string refreshToken, CancellationToken token = default);
}
