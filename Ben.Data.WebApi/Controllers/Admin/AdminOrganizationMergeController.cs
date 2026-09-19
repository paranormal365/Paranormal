using Ben.Data.Common.Constants;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Merging one organization into another (item 110). Preview first, always: the merge is a
/// destructive admin action on other people's records, so what it WOULD do is a separate,
/// mutation-free call the screen shows before asking for the final confirmation.
/// </summary>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/organization-merge")]
public sealed class AdminOrganizationMergeController : BenControllerBase
{
    private readonly OrganizationMergeService _merge;
    private readonly IAuditLogService _auditLog;

    public AdminOrganizationMergeController(OrganizationMergeService merge, IAuditLogService auditLog)
    {
        _merge = merge;
        _auditLog = auditLog;
    }

    [HttpGet("preview")]
    public async Task<ActionResult<Ben.Service.Models.Admin.MergePreview>> Preview(
        [FromQuery] Guid baseId, [FromQuery] Guid mergedId, CancellationToken ct)
    {
        var (preview, error) = await _merge.PreviewAsync(baseId, mergedId, ct);
        return preview is null ? BadRequest(error) : Ok(preview);
    }

    [HttpPost]
    public async Task<IActionResult> Merge(
        [FromBody] Ben.Service.Models.Admin.OrganizationMergeRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        var error = await _merge.MergeAsync(
            request.BaseOrganizationId, request.MergedOrganizationId, request.NewName, userId, ct);
        if (error is not null) return BadRequest(error);

        // The merged org row is gone, so the audit entry is the durable record of what happened.
        await _auditLog.LogCreateAsync("OrganizationMerge", request.BaseOrganizationId,
            new { request.BaseOrganizationId, request.MergedOrganizationId, request.NewName },
            userId, AppSources.WebApi);
        return NoContent();
    }
}
