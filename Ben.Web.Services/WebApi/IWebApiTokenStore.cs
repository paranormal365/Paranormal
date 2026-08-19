namespace Ben.Web.Services.WebApi;

public interface IWebApiTokenStore
{
    string? AccessToken { get; set; }
    string? RefreshToken { get; set; }
    DateTimeOffset? AccessTokenExpiresAtUtc { get; set; }
    string? UserEmail { get; set; }
    string? UserDisplayName { get; set; }
    Guid? UserId { get; set; }
    bool IsSuperAdmin { get; set; }

    /// <summary>
    /// App-wide Admin role. Grants nothing on its own today beyond help-document visibility —
    /// see RoleNames.Admin.
    /// </summary>
    bool IsAdmin { get; set; }
    bool IsAuthenticated => !string.IsNullOrWhiteSpace(AccessToken);

    // Impersonation
    bool IsImpersonating { get; set; }
    string? OriginalAccessToken { get; set; }
    string? OriginalRefreshToken { get; set; }
    Guid? OriginalUserId { get; set; }
    string? OriginalUserEmail { get; set; }

    /// <summary>
    /// True when the active session uses a Microsoft Entra access token.
    /// Entra sessions are NOT persisted to ProtectedLocalStorage — the OIDC
    /// cookie handles re-authentication on reload instead.
    /// </summary>
    bool IsEntraSession { get; set; }

    /// <summary>Fires after any auth-relevant state change (login, logout, impersonate, refresh).</summary>
    event Action? StateChanged;

    /// <summary>Invoke after all state fields have been set to notify subscribers.</summary>
    void NotifyStateChanged();

    /// <summary>
    /// Completes once this circuit has finished resolving auth state for the current page
    /// load (see <see cref="Ben.Web.Services.IBenUserState.AuthReady"/> for why this
    /// matters). Signalled once by <c>MainLayout</c> via <see cref="SignalAuthReady"/> after
    /// its first-render restore attempt.
    /// </summary>
    Task AuthReady { get; }

    /// <summary>Marks <see cref="AuthReady"/> complete. Call once, after the first-render auth restore attempt finishes (success or failure).</summary>
    void SignalAuthReady();
}
