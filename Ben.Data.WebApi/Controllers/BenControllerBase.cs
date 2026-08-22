using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Shared base for all Ben WebApi controllers.
/// Centralises identity resolution so no controller needs its own copy.
/// </summary>
[ApiController]
public abstract class BenControllerBase : ControllerBase
{
    /// <summary>
    /// Whether the caller holds SuperAdmin, on an endpoint that does <b>not</b> require
    /// authentication.
    /// </summary>
    /// <remarks>
    /// <para><b>Why an anonymous endpoint needs its own answer.</b> <c>UseAuthentication</c>
    /// populates <c>User</c> from the <i>default</i> scheme only — the local Identity bearer
    /// handler. On an endpoint with <c>[Authorize]</c> that does not matter, because the default
    /// policy pins both schemes and the authorization middleware replaces <c>User</c> with the
    /// merged, transformed principal. On an <c>[AllowAnonymous]</c> endpoint nothing does that, so
    /// a caller signed in with Microsoft arrives with no principal at all: not merely without the
    /// role, but unauthenticated. <c>User.IsInRole</c> returns false and the endpoint quietly
    /// serves them the visitor's view.</para>
    ///
    /// <para>Item 140. Only two endpoints were affected, and both failed <i>closed</i> — an admin
    /// saw less, never more — so this is a visibility gap rather than a security one.</para>
    ///
    /// <para>Costs nothing for an actual visitor: the local check answers first for a local
    /// session, and the Entra round trip is a scheme authentication against a token that a
    /// visitor does not have.</para>
    /// </remarks>
    protected async Task<bool> CallerIsSuperAdminAsync()
    {
        if (User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)) return true;

        // Entra is registered only when it is configured, and AuthenticateAsync THROWS on a scheme
        // that is not registered — so asking for it unconditionally would take these anonymous
        // endpoints down everywhere Entra is off, which is most environments. The first version of
        // this method did exactly that and the equipment tests caught it.
        var schemes = HttpContext.RequestServices?
            .GetService<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>();

        if (schemes is null) return false;
        if (await schemes.GetSchemeAsync(Ben.Data.Common.Constants.AuthPolicyNames.EntraScheme) is null)
            return false;

        var entra = await HttpContext.AuthenticateAsync(
            Ben.Data.Common.Constants.AuthPolicyNames.EntraScheme);

        // The claims transformation runs as part of AuthenticateAsync, so the role claims it
        // resolves from the database are already on this principal.
        return entra.Succeeded
            && entra.Principal is not null
            && entra.Principal.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin);
    }

    /// <summary>
    /// Returns the current user's AppUser Guid.
    /// Prefers the <c>app_user_id</c> claim injected by EntraClaimsTransformation;
    /// falls back to the standard <c>NameIdentifier</c> / <c>sub</c> claim.
    /// Returns <see cref="Guid.Empty"/> when neither claim is present or parseable.
    /// </summary>
    protected Guid GetCurrentUserId()
    {
        var value = User.FindFirstValue(EntraClaimsTransformation.AppUserIdClaimType)
                 ?? User.FindFirstValue(ClaimTypes.NameIdentifier)
                 ?? User.FindFirstValue("sub");
        return Guid.TryParse(value, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Returns the current user's AppUser Guid, or throws <see cref="UnauthorizedAccessException"/>
    /// when the claim is absent or invalid.
    /// </summary>
    protected Guid GetCurrentUserIdOrThrow()
    {
        var id = GetCurrentUserId();
        if (id == Guid.Empty)
            throw new UnauthorizedAccessException("Authenticated user id claim is missing or invalid.");
        return id;
    }

    /// <summary>
    /// Returns the current user's AppUser Guid as nullable.
    /// Returns <c>null</c> when the claim is absent or unparseable.
    /// </summary>
    protected Guid? GetCurrentUserIdOrNull()
    {
        var id = GetCurrentUserId();
        return id == Guid.Empty ? null : id;
    }

    /// <summary>
    /// Fires an audit log task, absorbing any failure so it never surfaces to the caller — but
    /// logging it, so a broken audit trail is visible to whoever runs this.
    /// </summary>
    /// <remarks>
    /// The swallow is deliberate: a logging outage must not roll back a CRUD operation that
    /// already succeeded. Swallowing <em>silently</em> was not deliberate — it meant a systemic
    /// failure (a bad migration, a full disk, a broken connection string) would produce a
    /// perfectly quiet application writing no audit rows at all, discoverable only by noticing
    /// their absence. Same reasoning already applied to upload metadata extraction.
    /// </remarks>
    protected async Task TryAuditAsync(Task auditTask)
    {
        try
        {
            await auditTask;
        }
        catch (Exception ex)
        {
            HttpContext?.RequestServices
                .GetService<ILogger<BenControllerBase>>()?
                .LogError(ex, "Audit log write failed for {Path}", HttpContext?.Request?.Path.Value);
        }
    }
}
