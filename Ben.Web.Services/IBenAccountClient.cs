namespace Ben.Web.Services;

/// <summary>
/// The Account slice of <see cref="IBenAdminClient"/> — signing up, confirming an email address,
/// and two-factor authentication.
/// </summary>
/// <remarks>
/// The first three methods are used by people who are <b>not signed in</b>, which is unusual for
/// this interface and worth naming: they must not attach a bearer token, and the adapter calls the
/// anonymous helpers for exactly that reason. Everything under <c>2fa</c> is the opposite — it acts
/// on the caller's own account and needs the token.
/// </remarks>
public interface IBenAccountClient
{
    // ── Signing up ───────────────────────────────────────────────────────────

    /// <summary>Whether an <c>@name</c> is legal and free. Advisory — the unique index decides.</summary>
    Task<HandleAvailability?> CheckHandleAsync(string handle, CancellationToken token = default);

    /// <summary>
    /// Creates an account and triggers its confirmation email.
    /// </summary>
    /// <remarks>
    /// Succeeds with the same message whether or not the address was already registered — see the
    /// WebApi's registration controller for why. Never treat a success here as proof that a new
    /// account was made.
    /// </remarks>
    Task<RegisterOutcome> RegisterAsync(RegisterAccount request, CancellationToken token = default);

    /// <summary>Confirms an email address from the link in the confirmation email.</summary>
    Task<ConfirmEmailOutcome> ConfirmEmailAsync(Guid userId, string code, CancellationToken token = default);

    // ── Two-factor ───────────────────────────────────────────────────────────

    Task<TwoFactorStatus?> GetTwoFactorStatusAsync(CancellationToken token = default);

    /// <summary>Starts enrolment. Nothing is switched on until a code is verified.</summary>
    Task<TwoFactorSetup?> BeginTwoFactorSetupAsync(CancellationToken token = default);

    /// <summary>Verifies a code and switches 2FA on. Returns the recovery codes, shown once.</summary>
    Task<(string[] RecoveryCodes, string? Error)> EnableTwoFactorAsync(string code, CancellationToken token = default);

    /// <summary>Switches 2FA off. Requires a current code.</summary>
    Task<string?> DisableTwoFactorAsync(string code, CancellationToken token = default);

    /// <summary>Issues a fresh set of recovery codes, invalidating the old ones.</summary>
    Task<(string[] RecoveryCodes, string? Error)> RegenerateRecoveryCodesAsync(string code, CancellationToken token = default);
}

/// <param name="Reason">Why not, when <paramref name="Available"/> is false.</param>
public sealed record HandleAvailability(string Handle, bool Available, string? Reason);

public sealed record RegisterAccount(
    string Email, string Password, string DisplayName, string Handle,
    string FirstName, string LastName);

/// <param name="Field">Which field to point at, when the server could say. Null for a general message.</param>
public sealed record RegisterOutcome(bool Succeeded, string Message, string? Field);

public sealed record ConfirmEmailOutcome(bool Succeeded, string Message);

public sealed record TwoFactorStatus(bool Enabled, bool HasAuthenticatorKey, int RecoveryCodesRemaining);

/// <param name="SharedKey">Formatted for typing in by hand, when a QR cannot be scanned.</param>
/// <param name="AuthenticatorUri">The <c>otpauth://</c> URI to render as a QR code.</param>
public sealed record TwoFactorSetup(string SharedKey, string AuthenticatorUri);
