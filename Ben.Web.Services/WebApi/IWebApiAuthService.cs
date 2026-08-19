namespace Ben.Web.Services.WebApi;

public interface IWebApiAuthService
{
    Task<bool> LoginAsync(string email, string password, CancellationToken token = default);

    /// <summary>
    /// Why the last <see cref="LoginAsync"/> returned false, or null when it succeeded. Lets a
    /// sign-in page say what actually went wrong instead of assuming the password was wrong.
    /// </summary>
    LoginFailure? LastLoginFailure { get; }
    Task<bool> RefreshIfNeededAsync(CancellationToken token = default);
    void Logout();

    Task<bool> ImpersonateAsync(Guid targetUserId, string targetUserEmail, CancellationToken token = default);
    Task StopImpersonatingAsync(CancellationToken token = default);
}

/// <summary>What stopped the last sign-in.</summary>
public enum LoginFailure
{
    /// <summary>The server rejected the email or password.</summary>
    InvalidCredentials,

    /// <summary>Too many attempts in the window; the caller should wait, not re-type.</summary>
    RateLimited,
}
