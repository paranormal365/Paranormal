namespace Ben.Web.Services.WebApi;

/// <summary>
/// The website's token store: everything a client needs (<see cref="ITokenStore"/>) plus the three
/// things only a Blazor Server host has — impersonation, the Entra OIDC bridge, and the
/// prerender-versus-interactive gate.
/// </summary>
public interface IWebApiTokenStore : ITokenStore
{
    // Impersonation
    bool IsImpersonating { get; set; }
    string? OriginalAccessToken { get; set; }
    string? OriginalRefreshToken { get; set; }
    Guid? OriginalUserId { get; set; }
    string? OriginalUserEmail { get; set; }
    string? OriginalUserDisplayName { get; set; }

    /// <summary>
    /// True when the active session uses a Microsoft Entra access token.
    /// Entra sessions are NOT persisted to ProtectedLocalStorage — the OIDC
    /// cookie handles re-authentication on reload instead.
    /// </summary>
    bool IsEntraSession { get; set; }

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
