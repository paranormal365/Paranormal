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
        // app_user_id is injected by EntraClaimsTransformation when it works.
        // If not present, fall back directly to the oid claim.
        AppUser? user = null;

        var appUserIdStr = context.User.FindFirstValue(EntraClaimsTransformation.AppUserIdClaimType);
        if (Guid.TryParse(appUserIdStr, out var appUserId))
        {
            user = await _userManager.FindByIdAsync(appUserId.ToString());
        }

        if (user is null)
        {
            var oidStr = context.User.FindFirstValue("oid")
                         ?? context.User.FindFirstValue(
                             "http://schemas.microsoft.com/identity/claims/objectidentifier");

            if (!string.IsNullOrEmpty(oidStr))
                user = await _userManager.FindByLoginAsync("Microsoft", oidStr);
        }

        if (user is not null && await _userManager.IsInRoleAsync(user, RoleNames.SuperAdmin))
            context.Succeed(requirement);
    }
}
