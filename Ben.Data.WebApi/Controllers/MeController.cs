using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Returns the currently authenticated user's identity and role information.
/// Supports both local Identity (password) logins and Microsoft Entra OIDC sessions.
/// </summary>
[ApiController]
[Authorize]
[Route("api/me")]
public sealed class MeController : BenControllerBase
{
    private readonly UserManager<AppUser> _userManager;

    public MeController(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    /// <summary>
    /// Returns the authenticated user's basic identity including role info.
    /// Works for both local Identity users and Microsoft Entra users.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<MeResponse>> Get()
    {
        // ── Step 1: Try Entra external login lookup (OID-based) ──────────────
        // If the user authenticated via Entra OIDC, their token carries an "oid" claim.
        // We look up the linked local AppUser by that external login before falling back.
        var oidStr = User.FindFirst("oid")?.Value
                     ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;

        if (Guid.TryParse(oidStr, out var entraOid))
        {
            var linkedUser = await _userManager.FindByLoginAsync("Microsoft", entraOid.ToString());
            if (linkedUser is not null)
            {
                var isSuperAdmin = await _userManager.IsInRoleAsync(linkedUser, RoleNames.SuperAdmin);
                var isAdmin = await _userManager.IsInRoleAsync(linkedUser, RoleNames.Admin);
                var isModerator = isSuperAdmin
                                  || await _userManager.IsInRoleAsync(linkedUser, RoleNames.Moderator);
                return Ok(new MeResponse(
                    linkedUser.Id, linkedUser.Email ?? string.Empty, isSuperAdmin, isAdmin, isModerator,
                    linkedUser.EmailKind, linkedUser.EmailConfirmed));
            }
        }

        // ── Step 2: Try local Identity user (password-based login) ───────────
        try
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is not null)
            {
                var isSuperAdmin = await _userManager.IsInRoleAsync(user, RoleNames.SuperAdmin);
                var isAdmin = await _userManager.IsInRoleAsync(user, RoleNames.Admin);
                var isModerator = isSuperAdmin
                                  || await _userManager.IsInRoleAsync(user, RoleNames.Moderator);
                return Ok(new MeResponse(
                    user.Id, user.Email ?? string.Empty, isSuperAdmin, isAdmin, isModerator,
                    user.EmailKind, user.EmailConfirmed));
            }
        }
        catch (FormatException)
        {
            // The NameIdentifier claim is not a valid Guid — this is an Entra JWT
            // (the sub/NameIdentifier is a non-GUID string). Fall through to Guid.Empty.
        }

        // ── Step 3: Entra user with no linked local account ──────────────────
        // Return UserId = Guid.Empty to signal the WebApp that account setup is needed.
        var email = User.FindFirst("preferred_username")?.Value
                    ?? User.FindFirst("email")?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                    ?? string.Empty;

        return Ok(new MeResponse(Guid.Empty, email, IsSuperAdmin: false, IsAdmin: false, IsModerator: false));
    }
}

/// <param name="UserId">The local AppUser id, or Guid.Empty for an Entra user with no linked account.</param>
/// <param name="Email">Sign-in address.</param>
/// <param name="IsSuperAdmin">Holds the SuperAdmin role.</param>
/// <param name="IsAdmin">
/// Holds the app-wide Admin role. Reported separately from SuperAdmin rather than folded into it:
/// Admin is a strictly smaller thing (see RoleNames.Admin), and a client that cannot tell them
/// apart would have to guess.
/// </param>
/// <param name="IsModerator">
/// May moderate (item 186 F5). True for a SuperAdmin as well as for the Moderator role, matching
/// ModeratorHandler on the server — two answers to "may this person moderate" would eventually
/// disagree, and the visible symptom would be a menu item leading to a 403.
/// </param>
/// <param name="EmailKind">
/// Whether <paramref name="Email"/> is an address this person chose and can read. A client must not
/// present an Apple relay or a placeholder as somebody's own address, and must not claim to have
/// emailed one.
/// </param>
/// <param name="EmailConfirmed">
/// Whether the person has proved they can read <paramref name="Email"/>. False only for an account
/// created from a provider's unverified address claim, which may use the site through that provider
/// but cannot reset a password or be written to until the address is confirmed.
/// </param>
public record MeResponse(
    Guid UserId, string Email, bool IsSuperAdmin, bool IsAdmin, bool IsModerator = false,
    EmailAddressKind EmailKind = EmailAddressKind.Ordinary, bool EmailConfirmed = true);
