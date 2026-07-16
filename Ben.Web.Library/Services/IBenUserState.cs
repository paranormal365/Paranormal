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

    /// <summary>Gets whether the current session is an impersonation session started by a SuperAdmin.</summary>
    bool IsImpersonating { get; }

    /// <summary>Gets the authenticated user's email address, or <c>null</c> when not signed in.</summary>
    string? UserEmail { get; }

    /// <summary>Gets the authenticated user's <see cref="Guid"/> primary key, or <c>null</c> when not signed in.</summary>
    Guid? UserId { get; }
}
