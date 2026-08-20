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
    /// <param name="twoFactorCode">A code from the authenticator app, when one is being offered.</param>
    /// <param name="recoveryCode">One of the printed recovery codes, instead of an app code.</param>
    Task<LoginAttempt> TryLoginAsync(
        string email, string password,
        string? twoFactorCode = null, string? recoveryCode = null,
        CancellationToken token = default);
    Task<WebApiTokenResponse?> RefreshAsync(string refreshToken, CancellationToken token = default);
}

/// <summary>The outcome of one sign-in request: the token when it worked, and the status either way.</summary>
public readonly record struct LoginAttempt(WebApiTokenResponse? Token, int StatusCode, string? Detail = null)
{
    /// <summary>The server refused the request rather than the credentials.</summary>
    public bool WasRateLimited => StatusCode == 429;

    /// <summary>
    /// The password was right and a second factor is needed.
    /// </summary>
    /// <remarks>
    /// Identity answers this case with a 401 whose problem-detail is the literal string
    /// <c>RequiresTwoFactor</c> — the same status it uses for a wrong password. Without reading the
    /// detail, a sign-in that merely needs a code is indistinguishable from one that failed, and
    /// somebody with 2FA on is told their password is wrong.
    /// </remarks>
    public bool RequiresTwoFactor =>
        StatusCode == 401 && string.Equals(Detail, "RequiresTwoFactor", StringComparison.Ordinal);
}
