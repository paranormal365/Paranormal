using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// Enriches Entra (Azure AD / MSA) JWT principals with the linked local AppUser ID
/// and DB role claims so that <c>User.IsInRole()</c> and <c>ClaimTypes.NameIdentifier</c>
/// work identically for both local Identity bearer tokens and Entra JWTs.
/// </summary>
/// <remarks>
/// For local Identity bearer tokens this transform is a no-op — those tokens already
/// carry <c>ClaimTypes.NameIdentifier</c> (AppUser Guid) and role claims.
/// For Entra JWTs the <c>oid</c> claim is used to find the linked <c>AppUser</c> via
/// <c>AspNetUserLogins</c>.  If a linked account is found its Id and roles are injected
/// as a supplementary <c>ClaimsIdentity</c> on the principal.
/// </remarks>
public sealed class EntraClaimsTransformation : IClaimsTransformation
{
    /// <summary>Claim type added to the principal containing the local AppUser.Id (Guid string).</summary>
    public const string AppUserIdClaimType = "app_user_id";

    private readonly UserManager<AppUser> _userManager;

    public EntraClaimsTransformation(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Skip if already enriched (IClaimsTransformation may be called more than once per request)
        if (principal.HasClaim(c => c.Type == AppUserIdClaimType))
            return principal;

        // Only enrich Entra tokens — identified by the presence of an "oid" claim
        var oidStr = principal.FindFirstValue("oid")
                     ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");

        if (string.IsNullOrEmpty(oidStr) || !Guid.TryParse(oidStr, out var oid))
            return principal;

        // Look up the linked local AppUser
        var user = await _userManager.FindByLoginAsync("Microsoft", oid.ToString());
        if (user is null)
            return principal; // Entra user with no linked local account — leave principal as-is

        // Build a supplementary identity with the AppUser.Id and DB roles
        var identity = new ClaimsIdentity("EntraEnrichment");
        identity.AddClaim(new Claim(AppUserIdClaimType, user.Id.ToString()));

        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));

        principal.AddIdentity(identity);
        return principal;
    }
}
