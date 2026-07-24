using Ben.Data.Common.Constants;
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
                return Ok(new MeResponse(linkedUser.Id, linkedUser.Email ?? string.Empty, isSuperAdmin));
            }
        }

        // ── Step 2: Try local Identity user (password-based login) ───────────
        try
        {
            var user = await _userManager.GetUserAsync(User);
            if (user is not null)
            {
                var isSuperAdmin = await _userManager.IsInRoleAsync(user, RoleNames.SuperAdmin);
                return Ok(new MeResponse(user.Id, user.Email ?? string.Empty, isSuperAdmin));
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

        return Ok(new MeResponse(Guid.Empty, email, IsSuperAdmin: false));
    }
}

public record MeResponse(Guid UserId, string Email, bool IsSuperAdmin);
