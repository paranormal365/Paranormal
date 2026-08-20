namespace Ben.Web.Services.WebApi;

public interface IWebApiAuthService
{
    /// <summary>
    /// Signs in. Two-factor authentication is <b>opt-in per account</b>: leave both code
    /// parameters null for the ordinary case, and supply one only after a previous attempt
    /// reported <see cref="LoginFailure.RequiresTwoFactor"/>.
    /// </summary>
    Task<bool> LoginAsync(
        string email, string password,
        string? twoFactorCode = null, string? recoveryCode = null,
        CancellationToken token = default);

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

    /// <summary>
    /// The password was right; this account has two-factor authentication switched on and a code
    /// is needed. Not a failure to report as one — the caller should ask for the code.
    /// </summary>
    RequiresTwoFactor,

    /// <summary>
    /// The account exists but its email address has never been confirmed, so it cannot sign in
    /// yet. Telling somebody their password is wrong here sends them to reset a password that was
    /// always right.
    /// </summary>
    EmailNotConfirmed,
}
