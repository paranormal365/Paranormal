using Ben.Data.Common.Constants;
using Ben.Data.WebApi.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Deleting a person. SuperAdmin, and irreversible.
/// </summary>
/// <remarks>
/// <para><b>Why it is here and not on the users controller.</b> The same reasoning that put the
/// group purge on its own route: an administrator tidying up a list should not be able to destroy
/// somebody's history by clicking the wrong row. This is the named exception, reached on purpose.
/// </para>
///
/// <para><b>Preview first, and the preview is the honest part.</b> It says which records will be
/// destroyed, which will be kept with the name stripped out, and — before anything is pressed —
/// whether the account row itself will actually disappear or merely be emptied. See
/// <see cref="AppUserPurge"/> for why those are different outcomes.</para>
///
/// <para><b>Typing the name is the confirmation.</b> Not as security — the caller is already the
/// most trusted account on the site — but because it is the one confirmation that cannot be
/// clicked through out of habit.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/users/{userId:guid}/purge")]
public sealed class AdminAppUserPurgeController : BenControllerBase
{
    private readonly AppUserPurge _purge;

    public AdminAppUserPurgeController(AppUserPurge purge) => _purge = purge;

    /// <summary>What deleting this person would destroy, and what it would leave. Changes nothing.</summary>
    [HttpGet]
    public async Task<ActionResult<AppUserPurgePreview>> Preview(Guid userId, CancellationToken ct)
    {
        var preview = await _purge.PreviewAsync(userId, ct);
        return preview is null ? NotFound() : Ok(preview);
    }

    /// <param name="ConfirmName">The person's exact display name, typed back.</param>
    public sealed record PurgeUserRequest(string ConfirmName);

    /// <summary>Destroys what is only theirs and strips the person out of what stays.</summary>
    [HttpDelete]
    public async Task<ActionResult<AppUserPurgeResult>> Purge(
        Guid userId, [FromBody] PurgeUserRequest request, CancellationToken ct)
    {
        var (result, error) = await _purge.PurgeAsync(
            userId, request.ConfirmName, GetCurrentUserIdOrThrow(), ct);

        // The server's own sentence travels: "you are the last SuperAdmin", "type the name
        // exactly" and "delete your own account from your profile" are three different problems
        // with three different answers, and one status code cannot tell them apart.
        return error is not null ? BadRequest(error) : Ok(result);
    }
}
