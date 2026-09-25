using System.Text;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Taking over an account somebody else made for you.
/// </summary>
/// <remarks>
/// <para><b>Why this is not the ordinary reset endpoint.</b> Identity's <c>/resetPassword</c>
/// refuses an address that has not been confirmed, and answers "invalid token" when it does — a
/// deliberate refusal to become an oracle for who has an account here. That rule is right for
/// somebody who forgot their password, and fatal for this case: an account made FOR somebody who
/// has never been here is unconfirmed by definition, so the one letter that has to work was the
/// one that never could. Its button failed every time, with a message that blamed the code.</para>
///
/// <para><b>Confirming the address is the consequence, not a favour.</b> The code was mailed to
/// that address and nowhere else, so somebody holding it has proved they read that inbox — which
/// is the whole of what confirmation means, and exactly the reasoning
/// <see cref="EmailLinkAccounts"/> already uses for an account made from a clicked link. Marking
/// it confirmed here therefore records something that has just been demonstrated rather than
/// something an administrator asserted.</para>
///
/// <para><b>It gives away nothing.</b> Every refusal is the same sentence whether the address is
/// unknown, the code is wrong, or the code has expired — so it is no more of an oracle than the
/// endpoint it sits beside, and it is rate limited on the same policy as signing in.</para>
/// </remarks>
[ApiController]
[Route("api/public/account-handover")]
[AllowAnonymous]
[EnableRateLimiting(RateLimiting.AuthPolicy)]
public sealed class PublicAccountHandoverController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly ILogger<PublicAccountHandoverController> _log;

    public PublicAccountHandoverController(
        UserManager<AppUser> users, ILogger<PublicAccountHandoverController> log)
    { _users = users; _log = log; }

    /// <summary>Sets the password on an account somebody was told about, and confirms the address.</summary>
    [HttpPost]
    public async Task<IActionResult> TakeOver(
        [FromBody] AccountHandoverRequest request, CancellationToken ct)
    {
        // One sentence for everything that can go wrong, so nothing here reports whether an
        // address is registered. The reader has the letter; if it does not work they ask for
        // another, and the wording says so.
        const string Refused =
            "That link is no longer valid. Ask whoever set the account up to send it again.";

        if (string.IsNullOrWhiteSpace(request?.Email)
         || string.IsNullOrWhiteSpace(request.Code)
         || string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(Refused);
        }

        var user = await _users.FindByEmailAsync(request.Email.Trim());
        if (user is null) return BadRequest(Refused);

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Code.Trim()));
        }
        catch (FormatException)
        {
            // A mail client that wrapped the link mangles the code rather than losing it, which
            // arrives here as a decode failure rather than a bad token.
            return BadRequest(Refused);
        }

        var result = await _users.ResetPasswordAsync(user, token, request.NewPassword);
        if (!result.Succeeded)
        {
            // The password policy's own sentences ARE worth showing — "too short" is something the
            // reader can act on, and saying it reveals nothing about whether the account exists,
            // because the code had to be right to get this far.
            var policy = result.Errors
                .Where(e => !string.Equals(e.Code, "InvalidToken", StringComparison.Ordinal))
                .Select(e => e.Description)
                .ToList();

            return BadRequest(policy.Count > 0 ? string.Join(" ", policy) : Refused);
        }

        // Proved by holding a code that went to this address and nowhere else. Done after the
        // reset, so an address is never confirmed by a request that then failed.
        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _users.UpdateAsync(user);
        }

        _log.LogInformation("Account {UserId} was taken over by its owner from a handover letter.", user.Id);
        return Ok();
    }
}

/// <summary>What the handover page posts.</summary>
/// <param name="Code">The code from the letter, base64url as it was sent.</param>
public sealed record AccountHandoverRequest(string? Email, string? Code, string? NewPassword);
