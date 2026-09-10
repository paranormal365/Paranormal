using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Data.Common.Helpers;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Sign in with Apple, for the iPhone and iPad apps.
/// </summary>
/// <remarks>
/// <para>The app performs the Apple authorization itself and posts the resulting identity token
/// here. This endpoint validates that token against Apple's own published signing keys — it never
/// trusts a <c>sub</c> or an email supplied in the request body, for the same reason
/// <see cref="EntraAuthController"/> does not: a body-supplied identifier would let any caller
/// claim an identity it does not hold.</para>
///
/// <para><b>Linking a mismatched address</b> is what <c>link</c> is for: an Apple relay address, or
/// an Apple ID that is simply a different address from the one somebody signed up with, will never
/// match by email, and without that door they would end up with a second account holding none of
/// their history.</para>
///
/// <para><b>Three outcomes.</b> A known Apple identity signs in. An unknown Apple identity whose
/// Apple-verified email matches an existing account is linked to it and signs in — that is what
/// "verified email" means, and it is how somebody who signed up on the website later signs in on
/// their phone without a second account. An unknown identity with no matching account needs a
/// display name and a handle before an account can exist, so it comes back as <c>409</c> with
/// <c>NeedsProfile</c> rather than inventing a permanent handle on somebody's behalf.</para>
///
/// <para><b>What comes back on success</b> is exactly what <c>/login</c> returns — the bearer
/// token response written by Identity's own sign-in handler — so the app's existing token session
/// consumes it unchanged.</para>
/// </remarks>
[ApiController]
[Route("api/auth/apple")]
// The same limit /login carries. An unauthenticated door that mints sessions needs one.
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimiting.AuthPolicy)]
public sealed class AppleAuthController : BenControllerBase
{
    private const string Provider = "Apple";
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly UserHandleService _handles;
    private readonly IAppleIdentityTokenValidator _validator;
    private readonly IConfiguration _config;
    private readonly ILogger<AppleAuthController> _log;

    public AppleAuthController(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        UserHandleService handles,
        IAppleIdentityTokenValidator validator,
        IConfiguration config,
        ILogger<AppleAuthController> log)
    {
        _userManager   = userManager;
        _signInManager = signInManager;
        _handles       = handles;
        _validator     = validator;
        _config        = config;
        _log           = log;
    }

