using Ben.Data.Common.Constants;
using Ben.Data.WebApi.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Deleting a case (item 183). SuperAdmin only, and the only place in the product where a case
/// can be deleted at all — groups close cases, they do not delete them.
/// </summary>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/cases/{caseId:guid}/purge")]
public sealed class AdminCasePurgeController : BenControllerBase
{
    private readonly CasePurge _purge;

    public AdminCasePurgeController(CasePurge purge) => _purge = purge;

    /// <summary>What deleting this case would destroy and what it would leave. Changes nothing.</summary>
    [HttpGet]
    public async Task<ActionResult<CasePurgePreview>> Preview(Guid caseId, CancellationToken ct)
    {
        var preview = await _purge.PreviewAsync(caseId, ct);
        return preview is null ? NotFound() : Ok(preview);
    }

    public sealed record PurgeCaseRequest(string ConfirmTitle);

    /// <summary>Deletes the case. Irreversible.</summary>
    /// <remarks>
    /// The server's own sentence travels back: a mistyped title and a database refusal are
    /// different problems with different answers, and one status code cannot tell them apart.
    /// </remarks>
    [HttpDelete]
    public async Task<ActionResult<CasePurgeResult>> Purge(
        Guid caseId, [FromBody] PurgeCaseRequest request, CancellationToken ct)
    {
        var (result, error) = await _purge.PurgeAsync(
            caseId, request.ConfirmTitle, GetCurrentUserIdOrThrow(), ct);

        return error is not null ? BadRequest(error) : Ok(result);
    }
}
