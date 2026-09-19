using Ben.Data.Common;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Encodings.Web;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Turning two-factor authentication on and off for your own account.
/// </summary>
/// <remarks>
/// <para><b>Standard TOTP</b> (RFC 6238), which is what Identity implements and what every
/// authenticator app speaks — Duo Mobile, Okta Verify, Google Authenticator, Microsoft
/// Authenticator, 1Password, Authy. They all scan the same QR code. What this is <i>not</i> is
/// Duo's push-approval product or Okta as a single-sign-on provider: both are separate
/// integrations against their own APIs, and Okta as an identity provider would sit beside the
/// existing Entra OIDC path rather than here.</para>
///
/// <para><b>Sign-in needs no new endpoint.</b> <c>MapIdentityApi</c>'s <c>/login</c> already
/// accepts <c>twoFactorCode</c> and <c>twoFactorRecoveryCode</c>, and answers 401 with
/// <c>RequiresTwoFactor</c> when a password alone is not enough. The site's sign-in page reads that
/// and asks for the code.</para>
///
/// <para><b>What was here before:</b> an administrator checkbox that set
/// <c>AppUser.TwoFactorEnabled</c> directly, with no enrolment behind it — so ticking it locked the
/// account out rather than securing it, because there was no authenticator key to produce a code
/// from. This is the enrolment that flag always implied.</para>
/// </remarks>
[ApiController]
[Route("api/me/2fa")]
[Authorize]
public sealed class MyTwoFactorController : BenControllerBase
{
    /// <summary>How many recovery codes to issue. Identity's own default, and enough to print.</summary>
    private const int RecoveryCodeCount = 10;

    private readonly UserManager<AppUser> _userManager;
    private readonly SiteIdentity _site;

    public MyTwoFactorController(UserManager<AppUser> userManager, IOptions<SiteIdentity> site)
    {
        _userManager = userManager;
        _site = site.Value;
    }

    /// <summary>Where this account stands.</summary>
    [HttpGet]
    public async Task<ActionResult<TwoFactorStatusResponse>> GetStatus()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        var enabled = await _userManager.GetTwoFactorEnabledAsync(user);