    [HttpPost]
    public async Task<IActionResult> SignIn([FromBody] AppleSignInRequest request, CancellationToken ct)
    {
        var audiences = _config.GetSection("Apple:ClientIds").Get<string[]>() ?? [];
        if (audiences.Length == 0)
        {
            // A misconfigured server must not fall back to "trust whatever arrives".
            _log.LogError("Sign in with Apple was called but Apple:ClientIds is not configured.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "Signing in with Apple isn't set up on this server yet.");
        }

        if (string.IsNullOrWhiteSpace(request.IdentityToken))
            return BadRequest("The Apple sign-in didn't complete. Try again.");

        AppleIdentity identity;
        try
        {
            identity = await _validator.ValidateAsync(request.IdentityToken, audiences, ct);
        }
        catch (SecurityTokenException ex)
        {
            _log.LogWarning(ex, "Rejected an Apple identity token.");
            return Unauthorized("That Apple sign-in couldn't be verified. Try again.");
        }
        catch (Exception ex)
        {
            // Apple's key endpoint being unreachable is not the caller's fault and is not a bad
            // token — saying "couldn't be verified" would send somebody off to fix their Apple ID.
            _log.LogError(ex, "Could not reach Apple to verify a sign-in.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "We couldn't reach Apple to check that sign-in. Try again in a moment.");
        }

        // 1. A returning Apple identity.
        var linked = await _userManager.FindByLoginAsync(Provider, identity.Subject);
        if (linked is not null)
            return await IssueTokenAsync(linked);

        // 2. An Apple identity whose verified email is already an account here.
        //    Only a VERIFIED email links — an unverified one proves nothing about ownership.
        if (identity.EmailVerified && !string.IsNullOrWhiteSpace(identity.Email))
        {
            var byEmail = await _userManager.FindByEmailAsync(identity.Email);
            if (byEmail is not null)
            {
                var link = await _userManager.AddLoginAsync(
                    byEmail, new UserLoginInfo(Provider, identity.Subject, identity.Email));
                if (!link.Succeeded)
                    return BadRequest(string.Join(" ", link.Errors.Select(e => e.Description)));

                // An Entra-born or website-born account that reaches us through Apple has now had
                // its address proved by Apple; leaving it unconfirmed would lock them out of the
                // website they can already use on the phone.
                if (!byEmail.EmailConfirmed)
                {
                    byEmail.EmailConfirmed = true;
                    await _userManager.UpdateAsync(byEmail);
                }

                return await IssueTokenAsync(byEmail);
            }
        }

        // 3. Nobody here yet. A handle is permanent, so it is asked for, never invented.
        var displayName = request.DisplayName?.Trim() ?? string.Empty;
        if (displayName.Length is < 2 or > 200 || string.IsNullOrWhiteSpace(request.Handle))
        {
            return Conflict(new AppleNeedsProfileResponse(
                NeedsProfile: true,
                SuggestedDisplayName: request.DisplayName?.Trim(),
                Email: identity.Email,
                IsPrivateEmail: identity.IsPrivateEmail));
        }

        var (handleFree, handleReason) = await _handles.IsAvailableAsync(request.Handle, ct);
        if (!handleFree)
            return Conflict(new AppleNeedsProfileResponse(
                NeedsProfile: true,
                SuggestedDisplayName: displayName,
                Email: identity.Email,
                IsPrivateEmail: identity.IsPrivateEmail,
                HandleProblem: handleReason ?? "Choose another name."));

        // Apple only hands over the email on the FIRST authorization, and a user may withhold it
        // entirely. A placeholder keeps Identity's uniqueness happy without ever pretending to be
        // a reachable address — MeController's own "no email" handling covers the rest.
        var withheld = string.IsNullOrWhiteSpace(identity.Email);
        var email = withheld ? $"{identity.Subject}@appleid.invalid" : identity.Email!;

        // Apple says which of the three this is exactly once, here. Recording it is the only way
        // anything later can tell a real address from a relay or a placeholder — and without that,
        // the site shows a machine-generated string as somebody's email and claims to have written
        // to them when Apple quietly dropped it.
        var emailKind =
            withheld                 ? EmailAddressKind.Unreachable
          : identity.IsPrivateEmail  ? EmailAddressKind.AppleRelay
          :                            EmailAddressKind.Ordinary;

        var user = new AppUser
        {
            Id                 = Guid.NewGuid(),
            Email              = email,
            UserName           = email,
            NormalizedEmail    = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            DisplayName        = displayName,
            Handle             = UserHandle.Normalize(request.Handle),
            EmailKind          = emailKind,
            // Apple verified it; there is no second confirmation to send, and no address to send
            // it to when the user withheld theirs.
            EmailConfirmed     = true,
            DateCreated        = DateTime.UtcNow,
        };

        var created = await _userManager.CreateAsync(user);
        if (!created.Succeeded)
        {
            var isHandleClash = created.Errors.Any(e =>
                e.Description.Contains("Handle", StringComparison.OrdinalIgnoreCase));
            return Conflict(new AppleNeedsProfileResponse(
                NeedsProfile: true,
                SuggestedDisplayName: displayName,
                Email: identity.Email,
                IsPrivateEmail: identity.IsPrivateEmail,
                HandleProblem: isHandleClash
                    ? "That name was taken a moment ago. Try another."
                    : string.Join(" ", created.Errors.Select(e => e.Description))));
        }

        var addLogin = await _userManager.AddLoginAsync(
            user, new UserLoginInfo(Provider, identity.Subject, identity.Email ?? email));
        if (!addLogin.Succeeded)
        {
            await _userManager.DeleteAsync(user);   // rollback: an account nothing can sign into
            return BadRequest(string.Join(" ", addLogin.Errors.Select(e => e.Description)));
        }

        return await IssueTokenAsync(user);
    }

    /// <summary>
    /// Links a verified Apple identity to an account that already exists here, and signs in.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists.</b> <see cref="SignIn"/> only joins an Apple identity to an
    /// existing account when Apple's own VERIFIED email happens to match one. Two very ordinary
    /// situations defeat that: somebody using Apple's Hide My Email relay, whose address here will
    /// never match anything, and somebody whose Apple ID is simply a different address from the one
    /// they signed up with. Both used to end at "create an account", which silently produced a
    /// SECOND account holding none of their cases, groups or history — and no way back.</para>
    ///
    /// <para><b>What proves what.</b> The Apple token proves they hold the Apple identity; the
    /// password proves they hold the account being claimed. Neither alone is enough, and that is
    /// the same bargain <see cref="EntraAuthController.Link"/> makes. Unlike that one, this issues
    /// a session: there is no standing session to fall back on, because an Apple identity token is
    /// not a credential this API accepts on ordinary requests.</para>
    /// </remarks>
    [HttpPost("link")]
    public async Task<IActionResult> Link([FromBody] AppleLinkRequest request, CancellationToken ct)
    {
        var audiences = _config.GetSection("Apple:ClientIds").Get<string[]>() ?? [];
        if (audiences.Length == 0)
        {
            _log.LogError("Sign in with Apple linking was called but Apple:ClientIds is not configured.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "Signing in with Apple isn't set up on this server yet.");
        }

        if (string.IsNullOrWhiteSpace(request.IdentityToken))
            return BadRequest("The Apple sign-in didn't complete. Try again.");

        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest("Email and password are required.");

        AppleIdentity identity;
        try
        {
            identity = await _validator.ValidateAsync(request.IdentityToken, audiences, ct);
        }
        catch (SecurityTokenException ex)
        {
            _log.LogWarning(ex, "Rejected an Apple identity token while linking.");
            return Unauthorized("That Apple sign-in couldn't be verified. Try again.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not reach Apple to verify a sign-in while linking.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "We couldn't reach Apple to check that sign-in. Try again in a moment.");
        }

        // Already linked somewhere. Answering before the password is checked is deliberate: it
        // reveals nothing about the account being named, and it stops a second account quietly
        // stealing an Apple identity that already belongs to a first.
        var existingOwner = await _userManager.FindByLoginAsync(Provider, identity.Subject);

        var user = await _userManager.FindByEmailAsync(request.Email);

        // One answer for "no such account" and "wrong password", as everywhere else — and the same
        // SHAPE too, not merely the same words. Answering a missing account with prose and a wrong
        // password with a problem-detail would tell them apart just as loudly, which turns this into
        // a way to ask whether any given address has an account here.
        if (user is null)
            return Problem(detail: "Failed", statusCode: StatusCodes.Status401Unauthorized);

        // CheckPasswordSignInAsync rather than CheckPasswordAsync: this endpoint takes a password
        // from an unauthenticated caller, so it must honour lockout, and a bare password check
        // does not. Without that, this would be the one door in the building where guesses are
        // free.
        var passwordCheck = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);

        // The refusals answer in the same shape /login uses — a problem-detail carrying Identity's
        // own word — so a client maps them with the same code rather than a second copy that could
        // disagree about what a 401 here means.
        if (passwordCheck.IsLockedOut)
            return Problem(detail: "LockedOut", statusCode: StatusCodes.Status401Unauthorized);

        // An unconfirmed address is NOT a wrong password, and saying so sends somebody to reset a
        // password that was always right. Three separate comments in this codebase exist because
        // that mistake has been made before.
        if (passwordCheck.IsNotAllowed)
            return Problem(detail: "NotAllowed", statusCode: StatusCodes.Status401Unauthorized);

        if (!passwordCheck.Succeeded)
            return Problem(detail: "Failed", statusCode: StatusCodes.Status401Unauthorized);

        // CheckPasswordSignInAsync verifies the PASSWORD. It never returns RequiresTwoFactor — that
        // is PasswordSignInAsync's job, and this endpoint cannot use it because it must not create
        // a cookie sign-in. So the second factor is checked here, explicitly. Without this, linking
        // would be a door into a two-factor account that needs only the password: a way around the
        // very protection its owner turned on.
        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            var verified = await VerifySecondFactorAsync(user, request.TwoFactorCode, request.TwoFactorRecoveryCode);
            if (!verified)
                return Problem(detail: "RequiresTwoFactor", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (existingOwner is not null)
        {
            // Already theirs: linking again is not an error, and the person asked to sign in.
            if (existingOwner.Id == user.Id)
                return await IssueTokenAsync(user);

            return Conflict(new { Message = "That Apple account is already linked to a different account here." });
        }

        var link = await _userManager.AddLoginAsync(
            user, new UserLoginInfo(Provider, identity.Subject, identity.Email ?? request.Email));

        if (!link.Succeeded)
            return BadRequest(string.Join(" ", link.Errors.Select(e => e.Description)));

        return await IssueTokenAsync(user);
    }

    /// <summary>
    /// Checks a second factor without a sign-in context, which is what an unauthenticated endpoint
    /// has to do.
    /// </summary>
    /// <remarks>
    /// <para>Returns false when nothing was supplied, which is how the caller is told to ask —
    /// the same 401 <c>/login</c> answers, so a client already knows what to do with it.</para>
    ///
    /// <para>A recovery code is redeemed, not merely checked: it is single use, and one that
    /// survived being used would not be a recovery code. The two are kept apart rather than
    /// guessed at by shape, because guessing wrong spends a recovery code on a mistyped app code.</para>
    /// </remarks>
    private async Task<bool> VerifySecondFactorAsync(AppUser user, string? code, string? recoveryCode)
    {
        // Spaces and hyphens are how these are printed and read aloud. The rest of the site strips
        // them; refusing a comfortably typed code would be a failure we caused.
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
    /// Writes the same bearer-token body <c>/login</c> writes, through Identity's own handler.
    /// </summary>
    private async Task<IActionResult> IssueTokenAsync(AppUser user)
    {
        // MAY THIS ACCOUNT SIGN IN AT ALL. Asked here because SignInAsync does not ask: it mints a
        // session unconditionally, and only PasswordSignInAsync runs the checks around it. So every
        // administrative refusal held for passwords and quietly did not hold for Apple.
        //
        // CanSignInAsync covers a CLOSED account (RecordingSignInManager's override) and the
        // confirmed-account requirement. Lockout is separate — Identity checks it alongside, not
        // inside — and lockout is the only lever an administrator has short of closing an account.
        // Missing either one makes that lever do nothing to anybody who has linked an Apple ID.
        if (await _userManager.IsLockedOutAsync(user) || !await _signInManager.CanSignInAsync(user))
        {
            _log.LogWarning("Refused an Apple sign-in for {UserId}: the account may not sign in.", user.Id);

            // Deliberately says nothing about why. Naming a closed or locked account tells a
            // stranger the address existed here, which is what every other refusal on this site
            // avoids.
            return Unauthorized("That sign-in couldn't be completed.");
        }

        _signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        await _signInManager.SignInAsync(user, isPersistent: false);

        // The one place an Apple session is minted, and so the one place to count it. Without
        // this the dashboard's sign-in figures were password-only and said nothing about it —
        // the Method column existed for a distinction that never arrived. Recording cannot fail
        // the sign-in: RecordExternalSignInAsync swallows its own errors, for the same reason the
        // password path does.
        if (_signInManager is Services.RecordingSignInManager recorder)
            await recorder.RecordExternalSignInAsync(user.Id, Services.RecordingSignInManager.AppleMethod);

        // The handler has already written the response body; returning anything else would
        // append to it.
        return new EmptyResult();
    }
}

/// <summary>What the app posts after Apple's own sheet finishes.</summary>
/// <param name="IdentityToken">Apple's signed JWT. The only thing here that is trusted.</param>
/// <param name="DisplayName">Only used when creating an account. Apple supplies the real name
/// once, on the first authorization, and never again — so the app has to pass it on.</param>
/// <param name="Handle">Only used when creating an account. Permanent, so it is asked for.</param>
public sealed record AppleSignInRequest(string IdentityToken, string? DisplayName, string? Handle);

/// <summary>Claiming an account that already exists here for a verified Apple identity.</summary>
/// <param name="IdentityToken">Apple's signed JWT, proving the Apple identity.</param>
/// <param name="Email">The account being claimed — NOT necessarily the address Apple gave.</param>
/// <param name="Password">Proof that the account being claimed belongs to the caller.</param>
/// <param name="TwoFactorCode">
/// A code from the authenticator app, when a previous attempt answered <c>RequiresTwoFactor</c>.
/// </param>
/// <param name="TwoFactorRecoveryCode">One of the printed recovery codes, instead of an app code.</param>
/// <remarks>
/// The two-factor fields work exactly as <c>/login</c>'s do, and for the same reason: an account
/// with a second factor turned on must not be joinable with a password alone, or linking becomes a
/// way around the protection its owner chose.
/// </remarks>
public sealed record AppleLinkRequest(
    string IdentityToken,
    string Email,
    string Password,
    string? TwoFactorCode = null,
    string? TwoFactorRecoveryCode = null);

/// <summary>Told to an app that must collect a name and handle before an account can exist.</summary>
public sealed record AppleNeedsProfileResponse(
    bool NeedsProfile,
    string? SuggestedDisplayName,
    string? Email,
    bool IsPrivateEmail,
    string? HandleProblem = null);

/// <summary>The bits of a validated Apple identity token this site acts on.</summary>
public sealed record AppleIdentity(string Subject, string? Email, bool EmailVerified, bool IsPrivateEmail);

public interface IAppleIdentityTokenValidator
{
    Task<AppleIdentity> ValidateAsync(string identityToken, IReadOnlyList<string> audiences, CancellationToken ct);
}

/// <summary>
/// Validates an Apple identity token against Apple's published signing keys.
/// </summary>
/// <remarks>
/// The keys are fetched through <see cref="ConfigurationManager{T}"/>, which caches them and
/// re-fetches on rotation. Hand-rolling that fetch would either pin a key Apple later retires or
/// hit Apple on every sign-in.
/// </remarks>
public sealed class AppleIdentityTokenValidator : IAppleIdentityTokenValidator
{
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration;

    public AppleIdentityTokenValidator(HttpClient http)
    {
        _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(
            "https://appleid.apple.com/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever(http) { RequireHttps = true });
    }

    public async Task<AppleIdentity> ValidateAsync(
        string identityToken, IReadOnlyList<string> audiences, CancellationToken ct)
    {
        var handler = new JwtSecurityTokenHandler();

        // Shape first, and locally: a token that is not a JWT at all is the caller's problem, and
        // fetching Apple's keys to check it against would be a round trip to learn nothing. It
        // also keeps the two failure kinds apart — see the catch below for why that matters.
        if (!handler.CanReadToken(identityToken))
            throw new SecurityTokenException("That was not a readable Apple token.");

        var config = await _configuration.GetConfigurationAsync(ct);

        ClaimsPrincipal result;
        try
        {
            result = handler.ValidateToken(identityToken, new TokenValidationParameters
            {
                ValidIssuer               = AppleAuthConstants.Issuer,
                ValidateIssuer            = true,
                ValidAudiences            = audiences,
                ValidateAudience          = true,
                IssuerSigningKeys         = config.SigningKeys,
                ValidateIssuerSigningKey  = true,
                ValidateLifetime          = true,
                ClockSkew                 = TimeSpan.FromMinutes(2),
            }, out _);
        }
        catch (ArgumentException ex)
        {
            // IdentityModel 8 moved SecurityTokenMalformedException under ArgumentException, so a
            // plain `catch (SecurityTokenException)` misses the commonest bad input there is. Left
            // uncaught it reached the controller's network branch, which told somebody holding a
            // junk token that APPLE was down — a refusal reported as somebody else's outage.
            throw new SecurityTokenException("That Apple token could not be read.", ex);
        }

        var subject = result.FindFirst(ClaimTypes.NameIdentifier)?.Value
                   ?? result.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject))
            throw new SecurityTokenException("The Apple token carried no subject.");

        var email = result.FindFirst(ClaimTypes.Email)?.Value ?? result.FindFirst("email")?.Value;

        // Apple writes these as the STRINGS "true"/"false", not JSON booleans.
        static bool Flag(ClaimsPrincipal p, string name) =>
            string.Equals(p.FindFirst(name)?.Value, "true", StringComparison.OrdinalIgnoreCase);

        return new AppleIdentity(subject, email, Flag(result, "email_verified"), Flag(result, "is_private_email"));
    }
}

internal static class AppleAuthConstants
{
    /// <summary>Apple's issuer value. Anything else signed by Apple's keys is not a sign-in.</summary>
    public const string Issuer = "https://appleid.apple.com";
}
