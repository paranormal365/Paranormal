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
    private readonly ExternalSignInService _external;
    private readonly ILogger<EntraClaimsTransformation> _logger;

    public EntraClaimsTransformation(
        UserManager<AppUser> userManager,
        ExternalSignInService external,
        ILogger<EntraClaimsTransformation> logger)
    {
        _userManager = userManager;
        _external    = external;
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

        // BY SUBJECT, AND ONLY BY SUBJECT. Until 2026-09-10 an object id nobody had linked was
        // looked up by its email / preferred_username / upn claim and the account found was linked
        // to it for good, meant for the day an app registration rotates every object id. But
        // Microsoft does not vouch for a personal account's email claim, this authority accepts
        // personal accounts, and so anybody could reach an account here by putting its address on
        // a Microsoft account of their own. Removed, not narrowed (decided with Ben): a rotated
        // object id uses the link door once, proving the account with its password and second
        // factor, which is what ExternalSignInService.LinkAsync is for.
        var user = await _userManager.FindByLoginAsync("Microsoft", oid.ToString());
        if (user is null)
        {
            _logger.LogDebug("EntraClaimsTransformation: no local account linked to OID {Oid}", oidStr);
            return principal;
        }

        // MAY THIS ACCOUNT BE USED AT ALL. This is the choke point for every Entra request, and
        // nothing downstream asks — a Microsoft token IS the credential, so there is no sign-in
        // step to run the usual checks. Without this, closing an account or locking it stopped
        // passwords and did nothing whatever to anybody holding a linked Microsoft identity.
        //
        // The gate is the shared service's — the same one every external door asks — so the two
        // Microsoft doors cannot disagree about who may come in. Refusing here means the principal
        // keeps its Microsoft identity but gains no local one, so it resolves exactly as an
        // unlinked Entra caller does. That is the honest outcome: the person really is signed in
        // to Microsoft, and really has no account here they may use.
        if (!await _external.MaySignInAsync(user))
        {
            _logger.LogWarning(
                "EntraClaimsTransformation: refused {UserId} — the account may not sign in.", user.Id);
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