        // Both of the extra fields are reported as false/zero unless 2FA is actually on. Identity
        // keeps a key and unspent recovery codes behind after it is switched off — ResetAuthenticator
        // replaces the secret rather than removing it — and a page that read those directly would
        // tell somebody who had just turned 2FA off that they still had an authenticator set up and
        // nine recovery codes. Neither would do them any good.
        return Ok(new TwoFactorStatusResponse(
            enabled,
            enabled && await _userManager.GetAuthenticatorKeyAsync(user) is { Length: > 0 },
            enabled ? await _userManager.CountRecoveryCodesAsync(user) : 0));
    }

    /// <summary>
    /// Starts enrolment: returns the shared key and the URI an authenticator app scans.
    /// </summary>
    /// <remarks>
    /// <para>Nothing is enabled here. This hands over a secret and waits to be shown a code
    /// generated from it, which is the only proof that the app actually holds it — enabling first
    /// and verifying later is how somebody locks themselves out with a mistyped setup.</para>
    ///
    /// <para>The key is reset every time enrolment is started afresh, so an abandoned attempt
    /// leaves no half-configured secret that a stale QR code from a screenshot could still satisfy.
    /// Restarting therefore invalidates the previous QR, which is the safe direction.</para>
    /// </remarks>
    [HttpPost("setup")]
    public async Task<ActionResult<TwoFactorSetupResponse>> BeginSetup()
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        if (await _userManager.GetTwoFactorEnabledAsync(user))
            return BadRequest("Two-factor authentication is already on for this account.");

        await _userManager.ResetAuthenticatorKeyAsync(user);
        var key = await _userManager.GetAuthenticatorKeyAsync(user);

        if (string.IsNullOrEmpty(key))
            return StatusCode(500, "Could not create an authenticator key.");

        return Ok(new TwoFactorSetupResponse(FormatKey(key), BuildAuthenticatorUri(user.Email ?? "account", key)));
    }

    /// <summary>
    /// Finishes enrolment: verifies a code from the app, turns 2FA on, and issues recovery codes.
    /// </summary>
    /// <remarks>
    /// The recovery codes are returned <b>once</b> and never again — they are stored hashed, so the
    /// server genuinely cannot show them a second time. The page that receives them has to say so.
    /// </remarks>
    [HttpPost("enable")]
    public async Task<ActionResult<TwoFactorEnabledResponse>> Enable([FromBody] TwoFactorCodeRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        if (await _userManager.GetTwoFactorEnabledAsync(user))
            return BadRequest("Two-factor authentication is already on for this account.");

        if (!await VerifyAsync(user, request.Code))
            return BadRequest("That code was not right. Check the app and try the current code.");

        await _userManager.SetTwoFactorEnabledAsync(user, true);

        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);

        return Ok(new TwoFactorEnabledResponse(codes?.ToArray() ?? []));
    }

    /// <summary>
    /// Turns 2FA off, and clears the authenticator key with it.
    /// </summary>
    /// <remarks>
    /// A current code is required. Without it, anybody who reached an unlocked browser could remove
    /// the second factor — which would make the whole feature decorative.
    /// </remarks>
    [HttpPost("disable")]
    public async Task<IActionResult> Disable([FromBody] TwoFactorCodeRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
            return NoContent();   // already off; saying so twice changes nothing

        if (!await VerifyAsync(user, request.Code))
            return BadRequest("That code was not right.");

        await _userManager.SetTwoFactorEnabledAsync(user, false);

        // Rotated as well as switched off. ResetAuthenticatorKey replaces the secret rather than
        // removing it, which is what we want: an authenticator app somebody still has installed
        // stops working immediately, and turning 2FA back on issues a fresh QR rather than
        // silently accepting the old entry.
        await _userManager.ResetAuthenticatorKeyAsync(user);

        // Recovery codes go too. They are a way past the second factor, and leaving nine of them
        // valid after the factor is gone means a set of printed codes that outlive the thing they
        // were a backup for.
        await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);

        return NoContent();
    }

    /// <summary>Issues a fresh set of recovery codes, invalidating the old ones.</summary>
    [HttpPost("recovery-codes")]
    public async Task<ActionResult<TwoFactorEnabledResponse>> RegenerateRecoveryCodes(
        [FromBody] TwoFactorCodeRequest request)
    {
        var user = await CurrentUserAsync();
        if (user is null) return Unauthorized();

        if (!await _userManager.GetTwoFactorEnabledAsync(user))
            return BadRequest("Two-factor authentication is not on for this account.");

        if (!await VerifyAsync(user, request.Code))
            return BadRequest("That code was not right.");

        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, RecoveryCodeCount);

        return Ok(new TwoFactorEnabledResponse(codes?.ToArray() ?? []));
    }

    // ── Shared ───────────────────────────────────────────────────────────────

    private async Task<AppUser?> CurrentUserAsync()
    {
        var userId = GetCurrentUserIdOrNull();
        return userId is null ? null : await _userManager.FindByIdAsync(userId.Value.ToString());
    }

    /// <summary>
    /// Checks a code from the authenticator app.
    /// </summary>
    /// <remarks>
    /// Spaces and hyphens are stripped because people read a code off a screen in two groups of
    /// three and type it the way they read it. Rejecting "123 456" would be a rule about
    /// typography rather than about security.
    /// </remarks>
    private Task<bool> VerifyAsync(AppUser user, string? code)
    {
        var cleaned = (code ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);

        return _userManager.VerifyTwoFactorTokenAsync(
            user, _userManager.Options.Tokens.AuthenticatorTokenProvider, cleaned);
    }

    /// <summary>
    /// The <c>otpauth://</c> URI an authenticator app scans.
    /// </summary>
    /// <remarks>
    /// The issuer appears twice on purpose — once as a prefix on the label and once as its own
    /// parameter. Older apps read only the label; newer ones read the parameter. Both are needed
    /// for the entry to be named after this site rather than appearing as a bare email address in
    /// a list of six identical-looking entries.
    /// </remarks>
    private string BuildAuthenticatorUri(string email, string unformattedKey)
    {
        var issuer = UrlEncoder.Default.Encode(_site.Name);
        var account = UrlEncoder.Default.Encode(email);

        return $"otpauth://totp/{issuer}:{account}?secret={unformattedKey}&issuer={issuer}&digits=6";
    }

    /// <summary>
    /// The key in groups of four, for the people typing it in by hand.
    /// </summary>
    /// <remarks>
    /// Every enrolment page offers a manual option, because a QR code is useless when the
    /// authenticator is on the same screen as the code — a desktop password manager, for one.
    /// </remarks>
    private static string FormatKey(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
        }

        return builder.ToString().Trim().ToLowerInvariant();
    }
}

/// <summary>Where an account stands. <c>RecoveryCodesRemaining</c> counts single-use codes not yet spent.</summary>
public sealed record TwoFactorStatusResponse(bool Enabled, bool HasAuthenticatorKey, int RecoveryCodesRemaining);

/// <summary>
/// Enrolment material: <c>SharedKey</c> formatted for typing in by hand, and <c>AuthenticatorUri</c>
/// — the <c>otpauth://</c> URI to render as a QR code.
/// </summary>
public sealed record TwoFactorSetupResponse(string SharedKey, string AuthenticatorUri);

public sealed record TwoFactorCodeRequest(string? Code);

/// <summary>Shown once: the codes are stored hashed and cannot be retrieved again.</summary>
public sealed record TwoFactorEnabledResponse(string[] RecoveryCodes);
