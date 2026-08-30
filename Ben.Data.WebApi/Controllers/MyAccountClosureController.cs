using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Deleting your own account.
/// </summary>
/// <remarks>
/// <para><b>Required, not optional.</b> App Review Guideline 5.1.1(v): an app that lets you create
/// an account must let you delete it from inside the app. A link to a web form does not satisfy
/// it, and the iOS submission is blocked without this.</para>
///
/// <para><b>Two endpoints, because a refusal has to be actionable.</b> The check is separate so the
/// app can say "you own Paranormal 365 — hand it over first" on the screen where the person is
/// standing, rather than only after they have typed a confirmation and pressed a destructive
/// button. See <see cref="AccountClosureService"/> for why an owner is refused at all.</para>
///
/// <para><b>Nothing here takes a user id.</b> Everything is scoped to the bearer token's own
/// account, so there is no "delete someone else" shape to get wrong — the same rule
/// <c>MyProfileController</c> follows.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me")]
public sealed class MyAccountClosureController : BenControllerBase
{
    private readonly AccountClosureService _closure;

    public MyAccountClosureController(AccountClosureService closure)
    {
        _closure = closure;
    }

    /// <summary>What stands in the way of closing this account, if anything.</summary>
    [HttpGet("closure")]
    public async Task<ActionResult<AccountClosureService.ClosureCheck>> GetClosureCheck(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        return Ok(await _closure.CheckAsync(userId, ct));
    }

    /// <summary>
    /// Closes the caller's account. Not reversible.
    /// </summary>
    /// <remarks>
    /// <para>The typed confirmation is required by the API, not only by the UI. This is the one
    /// endpoint where a mis-issued request destroys something nobody can restore, and a body that
    /// has to spell out a word cannot be sent by a stray retry, a prefetch, or a copied cURL line
    /// meant for a different route.</para>
    ///
    /// <para>DELETE with a body is unusual and deliberate — the alternative is putting the
    /// confirmation in a query string, and <c>?confirm=DELETE</c> lands in web-server logs.</para>
    /// </remarks>
    [HttpDelete]
    public async Task<IActionResult> Close([FromBody] CloseAccountRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (!string.Equals(request?.Confirmation?.Trim(), RequiredConfirmation, StringComparison.Ordinal))
            return BadRequest($"Type {RequiredConfirmation} to confirm.");

        var result = await _closure.CloseAsync(userId, ct);

        // The refusal is a sentence we wrote, which is what WebApiClient and the iOS client both
        // surface — a ProblemDetails blob would be dropped and the person would be told nothing.
        if (!result.Closed) return BadRequest(result.Refusal);

        return NoContent();
    }

    /// <summary>The word the caller has to type. Not localised — see the remarks on <see cref="Close"/>.</summary>
    public const string RequiredConfirmation = "DELETE";

    /// <param name="Confirmation">Must be exactly <see cref="RequiredConfirmation"/>.</param>
    public sealed record CloseAccountRequest(string? Confirmation);
}
