namespace Ben.Web.Services.WebApi;

public interface IWebApiIdentityClient
{
    Task<WebApiTokenResponse?> LoginAsync(string email, string password, CancellationToken token = default);

    /// <summary>
    /// Signs in and reports the HTTP status, so a caller can tell a rejected password from a
    /// refused request. Flattening both to null meant a rate-limited sign-in was reported to the
    /// user as "Invalid email or password", which is wrong and sends them to reset a password
    /// that was never the problem.
    /// </summary>
    Task<LoginAttempt> TryLoginAsync(string email, string password, CancellationToken token = default);
    Task<WebApiTokenResponse?> RefreshAsync(string refreshToken, CancellationToken token = default);
}

/// <summary>The outcome of one sign-in request: the token when it worked, and the status either way.</summary>
public readonly record struct LoginAttempt(WebApiTokenResponse? Token, int StatusCode)
{
    /// <summary>The server refused the request rather than the credentials.</summary>
    public bool WasRateLimited => StatusCode == 429;
}
