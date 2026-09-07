using Ben.Data.Common.Constants;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Handles Microsoft Entra (Azure AD) account registration and linking.
/// Called from the Blazor WebApp after the OIDC callback to either create a new
/// local account or link the Entra identity to an existing local account.
/// </summary>
/// <remarks>
/// Both actions require <see cref="AuthPolicyNames.EntraOnly"/> — a validated Entra JWT — and
/// read the caller's OID/email from that token's own claims (see <see cref="GetValidatedEntraIdentity"/>),
/// exactly as <c>MeController</c> does. Neither action ever trusts an OID or email supplied in
/// the request body: doing so previously let any caller register or link an identity it didn't
/// actually hold (account squatting / account-takeover via <c>EntraClaimsTransformation</c>'s
/// OID-based lookup on later, genuine sign-ins).
/// </remarks>
[ApiController]
[Route("api/auth/entra")]
public sealed class EntraAuthController : BenControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly Ben.Data.WebApi.Services.UserHandleService _handles;

    public EntraAuthController(
        UserManager<AppUser> userManager, Ben.Data.WebApi.Services.UserHandleService handles)
    {
        _userManager = userManager;
        _handles     = handles;
    }

    /// <summary>
    /// Creates a new local AppUser from the caller's validated Entra identity and links it.
    /// Called when an Entra user arrives with no matching local account.
    /// </summary>
    [HttpPost("register")]
    [Authorize(Policy = AuthPolicyNames.EntraOnly)]
    public async Task<ActionResult<EntraRegisterResult>> Register(
        [FromBody] EntraRegisterRequest request,
        CancellationToken cancellationToken)
    {
        var (entraOid, entraEmail) = GetValidatedEntraIdentity();
        if (entraOid is null || string.IsNullOrWhiteSpace(entraEmail))
            return BadRequest("The Entra token did not contain the expected identity claims.");

        if (string.IsNullOrWhiteSpace(request.DisplayName))
            return BadRequest("Display name is required.");

        // Guard: if email already exists, the user should use the link flow instead
        var existing = await _userManager.FindByEmailAsync(entraEmail);
        if (existing is not null)
            return Conflict(new { Message = "An account with this email already exists. Please use the 'Link existing account' option." });

        // Guard: if this OID is already linked (e.g., duplicate register attempt), return that user
        var oidString = entraOid.Value.ToString();
        var alreadyLinked = await _userManager.FindByLoginAsync("Microsoft", oidString);
        if (alreadyLinked is not null)
            return Ok(new EntraRegisterResult(alreadyLinked.Id, alreadyLinked.Email ?? string.Empty));

        var user = new AppUser
        {
            Id                 = Guid.NewGuid(),
            Email              = entraEmail,
            UserName           = entraEmail,
            NormalizedEmail    = entraEmail.ToUpperInvariant(),
            NormalizedUserName = entraEmail.ToUpperInvariant(),
            EmailConfirmed     = true,  // Entra has verified the email
            DisplayName        = request.DisplayName,
            // C1: allocated here rather than left for the restart backfill — an account with no
            // @name cannot be mentioned and is invisible to the feed until that job next runs.
            Handle             = await _handles.AllocateAsync(request.DisplayName, entraEmail, cancellationToken),
            DateCreated        = DateTime.UtcNow
        };

        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
            return BadRequest(new { Errors = createResult.Errors.Select(e => e.Description) });

        var loginInfo = new UserLoginInfo("Microsoft", oidString, entraEmail);
        var linkResult = await _userManager.AddLoginAsync(user, loginInfo);
        if (!linkResult.Succeeded)
        {
            await _userManager.DeleteAsync(user); // rollback
            return BadRequest(new { Errors = linkResult.Errors.Select(e => e.Description) });
        }

        return Ok(new EntraRegisterResult(user.Id, user.Email ?? string.Empty));
    }

    /// <summary>
    /// Links the caller's validated Entra identity to an existing local account. The caller
    /// proves ownership of the Entra identity by presenting a valid Entra JWT, and proves
    /// ownership of the target local account by supplying its password in the body — token
    /// possession alone is deliberately not sufficient; there is no requirement that the caller
    /// already hold a local session, since the whole point of this endpoint is to establish one.
    /// </summary>
    [HttpPost("link")]
    [Authorize(Policy = AuthPolicyNames.EntraOnly)]
    public async Task<IActionResult> Link(
        [FromBody] EntraLinkRequest request,
        CancellationToken cancellationToken)
    {
        var (entraOid, entraEmail) = GetValidatedEntraIdentity();
        if (entraOid is null)
            return BadRequest("The Entra token did not contain the expected identity claims.");

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Email and password are required.");

        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized(new { Message = "Invalid email or password." });

        // Check if this OID is already linked
        var oidString = entraOid.Value.ToString();
        var existingOwner = await _userManager.FindByLoginAsync("Microsoft", oidString);
        if (existingOwner is not null)
        {
            if (existingOwner.Id == user.Id)
                return Ok(new { Message = "This Microsoft account is already linked to your account." });

            return Conflict(new { Message = "This Microsoft account is already linked to a different local account." });
        }

        var loginInfo = new UserLoginInfo("Microsoft", oidString, entraEmail ?? request.Email);
        var result = await _userManager.AddLoginAsync(user, loginInfo);

        return result.Succeeded
            ? Ok(new { Message = "Microsoft account linked successfully." })
            : BadRequest(new { Errors = result.Errors.Select(e => e.Description) });
    }

    /// <summary>
    /// Reads the Entra OID and email off the validated JWT claims attached by the "Entra"
    /// authentication scheme — mirrors <c>MeController.Get()</c>'s claim reading. Never reads
    /// from client input; that's the entire point of gating both actions on
    /// <see cref="AuthPolicyNames.EntraOnly"/>.
    /// </summary>
    private (Guid? Oid, string? Email) GetValidatedEntraIdentity()
    {
        var oidStr = User.FindFirst("oid")?.Value
                     ?? User.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value;
        var email = User.FindFirst("preferred_username")?.Value
                    ?? User.FindFirst("email")?.Value
                    ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

        return Guid.TryParse(oidStr, out var oid) ? (oid, email) : (null, email);
    }
}

/// <summary>Only carries what the server can't determine itself — the OID/email come from the
/// validated Entra token, not the body.</summary>
public record EntraRegisterRequest(string DisplayName);

public record EntraRegisterResult(Guid UserId, string Email);

/// <summary>Identifies the target local account to link; ownership is proven by <see cref="Password"/>,
/// checked server-side. The Entra identity being linked comes from the caller's validated token,
/// not from this body.</summary>
public record EntraLinkRequest(string Email, string Password);
