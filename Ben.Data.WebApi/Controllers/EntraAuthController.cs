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
// Link takes a PASSWORD, so this belongs on the same limit /login carries. Without it these fell
// under the global 600-a-minute ceiling instead of 20 — thirty times the guesses, at the one door
// that also was not counting them toward a lockout.
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Ben.Data.WebApi.Services.RateLimiting.AuthPolicy)]
public sealed class EntraAuthController : BenControllerBase
{
    private const string Provider = "Microsoft";
    private readonly UserManager<AppUser> _userManager;
    private readonly Ben.Data.WebApi.Services.ExternalSignInService _external;

    public EntraAuthController(
        UserManager<AppUser> userManager,
        Ben.Data.WebApi.Services.ExternalSignInService external)
    {
        _userManager = userManager;
        _external    = external;
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

        // Microsoft's address claim is NOT treated as verified: for a personal account it may not
        // be, and the service auto-links only a verified address. So an existing address routes to
        // the link door rather than being joined on a claim - which is what this endpoint always
        // did, now for the reason written down once in ExternalSignInService.
        var identity = new Ben.Data.WebApi.Services.ExternalIdentity(
            Provider, entraOid.Value.ToString(), entraEmail, EmailVerified: false);

        switch (await _external.ResolveAsync(identity, cancellationToken))
        {
            case Ben.Data.WebApi.Services.ResolveResult.Found found:
                // A duplicate register attempt for an identity already linked: answer with that
                // account rather than complaining.
                return Ok(new EntraRegisterResult(found.User.Id, found.User.Email ?? string.Empty));

            case Ben.Data.WebApi.Services.ResolveResult.Refused:
                return Unauthorized("That sign-in couldn't be completed.");

            case Ben.Data.WebApi.Services.ResolveResult.Unknown unknown when unknown.ShouldLinkInstead:
                return Conflict(new { Message = "An account with this email already exists. Please use the 'Link existing account' option." });
        }

        // Handle null: this flow has no way to ask for one, so one is allocated - here rather than
        // left for the restart backfill, because an account with no @name cannot be mentioned and
        // is invisible to the feed until that job next runs.
        switch (await _external.RegisterAsync(identity, request.DisplayName, handle: null, cancellationToken))
        {
            case Ben.Data.WebApi.Services.RegisterResult.Created created:
                return Ok(new EntraRegisterResult(created.User.Id, created.User.Email ?? string.Empty));

            case Ben.Data.WebApi.Services.RegisterResult.NeedsProfile:
                return BadRequest("Display name is required.");

            case Ben.Data.WebApi.Services.RegisterResult.AddressTaken:
                return Conflict(new { Message = "An account with this email already exists. Please use the 'Link existing account' option." });

            case Ben.Data.WebApi.Services.RegisterResult.Failed failed:
                return BadRequest(new { Errors = new[] { failed.Reason } });

            default:
                return BadRequest("That sign-in couldn't be completed.");
        }
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

        var identity = new Ben.Data.WebApi.Services.ExternalIdentity(
            Provider, entraOid.Value.ToString(), entraEmail, EmailVerified: false);

        switch (await _external.LinkAsync(
            identity, request.Email, request.Password,
            request.TwoFactorCode, request.TwoFactorRecoveryCode, cancellationToken))
        {
            case Ben.Data.WebApi.Services.LinkResult.Linked linked:
                return Ok(new
                {
                    Message = linked.AlreadyWas
                        ? "This Microsoft account is already linked to your account."
                        : "Microsoft account linked successfully.",
                });

            case Ben.Data.WebApi.Services.LinkResult.Refused refused:
                // The SAME problem-detail shape /login and the Apple link answer with, carrying
                // Identity's own word. Until today this door answered in its own shape, and two
                // clients mapped the same four refusals twice.
                return Problem(detail: refused.Why.ToString(), statusCode: StatusCodes.Status401Unauthorized);

            case Ben.Data.WebApi.Services.LinkResult.HeldByAnotherAccount:
                return Conflict(new { Message = "This Microsoft account is already linked to a different local account." });

            case Ben.Data.WebApi.Services.LinkResult.Failed failed:
                return BadRequest(new { Errors = new[] { failed.Reason } });

            default:
                return BadRequest("That account couldn't be linked.");
        }
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
public record EntraLinkRequest(
    string Email,
    string Password,
    string? TwoFactorCode = null,
    string? TwoFactorRecoveryCode = null);


