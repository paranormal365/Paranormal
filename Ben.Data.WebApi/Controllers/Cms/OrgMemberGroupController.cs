using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>CRUD for organization member groups used in CMS page permission grants.</summary>
[Route("api/organizations/{orgId:guid}/groups")]
public sealed class OrgMemberGroupController : OrgCmsControllerBase
{
    private readonly IAuditLogService _auditLog;

    public OrgMemberGroupController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        IAuditLogService auditLog)
        : base(dbFactory, mapper, security)
    {
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrgMemberGroupRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrgMemberGroup, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var groups = await db.OrgMemberGroups.AsNoTracking()
            .Where(g => g.OrganizationId == orgId)
            .OrderBy(g => g.SortOrder).ThenBy(g => g.Name)
            .ToListAsync(ct);

        return Ok(Mapper.Map<IEnumerable<OrgMemberGroupRecord>>(groups));
    }

    [HttpPost]
    public async Task<ActionResult<OrgMemberGroupRecord>> Create(
        Guid orgId, [FromBody] CreateOrgMemberGroupRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrgMemberGroup, OrganizationSecurityAction.Create, ct))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var group = new OrgMemberGroup
        {
            OrganizationId     = orgId,
            Name               = request.Name.Trim(),
            Description        = request.Description?.Trim(),
            IsActive           = request.IsActive,
            SortOrder          = request.SortOrder,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value
        };

        db.OrgMemberGroups.Add(group);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrgMemberGroup), group.Id, group, userId.Value, AppSources.WebApi));
        return CreatedAtAction(nameof(GetAll), new { orgId }, Mapper.Map<OrgMemberGroupRecord>(group));
    }

    [HttpPut("{groupId:guid}")]
    public async Task<ActionResult<OrgMemberGroupRecord>> Update(
        Guid orgId, Guid groupId, [FromBody] UpdateOrgMemberGroupRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrgMemberGroup, OrganizationSecurityAction.Update, ct))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var before = await db.OrgMemberGroups.AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == groupId && g.OrganizationId == orgId, ct);
        if (before is null) return NotFound();
        var group = await db.OrgMemberGroups
            .FirstOrDefaultAsync(g => g.Id == groupId && g.OrganizationId == orgId, ct);

        group!.Name               = request.Name.Trim();
        group.Description        = request.Description?.Trim();
        group.IsActive           = request.IsActive;
        group.SortOrder          = request.SortOrder;
        group.DateUpdated        = DateTime.UtcNow;
        group.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(OrgMemberGroup), groupId, before, group!, userId.Value, AppSources.WebApi));
        return Ok(Mapper.Map<OrgMemberGroupRecord>(group));
    }

    [HttpDelete("{groupId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid groupId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrgMemberGroup, OrganizationSecurityAction.Delete, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var group = await db.OrgMemberGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == groupId && g.OrganizationId == orgId, ct);
        if (group is null) return NotFound();

        db.OrgMemberGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(OrgMemberGroup), groupId, group, userId.Value, AppSources.WebApi));
        return NoContent();
    }

    // ── Group membership management ───────────────────────────────────────────

    [HttpGet("{groupId:guid}/members")]
    public async Task<ActionResult<IEnumerable<OrgMemberGroupMembershipRecord>>> GetMembers(
        Guid orgId, Guid groupId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrgMemberGroup, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrgMemberGroups.AnyAsync(g => g.Id == groupId && g.OrganizationId == orgId, ct))
            return NotFound();

        var members = await db.OrgMemberGroupMemberships
            .AsNoTracking()
            .Where(m => m.OrgMemberGroupId == groupId)
            .ToListAsync(ct);

        return Ok(Mapper.Map<IEnumerable<OrgMemberGroupMembershipRecord>>(members));
    }

    [HttpPost("{groupId:guid}/members")]
    public async Task<ActionResult<OrgMemberGroupMembershipRecord>> AddMember(
        Guid orgId, Guid groupId, [FromBody] AddGroupMemberRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrgMemberGroup, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        if (!await db.OrgMemberGroups.AnyAsync(g => g.Id == groupId && g.OrganizationId == orgId, ct))
            return NotFound();

        // Verify the membership belongs to this org
        if (!await db.OrganizationUserMemberships.AnyAsync(
                m => m.Id == request.OrganizationUserMembershipId && m.OrganizationId == orgId, ct))
            return BadRequest("OrganizationUserMembership not found in this organization.");

        // Prevent duplicate
        if (await db.OrgMemberGroupMemberships.AnyAsync(
                m => m.OrgMemberGroupId == groupId && m.OrganizationUserMembershipId == request.OrganizationUserMembershipId, ct))
            return Conflict("This member is already in the group.");

        var membership = new OrgMemberGroupMembership
        {
            OrgMemberGroupId              = groupId,
            OrganizationUserMembershipId  = request.OrganizationUserMembershipId,
            DateCreated                   = DateTime.UtcNow,
            CreatedByAppUserId            = userId.Value
        };

        db.OrgMemberGroupMemberships.Add(membership);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrgMemberGroupMembership), membership.Id, membership, userId.Value, AppSources.WebApi));
        return CreatedAtAction(nameof(GetMembers), new { orgId, groupId },
            Mapper.Map<OrgMemberGroupMembershipRecord>(membership));
    }

    [HttpDelete("{groupId:guid}/members/{membershipId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid orgId, Guid groupId, Guid membershipId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrgMemberGroup, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var gm = await db.OrgMemberGroupMemberships
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.OrgMemberGroupId == groupId, ct);
        if (gm is null) return NotFound();

        db.OrgMemberGroupMemberships.Remove(gm);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(OrgMemberGroupMembership), membershipId, gm, userId.Value, AppSources.WebApi));
        return NoContent();
    }
}

public sealed record CreateOrgMemberGroupRequest(string Name, string? Description, bool IsActive, int SortOrder);
public sealed record UpdateOrgMemberGroupRequest(string Name, string? Description, bool IsActive, int SortOrder);
public sealed record AddGroupMemberRequest(Guid OrganizationUserMembershipId);
