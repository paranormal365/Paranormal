using Ben.Web.Services;

namespace Ben.Web.Services.WebApi;

/// <summary>
/// The Account half of the adapter — implements <see cref="Ben.Web.Services.IBenAccountClient"/>.
/// </summary>
/// <remarks>
/// The sign-up methods call the <b>anonymous</b> helpers, because the person using them does not
/// have an account yet and therefore has no token to send. Attaching one would be harmless but
/// misleading; more to the point, the endpoints are <c>[AllowAnonymous]</c> and rate-limited on the
/// assumption that nobody is authenticated.
/// </remarks>
public sealed partial class BenAdminClientAdapter
{
    // ── Signing up ───────────────────────────────────────────────────────────

    public Task<HandleAvailability?> CheckHandleAsync(string handle, CancellationToken token = default)
        => _api.GetAnonymousAsync<HandleAvailability>(
            $"/api/account/handle-available?handle={Uri.EscapeDataString(handle ?? string.Empty)}", token);

    public async Task<RegisterOutcome> RegisterAsync(RegisterAccount request, CancellationToken token = default)
    {
        var result = await _api.PostAnonymousReadingBodyAsync<RegisterAccount, RegisterOutcome>(
            "/api/account/register", request, token);

        // A null here means the server did not answer in the shape this expects — a 500, a proxy
        // page, a dropped connection. The account may or may not exist; saying "couldn't reach"
        // is the only honest thing.
        return result ?? new RegisterOutcome(false, "Couldn't reach the server. Try again in a moment.", null);
    }

    public async Task<ConfirmEmailOutcome> ConfirmEmailAsync(
        Guid userId, string code, CancellationToken token = default)
    {
        var result = await _api.PostAnonymousReadingBodyAsync<object, ConfirmEmailOutcome>(
            "/api/account/confirm-email", new { userId, code }, token);

        return result ?? new ConfirmEmailOutcome(false, "Couldn't reach the server. Try the link again.");
    }

    public async Task<string> ResendConfirmationAsync(string email, CancellationToken token = default)
    {
        var result = await _api.PostAnonymousReadingBodyAsync<object, ResendConfirmationOutcome>(
            "/api/account/resend-confirmation", new { email }, token);

        // Null is the server not answering in shape — which includes the rate limiter's 429,
        // since this anonymous endpoint sits behind the auth policy. Honest about that rather
        // than pretending a send happened.
        return result?.Message
            ?? "Couldn't ask for a new link just now. Wait a minute and try again.";
    }

    private sealed record ResendConfirmationOutcome(string Message);

    // ── Password ─────────────────────────────────────────────────────────────

    public async Task<bool?> GetHasPasswordAsync(CancellationToken token = default)
    {
        var status = await _api.GetAsync<PasswordStatusResponse>("/api/me/password", token);
        return status?.HasPassword;
    }

    public async Task<string?> SetPasswordAsync(
        string? currentPassword, string newPassword, CancellationToken token = default)
    {
        var (_, error) = await _api.SendExpectingReasonAsync<object, object>(
            HttpMethod.Post, "/api/me/password", new { currentPassword, newPassword }, token);

        return error;   // null on success — same shape as DisableTwoFactorAsync
    }

    private sealed record PasswordStatusResponse(bool HasPassword);

    // ── Two-factor ───────────────────────────────────────────────────────────

    public Task<TwoFactorStatus?> GetTwoFactorStatusAsync(CancellationToken token = default)
        => _api.GetAsync<TwoFactorStatus>("/api/me/2fa", token);

    public async Task<TwoFactorSetup?> BeginTwoFactorSetupAsync(CancellationToken token = default)
    {
        var (result, _) = await _api.SendExpectingReasonAsync<object, TwoFactorSetup>(
            HttpMethod.Post, "/api/me/2fa/setup", new { }, token);

        return result;
    }

    public Task<(string[] RecoveryCodes, string? Error)> EnableTwoFactorAsync(
        string code, CancellationToken token = default)
        => CodesOrReasonAsync("/api/me/2fa/enable", code, token);

    public Task<(string[] RecoveryCodes, string? Error)> RegenerateRecoveryCodesAsync(
        string code, CancellationToken token = default)
        => CodesOrReasonAsync("/api/me/2fa/recovery-codes", code, token);

    public async Task<string?> DisableTwoFactorAsync(string code, CancellationToken token = default)
    {
        var (_, error) = await _api.SendExpectingReasonAsync<object, object>(
            HttpMethod.Post, "/api/me/2fa/disable", new { code }, token);

        return error;   // null on success
    }

    /// <summary>
    /// Posts a code and returns either the recovery codes or the server's own refusal.
    /// </summary>
    /// <remarks>
    /// The refusal matters as much as the success: "that code was not right" is what somebody
    /// mistyping a six-digit number needs to see, and the ordinary Post helper would flatten it
    /// into a null that reads as "something broke".
    /// </remarks>
    private async Task<(string[] RecoveryCodes, string? Error)> CodesOrReasonAsync(
        string url, string code, CancellationToken token)
    {
        var (result, error) = await _api.SendExpectingReasonAsync<object, RecoveryCodesResponse>(
            HttpMethod.Post, url, new { code }, token);

        if (error is not null) return ([], error);

        // Explicit rather than `?? []`: the codes are only ever returned alongside a null error,
        // so there is no path handing back "no recovery codes" as a fact.
        if (result is null) return ([], "No recovery codes came back.");

        return (result.RecoveryCodes, null);
    }

    private sealed record RecoveryCodesResponse(string[] RecoveryCodes);
}
