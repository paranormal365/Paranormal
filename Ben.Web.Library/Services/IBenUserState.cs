namespace Ben.Web.Library.Services;

/// <summary>
/// Exposes the minimum authentication state needed by shared Blazor library components.
/// </summary>
/// <remarks>
/// Implemented by <c>WebApiTokenStore</c> in <c>Ben.Web.WebApp</c>, which is registered
/// in DI as both <c>IWebApiTokenStore</c> and <c>IBenUserState</c>.  Library components
/// depend on this interface rather than the full <c>IWebApiTokenStore</c> so that
/// <c>Ben.Web.Library</c> does not need a project reference to <c>Ben.Web.WebApp</c>.
/// </remarks>
public interface IBenUserState
{
    /// <summary>Gets whether the user is currently authenticated (has a valid access token).</summary>
    bool IsAuthenticated { get; }

    /// <summary>Gets whether the authenticated user holds the <see cref="Ben.Data.Common.Constants.RoleNames.SuperAdmin"/> role.</summary>
    bool IsSuperAdmin { get; }

    /// <summary>
    /// App-wide Admin role — a tier below SuperAdmin. Today this only reveals the
    /// app-administration help documents; see RoleNames.Admin for why nothing else reads it yet.
    /// </summary>
    bool IsAdmin { get; }

    /// <summary>Gets whether the current session is an impersonation session started by a SuperAdmin.</summary>
    bool IsImpersonating { get; }

    /// <summary>Gets the authenticated user's email address, or <c>null</c> when not signed in.</summary>
    string? UserEmail { get; }

    /// <summary>Gets the authenticated user's <see cref="Guid"/> primary key, or <c>null</c> when not signed in.</summary>
    Guid? UserId { get; }

    /// <summary>
    /// Raised whenever sign-in state changes within this circuit — login, logout, or a SuperAdmin
    /// starting/stopping impersonation. Anything caching per-user data (see
    /// <see cref="NotificationState"/>) must discard it on this signal, because the circuit
    /// outlives the session it was fetched for.
    /// </summary>
    event Action? StateChanged;

    /// <summary>
    /// Completes once this circuit has finished resolving auth state for the current page
    /// load — i.e. after MainLayout has attempted to restore a persisted session (or bridge
    /// an Entra session) on first render. <see cref="IsAuthenticated"/> is unreliable before
    /// this completes: on a hard navigation, a fresh circuit always starts unauthenticated
    /// until that restore runs, so any page-load guard that checks <see cref="IsAuthenticated"/>
    /// without awaiting this first will incorrectly redirect an actually-signed-in user to
    /// /login. Await this before checking <see cref="IsAuthenticated"/> in OnInitializedAsync
    /// or OnAfterRenderAsync(firstRender).
    /// </summary>
    Task AuthReady { get; }

    /// <summary>
    /// The viewer's browser-resolved IANA timezone, populated once via JS interop during
    /// MainLayout's first-render bootstrap (the same sequence that signals <see cref="AuthReady"/>).
    /// Defaults to UTC until resolved, or if resolution fails — components should await
    /// <see cref="AuthReady"/> before reading this, exactly as they already do for auth state.
    /// </summary>
    TimeZoneInfo BrowserTimeZone { get; }
}
