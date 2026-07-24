using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

[Route("api/organizations/{orgId:guid}/settings")]
public sealed class OrganizationSettingsController : OrgCmsControllerBase
{
    private readonly IAuditLogService _auditLog;

    public OrganizationSettingsController(IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security, IAuditLogService auditLog)
        : base(dbFactory, mapper, security) { _auditLog = auditLog; }

    [HttpGet]
    public async Task<ActionResult<OrgSettingsResponse>> Get(Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return NotFound();
        return Ok(new OrgSettingsResponse(org.ShowAddressMap, org.ShowAddressDirections));
    }

    [HttpPut]
    public async Task<ActionResult<OrgSettingsResponse>> Update(Guid orgId, [FromBody] OrgSettingsRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var before = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (before is null) return NotFound();
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == orgId, ct);
        org!.ShowAddressMap       = request.ShowAddressMap;
        org.ShowAddressDirections = request.ShowAddressDirections;
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(Organization), orgId, before, org, userId.Value, AppSources.WebApi, ct));
        return Ok(new OrgSettingsResponse(org.ShowAddressMap, org.ShowAddressDirections));
    }
}

public sealed record OrgSettingsResponse(bool ShowAddressMap, bool ShowAddressDirections);
public sealed record OrgSettingsRequest(bool ShowAddressMap, bool ShowAddressDirections);
