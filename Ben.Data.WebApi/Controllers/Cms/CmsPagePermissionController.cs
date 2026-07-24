using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>Per-page CMS permission grants (individual members or member groups).</summary>
[Route("api/organizations/{orgId:guid}/pages/{pageId:guid}/permissions")]
public sealed class CmsPagePermissionController : OrgCmsControllerBase
{
    private readonly IAuditLogService _auditLog;

    public CmsPagePermissionController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        IAuditLogService auditLog)
        : base(dbFactory, mapper, security)
    {
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CmsPagePermissionRecord>>> GetAll(
        Guid orgId, Guid pageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationPages.AnyAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct))
            return NotFound();

        var perms = await db.CmsPagePermissions.AsNoTracking()
            .Where(p => p.OrganizationPageId == pageId)
            .ToListAsync(ct);

        return Ok(Mapper.Map<IEnumerable<CmsPagePermissionRecord>>(perms));
    }

    [HttpPost]
    public async Task<ActionResult<CmsPagePermissionRecord>> Create(
        Guid orgId, Guid pageId, [FromBody] CreatePagePermissionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Update, ct))
            return Forbid();

        if (request.AppUserId is null && request.OrgMemberGroupId is null)
            return BadRequest("Either AppUserId or OrgMemberGroupId must be specified.");

        if (request.Actions == CmsPageAction.None)
            return BadRequest("At least one action must be specified.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationPages.AnyAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct))
            return NotFound();

        var perm = new CmsPagePermission
        {
            OrganizationPageId = pageId,
            AppUserId          = request.AppUserId,
            OrgMemberGroupId   = request.OrgMemberGroupId,
            Actions            = request.Actions,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value
        };

        db.CmsPagePermissions.Add(perm);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(CmsPagePermission), perm.Id, perm, userId.Value, AppSources.WebApi, ct));
        return CreatedAtAction(nameof(GetAll), new { orgId, pageId },
            Mapper.Map<CmsPagePermissionRecord>(perm));
    }

    [HttpPut("{permissionId:guid}")]
    public async Task<ActionResult<CmsPagePermissionRecord>> Update(
        Guid orgId, Guid pageId, Guid permissionId,
        [FromBody] UpdatePagePermissionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Update, ct))
            return Forbid();

        if (request.Actions == CmsPageAction.None)
            return BadRequest("At least one action must be specified. Use DELETE to remove a permission.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var before = await db.CmsPagePermissions.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == permissionId && p.OrganizationPageId == pageId, ct);
        if (before is null) return NotFound();
        var perm = await db.CmsPagePermissions
            .FirstOrDefaultAsync(p => p.Id == permissionId && p.OrganizationPageId == pageId, ct);

        perm!.Actions            = request.Actions;
        perm.DateUpdated        = DateTime.UtcNow;
        perm.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(CmsPagePermission), permissionId, before, perm!, userId.Value, AppSources.WebApi, ct));
        return Ok(Mapper.Map<CmsPagePermissionRecord>(perm));
    }

    [HttpDelete("{permissionId:guid}")]
    public async Task<IActionResult> Delete(
        Guid orgId, Guid pageId, Guid permissionId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var perm = await db.CmsPagePermissions
            .FirstOrDefaultAsync(p => p.Id == permissionId && p.OrganizationPageId == pageId, ct);
        if (perm is null) return NotFound();

        db.CmsPagePermissions.Remove(perm);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(CmsPagePermission), permissionId, perm, userId.Value, AppSources.WebApi, ct));
        return NoContent();
    }
}

public sealed record CreatePagePermissionRequest(
    Guid? AppUserId,
    Guid? OrgMemberGroupId,
    CmsPageAction Actions);

public sealed record UpdatePagePermissionRequest(CmsPageAction Actions);
