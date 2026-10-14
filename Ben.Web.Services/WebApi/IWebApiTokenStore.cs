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
    /// The IANA zone the signed-in person chose (from <c>/api/me</c>), or null. Cleared with the
    /// session; set again on every sign-in, restore and impersonation change (2026-09-28).
    /// </summary>
    string? SavedTimeZoneId { get; set; }

    /// <summary>
    /// Completes once this circuit has finished resolving auth state for the current page
    /// load (see <see cref="Ben.Web.Services.IBenUserState.AuthReady"/> for why this
    /// matters). Signalled once by <c>MainLayout</c> via <see cref="SignalAuthReady"/> after
    /// its first-render restore attempt.
    /// </summary>
    Task AuthReady { get; }

    /// <summary>Marks <see cref="AuthReady"/> complete. Call once, after the first-render auth restore attempt finishes (success or failure).</summary>
    void SignalAuthReady();

    /// <summary>
    /// One renewal at a time for this circuit. Every part of a page asks the API at once, and with
    /// an expired token each would otherwise spend the refresh token separately — and a refresh
    /// token is good for one use (10/14/2026).
    /// </summary>
    SemaphoreSlim RenewalGate { get; }

    /// <summary>
    /// The API refused this session and it could not be renewed. <c>MainLayout</c> signs the person
    /// out and takes them to sign in (Ben, 10/14/2026: "just redirect to the sign in page and
    /// actually sign me out instead of telling and making me have to actually sign out").
    /// </summary>
    event Action? SessionEnded;

    /// <summary>Raises <see cref="SessionEnded"/>.</summary>
    void EndSession();
}
