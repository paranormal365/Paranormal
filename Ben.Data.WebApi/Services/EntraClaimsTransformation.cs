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
public sealed class EntraClaimsTransformation : IClaimsTransformation
{
    /// <summary>Claim type added to the principal containing the local AppUser.Id (Guid string).</summary>
    public const string AppUserIdClaimType = "app_user_id";

    private readonly UserManager<AppUser> _userManager;
    private readonly ILogger<EntraClaimsTransformation> _logger;

    public EntraClaimsTransformation(UserManager<AppUser> userManager, ILogger<EntraClaimsTransformation> logger)
    {
        _userManager = userManager;
        _logger      = logger;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.HasClaim(c => c.Type == AppUserIdClaimType))
            return principal;

        var oidStr = principal.FindFirstValue("oid")
                     ?? principal.FindFirstValue("http://schemas.microsoft.com/identity/claims/objectidentifier");

        if (string.IsNullOrEmpty(oidStr) || !Guid.TryParse(oidStr, out var oid))
            return principal;

        // Log the OID and available email-like claims to help diagnose OID mismatches
        var emailClaim   = principal.FindFirstValue("email");
        var preferredName = principal.FindFirstValue("preferred_username");
        var upn          = principal.FindFirstValue("upn");
        _logger.LogDebug("EntraClaimsTransformation: oid={Oid} email={Email} preferred_username={PrefUser} upn={Upn}",
            oidStr, emailClaim ?? "(none)", preferredName ?? "(none)", upn ?? "(none)");

        // Try OID lookup first (fast path)
        var user = await _userManager.FindByLoginAsync("Microsoft", oid.ToString());

        // Fallback: match by email/preferred_username when OID changed (e.g. after app registration rotation).
        // Note: access tokens for custom APIs do not always include email/preferred_username — see Azure Portal
        // optional claims if this fallback is needed. Login can also be re-linked via /entra/complete-profile.
        if (user is null)
        {
            var email = emailClaim ?? preferredName ?? upn;
            if (!string.IsNullOrEmpty(email))
            {
                user = await _userManager.FindByEmailAsync(email);
                _logger.LogDebug("EntraClaimsTransformation: OID not linked — email fallback for '{Email}': {Found}",
                    email, user is not null ? "found" : "not found");
            }

            if (user is not null)
            {
                // Re-link the new OID permanently
                var loginInfo = new UserLoginInfo("Microsoft", oid.ToString(), "Microsoft");
                var result = await _userManager.AddLoginAsync(user, loginInfo);
                _logger.LogInformation("EntraClaimsTransformation: re-linked OID {Oid} to {Email} — succeeded={Ok}",
                    oidStr, user.Email, result.Succeeded);
            }
        }

        if (user is null)
        {
            _logger.LogDebug("EntraClaimsTransformation: no local account found for OID {Oid}", oidStr);
            return principal;
        }

        var identity = new ClaimsIdentity("EntraEnrichment");
        identity.AddClaim(new Claim(AppUserIdClaimType, user.Id.ToString()));

        var roles = await _userManager.GetRolesAsync(user);
        foreach (var role in roles)
            identity.AddClaim(new Claim(ClaimTypes.Role, role));

        principal.AddIdentity(identity);
        return principal;
    }
}

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

        // Look up the linked local AppUser by OID first
        var user = await _userManager.FindByLoginAsync("Microsoft", oid.ToString());

        // Fallback: if OID not linked yet, try matching by email.
        // This handles cases where the app registration was rotated and the token
        // now carries a different OID from the one stored in AspNetUserLogins.
        if (user is null)
        {
            var email = principal.FindFirstValue("email")
                        ?? principal.FindFirstValue("preferred_username")
                        ?? principal.FindFirstValue("upn");

            if (!string.IsNullOrEmpty(email))
                user = await _userManager.FindByEmailAsync(email);

            // Re-link the new OID so future requests use the fast path
            if (user is not null)
            {
                var loginInfo = new UserLoginInfo("Microsoft", oid.ToString(), "Microsoft");
                await _userManager.AddLoginAsync(user, loginInfo);
            }
        }

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
