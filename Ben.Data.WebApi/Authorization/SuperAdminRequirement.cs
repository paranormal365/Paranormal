using Ben.Data.Common.Constants;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Ben.Data.WebApi.Authorization;

/// <summary>
/// Authorization requirement satisfied when the caller holds the SuperAdmin role.
/// Works for both local Identity bearer tokens (claim-based) and Entra JWTs
/// (DB lookup via UserManager — no reliance on claim injection).
/// </summary>
public sealed class SuperAdminRequirement : IAuthorizationRequirement { }

/// <summary>
/// Satisfied when the caller holds either app-wide administration role.
/// </summary>
/// <remarks>
/// Separate from <see cref="SuperAdminRequirement"/> rather than a parameter on it, so that
/// widening an endpoint from "SuperAdmin only" to "either administrator" is a visible change of
/// requirement at the call site. <see cref="RoleNames.Admin"/> deliberately grants almost nothing
/// today; this exists for the endpoints that already accepted both.
/// </remarks>
public sealed class AppAdministratorRequirement : IAuthorizationRequirement { }

/// <summary>
/// Satisfied when the caller may moderate: the Moderator role, or SuperAdmin implicitly.
/// </summary>
/// <remarks>
/// SuperAdmin is included rather than required separately so that nobody has to hold two roles to
/// do one job — and so that a site with no moderators yet is still moderatable by the person who
/// runs it, which is the state every site starts in.
/// </remarks>
public sealed class ModeratorRequirement : IAuthorizationRequirement { }

/// <summary>
/// Resolves the <see cref="AppUser"/> behind a principal, whichever way it authenticated.
/// </summary>
/// <remarks>
/// <para>Shared by both handlers below so there is exactly one answer to "who is calling?". A
/// second copy of this logic is how an authorization boundary quietly develops two behaviours.</para>
///
/// <para>The order matters. <c>app_user_id</c> is injected by
/// <see cref="EntraClaimsTransformation"/> when it runs; the <c>oid</c> fallback exists because it
/// does not always run, and an Entra caller with no resolvable user must be refused rather than
/// treated as anonymous-but-allowed.</para>
/// </remarks>
internal static class AppUserPrincipal
{
    public static async Task<AppUser?> ResolveAsync(ClaimsPrincipal principal, UserManager<AppUser> userManager)
    {
        var appUserIdStr = principal.FindFirstValue(EntraClaimsTransformation.AppUserIdClaimType);
        if (Guid.TryParse(appUserIdStr, out var appUserId))
        {
            var byId = await userManager.FindByIdAsync(appUserId.ToString());
            if (byId is not null) return byId;
        }

        var oidStr = principal.FindFirstValue("oid")
                     ?? principal.FindFirstValue(
                         "http://schemas.microsoft.com/identity/claims/objectidentifier");

        if (!string.IsNullOrEmpty(oidStr))
            return await userManager.FindByLoginAsync("Microsoft", oidStr);

        return null;
    }
}

/// <summary>
/// Handles <see cref="SuperAdminRequirement"/> by checking the SuperAdmin role
/// directly from the database via <c>UserManager</c> for Entra JWT sessions,
/// and via role claims for local Identity bearer sessions.
/// </summary>
public sealed class SuperAdminHandler : AuthorizationHandler<SuperAdminRequirement>
{
    private readonly UserManager<AppUser> _userManager;

    public SuperAdminHandler(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, SuperAdminRequirement requirement)
    {
        // ── Path 1: local Identity bearer token carries role claims ───────────
        if (context.User.IsInRole(RoleNames.SuperAdmin))
        {
            context.Succeed(requirement);
            return;
        }

        // ── Path 2: Entra JWT — look up user in DB by OID ────────────────────
        var user = await AppUserPrincipal.ResolveAsync(context.User, _userManager);

        if (user is not null && await _userManager.IsInRoleAsync(user, RoleNames.SuperAdmin))
            context.Succeed(requirement);
    }
}

/// <summary>
/// Handles <see cref="AppAdministratorRequirement"/>, accepting either app-wide role by the same
/// two paths as <see cref="SuperAdminHandler"/>.
/// </summary>
public sealed class AppAdministratorHandler : AuthorizationHandler<AppAdministratorRequirement>
{
    private readonly UserManager<AppUser> _userManager;

    public AppAdministratorHandler(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, AppAdministratorRequirement requirement)
    {
        foreach (var role in RoleNames.AppAdministrators)
        {
            if (context.User.IsInRole(role))
            {
                context.Succeed(requirement);
                return;
            }
        }

        var user = await AppUserPrincipal.ResolveAsync(context.User, _userManager);
        if (user is null) return;

        foreach (var role in RoleNames.AppAdministrators)
        {
            if (await _userManager.IsInRoleAsync(user, role))
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}

/// <summary>
/// Handles <see cref="ModeratorRequirement"/> by the same two paths as the handlers above.
/// </summary>
public sealed class ModeratorHandler : AuthorizationHandler<ModeratorRequirement>
{
    private readonly UserManager<AppUser> _userManager;

    public ModeratorHandler(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ModeratorRequirement requirement)
    {
        foreach (var role in RoleNames.Moderators)
        {
            if (context.User.IsInRole(role))
            {
                context.Succeed(requirement);
                return;
            }
        }

        var user = await AppUserPrincipal.ResolveAsync(context.User, _userManager);
        if (user is null) return;

        foreach (var role in RoleNames.Moderators)
        {
            if (await _userManager.IsInRoleAsync(user, role))
            {
                context.Succeed(requirement);
                return;
            }
        }
    }
}
