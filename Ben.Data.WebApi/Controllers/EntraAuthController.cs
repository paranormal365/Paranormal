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
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly Ben.Data.WebApi.Services.UserHandleService _handles;

    public EntraAuthController(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        Ben.Data.WebApi.Services.UserHandleService handles)
    {
        _userManager   = userManager;
        _signInManager = signInManager;
        _handles       = handles;
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

        // One answer for "no such account" and "wrong password", in the same shape, so this cannot
        // be used to ask whether an address has an account here.
        if (user is null)
            return Unauthorized(new EntraLinkRefusal("Invalid email or password."));

        // CheckPasswordSignInAsync rather than CheckPasswordAsync. The bare check verifies the
        // password and NOTHING else: it does not count a failure toward a lockout, and it does not
        // ask whether the account may sign in at all. This endpoint takes a password from a caller
        // holding nothing but a Microsoft account, which anyone can create in a minute, so a door
        // where guesses are both unthrottled and uncounted is the weakest one in the building.
        var passwordCheck = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        if (passwordCheck.IsLockedOut)
            return Unauthorized(new EntraLinkRefusal(
                "That account is locked after too many attempts. Waiting is the only thing that helps."));

        // An unconfirmed account cannot sign in, so it must not be linkable either — linking would
        // hand somebody a way in that the confirmation requirement exists to withhold.
        if (passwordCheck.IsNotAllowed)
            return Unauthorized(new EntraLinkRefusal(
                "That account's email address hasn't been confirmed yet. Use the link we sent, or ask for another."));

        if (!passwordCheck.Succeeded)
            return Unauthorized(new EntraLinkRefusal("Invalid email or password."));

        // THE SECOND FACTOR, and this is the case that matters most here. Linking attaches a
        // Microsoft identity permanently, and afterwards that identity signs in on its own — the
        // password is never asked for again, and neither is the code. So a link granted on a
        // password alone does not merely skip two-factor once; it removes it for good, for whoever
        // holds the Microsoft account. CheckPasswordSignInAsync never reports this: that is
        // PasswordSignInAsync's job, and this endpoint must not create a cookie sign-in.
        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            var verified = await VerifySecondFactorAsync(user, request.TwoFactorCode, request.TwoFactorRecoveryCode);
            if (!verified)
                return Unauthorized(new EntraLinkRefusal(
                    "That account uses two-step verification. Enter the code from your authenticator app.",
                    RequiresTwoFactor: true));
        }

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
    /// Checks a second factor without a sign-in context, which is what an unauthenticated-by-us
    /// endpoint has to do.
    /// </summary>
    /// <remarks>
    /// A recovery code is redeemed, not merely checked — one that survived being used would not be
    /// a recovery code. The two are kept apart rather than guessed at by shape, because guessing
    /// wrong spends a recovery code on a mistyped app code.
    /// </remarks>
    private async Task<bool> VerifySecondFactorAsync(AppUser user, string? code, string? recoveryCode)
    {
        // Spaces and hyphens are how these are printed and read aloud, and the rest of the site
        // strips them.
        static string Clean(string value) =>
            value.Replace(" ", string.Empty).Replace("-", string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(recoveryCode))
            return await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, Clean(recoveryCode)) is { Succeeded: true };

        if (string.IsNullOrWhiteSpace(code))
            return false;

        return await _userManager.VerifyTwoFactorTokenAsync(
            user, TokenOptions.DefaultAuthenticatorProvider, Clean(code));
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

/// <summary>Why a link was refused.</summary>
/// <param name="RequiresTwoFactor">
/// The password was right and a code is needed. NOT a failure to report as one, and the reason this
/// carries a flag rather than only a sentence: a client has to know to show a code box.
/// </param>
public record EntraLinkRefusal(string Message, bool RequiresTwoFactor = false);
