using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Setting or changing your own password.
/// </summary>
/// <remarks>
/// <para><b>Why "set" exists at all.</b> An account created through Entra sign-in has no password —
/// <c>EntraAuthController</c> calls <c>CreateAsync(user)</c> without one — and until this
/// controller existed there was no way to acquire one: no reset flow, no panel, nothing. Password
/// sign-in against such an account answers "Invalid email or password", which is true and
/// completely undiagnosable from the outside. Item 142: that is exactly what happened to Ben's own
/// production account, and it looked like sign-in being broken.</para>
///
/// <para><b>One endpoint, two proofs.</b> An account with a password must present it to change it
/// (<c>ChangePasswordAsync</c>); an account without one is already fully proven by the bearer
/// token that got the caller here — an Entra session — so <c>AddPasswordAsync</c> needs nothing
/// more. Asking an Entra-born account for its "current password" would be asking for a thing that
/// does not exist.</para>
///
/// <para>The status endpoint tells the panel which of those two forms to draw, and is also how
/// the panel explains itself: "your account signs in with Microsoft; add a password to also sign
/// in directly."</para>
/// </remarks>
[ApiController]
[Route("api/me/password")]
[Authorize]
public sealed class MyPasswordController : BenControllerBase
{
    private readonly UserManager<AppUser> _userManager;

    public MyPasswordController(UserManager<AppUser> userManager)
    {
        _userManager = userManager;
    }

    public sealed record PasswordStatus(bool HasPassword);

    public sealed record SetPasswordRequest(string? CurrentPassword, string NewPassword);

    /// <summary>Whether this account has a password at all.</summary>
    [HttpGet]
    public async Task<ActionResult<PasswordStatus>> GetStatus(CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(GetCurrentUserIdOrThrow().ToString());
        if (user is null) return Unauthorized();

        return Ok(new PasswordStatus(await _userManager.HasPasswordAsync(user)));
    }

    /// <summary>Sets a first password, or changes the existing one.</summary>
    [HttpPost]
    public async Task<IActionResult> Set([FromBody] SetPasswordRequest request, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(GetCurrentUserIdOrThrow().ToString());
        if (user is null) return Unauthorized();

        IdentityResult result;

        if (await _userManager.HasPasswordAsync(user))
        {
            // The bearer token proves the session; the current password proves the person at the
            // keyboard is not just borrowing an unlocked one. Standard change-password shape.
            if (string.IsNullOrEmpty(request.CurrentPassword))
                return BadRequest("Enter your current password to change it.");

            result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        }
        else
        {
            result = await _userManager.AddPasswordAsync(user, request.NewPassword);
        }

        if (!result.Succeeded)
        {
            // Identity's descriptions are already sentences ("Passwords must have at least one
            // digit…", "Incorrect password.") — pass them through rather than flattening them
            // into a generic failure the person cannot act on.
            return BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        return NoContent();
    }
}
