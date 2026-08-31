using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Ben.Data.Common.Constants;
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
        var email = string.IsNullOrWhiteSpace(identity.Email)
            ? $"{identity.Subject}@appleid.invalid"
            : identity.Email;

        var user = new AppUser
        {
            Id                 = Guid.NewGuid(),
            Email              = email,
            UserName           = email,
            NormalizedEmail    = email.ToUpperInvariant(),
            NormalizedUserName = email.ToUpperInvariant(),
            DisplayName        = displayName,
            Handle             = UserHandle.Normalize(request.Handle),
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
    /// Writes the same bearer-token body <c>/login</c> writes, through Identity's own handler.
    /// </summary>
    private async Task<IActionResult> IssueTokenAsync(AppUser user)
    {
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
