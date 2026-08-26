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
    /// The sign-in endpoint was never reached, so the credentials were never judged.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="InvalidCredentials"/> on purpose: the fix is a deployment or a
    /// network, never a password.
    /// </remarks>
    Unreachable,

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

    /// <summary>
    /// The refusal arrived but its reason could not be read, so WHY is genuinely unknown.
    /// </summary>
    /// <remarks>
    /// <para>Distinct from <see cref="InvalidCredentials"/> because the credentials may have been
    /// perfectly good. <c>ReadDetailAsync</c> returns null for any response it cannot parse —
    /// a truncated body, an aborted read, a proxy page — and null used to fall through to
    /// "invalid email or password", which is the exact harm three separate comments in this file
    /// warn about: sending somebody to reset a password that was always right.</para>
    ///
    /// <para>Seen in a full Playwright run: an account with an unconfirmed email and a CORRECT
    /// password was told its password was wrong, because the 401's problem-detail did not survive
    /// the read under load. Asking the person to try again is honest; naming their password is
    /// not.</para>
    /// </remarks>
    UnknownRefusal,

    /// <summary>
    /// Identity has locked the account after too many failed attempts. Distinct from
    /// <see cref="RateLimited"/>, which is the server refusing the request rather than the
    /// account: this one is about this account specifically, and waiting is the only cure —
    /// retyping the right password does not help until the lockout expires.
    /// </summary>
    LockedOut,
}
