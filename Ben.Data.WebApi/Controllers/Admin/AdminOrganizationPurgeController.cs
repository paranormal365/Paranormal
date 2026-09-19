using Ben.Data.Common.Constants;
using Ben.Data.WebApi.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Deleting a group and everything belonging to it. SuperAdmin, and irreversible.
/// </summary>
/// <remarks>
/// <para><b>Separate from the ordinary delete, deliberately.</b>
/// <c>OrganizationController.Delete</c> refuses a group that still has cases, files or events,
/// and that refusal is right: an administrator tidying up should not be able to destroy real work
/// by accident. This endpoint is the named exception, and it is separated so that nobody reaches
/// it while meaning the other one.</para>
///
/// <para><b>Preview first is not a suggestion.</b> The counts are the only thing standing between
/// a SuperAdmin and somebody's history, and they are what the confirmation is read against. The
/// delete then requires the group's NAME typed back — not as security, since the caller is
/// already trusted, but because typing a name is the one confirmation that cannot be clicked
/// through out of habit.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/organizations/{organizationId:guid}/purge")]
public sealed class AdminOrganizationPurgeController : BenControllerBase
{
    private readonly OrganizationPurge _purge;

    public AdminOrganizationPurgeController(OrganizationPurge purge) => _purge = purge;

    /// <summary>What deleting this group would destroy. Changes nothing.</summary>
    [HttpGet]
    public async Task<ActionResult<OrganizationPurgePreview>> Preview(
        Guid organizationId, CancellationToken ct)
    {
        var preview = await _purge.PreviewAsync(organizationId, ct);
        return preview is null ? NotFound() : Ok(preview);
    }

    /// <param name="ConfirmName">The group's exact name, typed back.</param>
    public sealed record PurgeRequest(string ConfirmName);

    /// <summary>Deletes the group and everything belonging to it.</summary>
    [HttpDelete]
    public async Task<ActionResult<OrganizationPurgePreview>> Purge(
        Guid organizationId, [FromBody] PurgeRequest request, CancellationToken ct)
    {
        var (removed, error) = await _purge.PurgeAsync(
            organizationId, request.ConfirmName, GetCurrentUserIdOrThrow(), ct);

        // The reason is the whole value of the refusal here — a mistyped name and a database
        // constraint are very different problems, and both arrive as this one status.
        return error is not null ? BadRequest(error) : Ok(removed);
    }
}
