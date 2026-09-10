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
    private readonly ExternalSignInService _external;
    private readonly IAppleIdentityTokenValidator _validator;
    private readonly IConfiguration _config;
    private readonly ILogger<AppleAuthController> _log;

    public AppleAuthController(
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        ExternalSignInService external,
        IAppleIdentityTokenValidator validator,
        IConfiguration config,
        ILogger<AppleAuthController> log)
    {
        _userManager   = userManager;
        _signInManager = signInManager;
        _external      = external;
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

        // Every decision from here is ExternalSignInService's, shared with Microsoft: resolve by
        // subject, join a VERIFIED address, refuse an account that may not sign in, or say what an
        // account still needs. This controller only turns those answers into HTTP.
        var external = Identity(identity);

        switch (await _external.ResolveAsync(external, ct))
        {
            case ResolveResult.Found found:
                return await IssueTokenAsync(found.User);

            case ResolveResult.Refused:
                return RefusedAccount();

            case ResolveResult.Failed failed:
                return BadRequest(failed.Reason);

            case ResolveResult.Unknown unknown when unknown.ShouldLinkInstead:
                // The (unverified) address already belongs to somebody. Route, do not create.
                return Conflict(NeedsProfile(identity, request.DisplayName, addressTaken: true));
        }

        // Nobody here yet. A handle is permanent, so it is asked for, never invented — which is why
        // the handle is passed through rather than null.
        switch (await _external.RegisterAsync(external, request.DisplayName, request.Handle ?? string.Empty, ct))
        {
            case RegisterResult.Created created:
                return await IssueTokenAsync(created.User);

            case RegisterResult.NeedsProfile needs:
                return Conflict(NeedsProfile(identity, request.DisplayName, handleProblem: needs.HandleProblem));

            case RegisterResult.AddressTaken:
                return Conflict(NeedsProfile(identity, request.DisplayName, addressTaken: true));

            case RegisterResult.Failed failed:
                return BadRequest(failed.Reason);

            default:
                return BadRequest("That sign-in couldn't be completed.");
        }
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

        switch (await _external.LinkAsync(
            Identity(identity), request.Email, request.Password,
            request.TwoFactorCode, request.TwoFactorRecoveryCode, ct))
        {
            case LinkResult.Linked linked:
                // Already on this account, or newly attached: either way, the person asked to sign
                // in, and an Apple identity token is not a credential the API accepts on ordinary
                // requests - so a session is issued here or they are left holding nothing.
                return await IssueTokenAsync(linked.User);

            case LinkResult.Refused refused:
                // The same problem-detail shape /login answers with, carrying Identity's own word,
                // so a client maps it with the same code rather than a second copy that could
                // disagree about what a 401 here means.
                return Problem(detail: refused.Why.ToString(), statusCode: StatusCodes.Status401Unauthorized);

            case LinkResult.HeldByAnotherAccount:
                return Conflict(new { Message = "That Apple account is already linked to a different account here." });

            case LinkResult.Failed failed:
                return BadRequest(failed.Reason);

            default:
                return BadRequest("That account couldn't be linked.");
        }
    }

    /// <summary>The provider-neutral statement of who Apple says this is.</summary>
    private static ExternalIdentity Identity(AppleIdentity identity) =>
        new(Provider, identity.Subject, identity.Email, identity.EmailVerified, identity.IsPrivateEmail);

    /// <summary>What the app is told when an account still needs something before it can exist.</summary>
    private static AppleNeedsProfileResponse NeedsProfile(
        AppleIdentity identity, string? displayName, string? handleProblem = null, bool addressTaken = false) =>
        new(
            NeedsProfile: true,
            SuggestedDisplayName: displayName?.Trim(),
            Email: identity.Email,
            IsPrivateEmail: identity.IsPrivateEmail,
            HandleProblem: handleProblem,
            ShouldLinkInstead: addressTaken,
            EmailProblem: addressTaken
                ? "That email address already has an account here. Sign in to it once and we'll join the two."
                : null);

    /// <summary>
    /// The one refusal for an account that may not sign in. Deliberately names nothing: telling a
    /// stranger an account is closed or locked tells them the address existed here.
    /// </summary>
    private IActionResult RefusedAccount() => Unauthorized("That sign-in couldn't be completed.");

    /// <summary>
    /// Writes the same bearer-token body <c>/login</c> writes, through Identity's own handler.
    /// </summary>
    private async Task<IActionResult> IssueTokenAsync(AppUser user)
    {
        // MAY THIS ACCOUNT SIGN IN AT ALL. Asked here because SignInAsync does not ask: it mints a
        // session unconditionally, and only PasswordSignInAsync runs the checks around it. So every
        // administrative refusal held for passwords and quietly did not hold for Apple.
        //
        // The gate is closure and lockout, and deliberately not the confirmed-account rule: see
        // ExternalSignInService.MaySignInAsync for why an account created from an unverified
        // address must still be able to come in through the provider that created it.
        if (!await _external.MaySignInAsync(user))
        {
            _log.LogWarning("Refused an Apple sign-in for {UserId}: the account may not sign in.", user.Id);
            return RefusedAccount();
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
/// <param name="ShouldLinkInstead">
/// The address already has an account here, so creating a second one is not the answer. The caller
/// should offer the link door rather than the create form.
/// </param>
/// <param name="EmailProblem">
/// What is wrong with the ADDRESS, kept apart from <paramref name="HandleProblem"/> because they
/// belong under different fields and putting one under the other tells somebody to fix the wrong
/// thing.
/// </param>
public sealed record AppleNeedsProfileResponse(
    bool NeedsProfile,
    string? SuggestedDisplayName,
    string? Email,
    bool IsPrivateEmail,
    string? HandleProblem = null,
    bool ShouldLinkInstead = false,
    string? EmailProblem = null);

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
