using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services;

namespace Ben.Data.WebApi.Controllers.Entities;

[Route("api/organizations/{orgId:guid}/settings")]
public sealed class OrganizationSettingsController : OrgCmsControllerBase
{
    private readonly IAuditLogService _auditLog;

    private readonly IAvMetadataStripper _avStripper;

    public OrganizationSettingsController(IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security, IAuditLogService auditLog, IAvMetadataStripper avStripper)
        : base(dbFactory, mapper, security) { _auditLog = auditLog; _avStripper = avStripper; }

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

        // The stripping toggle travels with WHY it is or is not in effect, so the screen can gray
        // it with a sentence instead of showing a switch that quietly does nothing (item 156's
        // rule for tier-gated controls, and the write-only-feature lesson behind it).
        var decision = await MediaStrippingPolicy.ForOrganizationAsync(db, _avStripper, orgId, ct);
        return Ok(new OrgSettingsResponse(
            org.ShowAddressMap, org.ShowAddressDirections,
            org.StripMediaMetadata, decision.Strips, decision.Reason, decision.NeedsUpgrade,
            decision.CanChoose));
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
        if (org is null) return NotFound();
        org.ShowAddressMap        = request.ShowAddressMap;
        org.ShowAddressDirections = request.ShowAddressDirections;
        // Saved whatever the plan says. A group that upgrades should find the preference they
        // expressed still there rather than silently reset to the default — and the effective
        // answer is computed on read, so storing it can never contradict the plan.
        org.StripMediaMetadata    = request.StripMediaMetadata;
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(Organization), orgId, before, org, userId.Value, AppSources.WebApi));

        var decision = await MediaStrippingPolicy.ForOrganizationAsync(db, _avStripper, orgId, ct);
        return Ok(new OrgSettingsResponse(
            org.ShowAddressMap, org.ShowAddressDirections,
            org.StripMediaMetadata, decision.Strips, decision.Reason, decision.NeedsUpgrade));
    }
}

/// <param name="ShowAddressMap">Whether the group's public page shows a map of its address.</param>
/// <param name="ShowAddressDirections">Whether that page offers directions to it.</param>
/// <param name="StripMediaMetadata">The group's stored preference.</param>
/// <param name="StripMediaMetadataInEffect">Whether it is actually happening — preference AND plan AND host.</param>
/// <param name="StripMediaMetadataReason">Why not, when it is not. Null when it is in effect.</param>
/// <param name="StripMediaMetadataNeedsUpgrade">True when the plan is the only thing in the way.</param>
/// <param name="StripMediaMetadataCanChoose">Whether the group may change the preference at all.</param>
public sealed record OrgSettingsResponse(
    bool ShowAddressMap, bool ShowAddressDirections,
    bool StripMediaMetadata = true, bool StripMediaMetadataInEffect = false,
    string? StripMediaMetadataReason = null, bool StripMediaMetadataNeedsUpgrade = false,
    bool StripMediaMetadataCanChoose = false);

public sealed record OrgSettingsRequest(
    bool ShowAddressMap, bool ShowAddressDirections, bool StripMediaMetadata = true);
