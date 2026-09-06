using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Carries a signed-in identity from the site to the standalone editor, without carrying tokens.
/// </summary>
/// <remarks>
/// <para><b>The problem.</b> The site holds a person's tokens in their Blazor circuit, on the
/// server. The standalone editor at <c>/editors/video/</c> runs in the browser and needs tokens of
/// its own. So somebody already signed in who follows the site's link to the editor arrives signed
/// out and is asked for their password a second time, at a second door — which is most of why that
/// link reads as a curiosity rather than as where the work happens (2026-09-05 audit, phase 12).</para>
///
/// <para><b>The shape.</b> The site asks this controller, as the signed-in person, for a code. The
/// code proves nothing by itself and lives sixty seconds. It travels in the link's <b>fragment</b>,
/// which browsers never send to a server, so it stays out of access logs, out of <c>Referer</c>,
/// and out of anything in between. The editor exchanges it here for bearer tokens minted for it by
/// Identity's own handler — the same body <c>/login</c> writes.</para>
///
/// <para><b>What is deliberately not done.</b> The site's own tokens are never relayed. A refresh
/// token in particular is a long-lived credential, and putting one into page script on another
/// origin would give away exactly what keeping tokens in the circuit was protecting.</para>
/// </remarks>
[ApiController]
[Route("api/auth/editor-handoff")]
// The same limit /login carries. The exchange is an unauthenticated door that mints sessions, and
// issuing is cheap to ask for but should not be free to spam.
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimiting.AuthPolicy)]
public sealed class EditorHandoffController : BenControllerBase
{
    private readonly EditorHandoffCodeStore _codes;
    private readonly UserManager<AppUser> _userManager;
    private readonly SignInManager<AppUser> _signInManager;
    private readonly ILogger<EditorHandoffController> _log;

    public EditorHandoffController(
        EditorHandoffCodeStore codes,
        UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager,
        ILogger<EditorHandoffController> log)
    {
        _codes         = codes;
        _userManager   = userManager;
        _signInManager = signInManager;
        _log           = log;
    }

    /// <summary>
    /// Issues a handoff code for the signed-in caller.
    /// </summary>
    /// <remarks>
    /// Authorized, so the code can only ever stand for the account that asked for it. There is
    /// nothing to pass in: an id in the body would be a request to be issued somebody else's
    /// session.
    /// </remarks>
    [HttpPost]
    [Authorize]
    public IActionResult Issue()
    {
        var userId = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();

        return Ok(new EditorHandoffCodeResponse(
            _codes.Issue(userId.Value),
            (int)EditorHandoffCodeStore.Lifetime.TotalSeconds));
    }

    /// <summary>
    /// Exchanges a handoff code for bearer tokens.
    /// </summary>
    /// <remarks>
    /// Anonymous by necessity — the caller has no session yet, which is the whole point. The code
    /// is the credential, it is good once, and it is good for a minute.
    /// </remarks>
    [HttpPost("exchange")]
    [AllowAnonymous]
    public async Task<IActionResult> ExchangeAsync([FromBody] EditorHandoffExchangeRequest request)
    {
        var userId = _codes.Redeem(request?.Code);

        // One message for unknown, used and expired alike. Telling them apart tells a guesser
        // which guesses are closer.
        if (userId is null)
            return Unauthorized("That link has already been used or has expired. Open the editor from the site again.");

        var user = await _userManager.FindByIdAsync(userId.Value.ToString());

        if (user is null)
        {
            // The account went away inside the minute the code was alive. Vanishingly rare, and
            // worth a line in the log if it ever happens.
            _log.LogWarning("A handoff code named an account that no longer exists.");
            return Unauthorized("That account is no longer available.");
        }

        // A closed or locked-out account cannot come in through this door either. CanSignInAsync
        // is what the password path consults, and RecordingSignInManager overrides it so a closed
        // account can never sign in again by any route.
        if (!await _signInManager.CanSignInAsync(user))
            return Unauthorized("That account cannot be signed in to.");

        return await IssueTokensAsync(user);
    }

    /// <summary>
    /// Writes the same bearer-token body <c>/login</c> writes, through Identity's own handler.
    /// </summary>
    /// <remarks>Mirrors <see cref="AppleAuthController"/>'s <c>IssueTokenAsync</c> exactly.</remarks>
    private async Task<IActionResult> IssueTokensAsync(AppUser user)
    {
        _signInManager.AuthenticationScheme = IdentityConstants.BearerScheme;
        await _signInManager.SignInAsync(user, isPersistent: false);

        // A handoff is a sign-in, so it is counted as one. Leaving it out would make the
        // dashboard's figures quietly wrong in the same way Apple's were before they were
        // recorded. Recording cannot fail the sign-in.
        if (_signInManager is RecordingSignInManager recorder)
            await recorder.RecordExternalSignInAsync(user.Id, RecordingSignInManager.HandoffMethod);

        // The handler has already written the response body; returning anything else would
        // append to it.
        return new EmptyResult();
    }
}

/// <summary>What the site gets back, and puts in the link's fragment.</summary>
/// <param name="Code">Good once, and only for <paramref name="ExpiresInSeconds"/>.</param>
public sealed record EditorHandoffCodeResponse(string Code, int ExpiresInSeconds);

/// <summary>What the standalone editor posts when it finds a code in its own URL.</summary>
public sealed record EditorHandoffExchangeRequest(string Code);
