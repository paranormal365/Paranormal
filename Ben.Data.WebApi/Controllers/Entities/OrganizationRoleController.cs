using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>CRUD for organization named roles and their permissions/memberships.</summary>
[Route("api/organizations/{orgId:guid}/roles")]
public sealed class OrganizationRoleController : OrgCmsControllerBase
{
    private readonly IAuditLogService _auditLog;

    public OrganizationRoleController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        IAuditLogService auditLog)
        : base(dbFactory, mapper, security)
    {
        _auditLog = auditLog;
    }

    // ── Roles CRUD ────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationRoleRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var roles = await db.OrganizationRoles.AsNoTracking()
            .Where(r => r.OrganizationId == orgId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .ToListAsync(ct);

        return Ok(Mapper.Map<IEnumerable<OrganizationRoleRecord>>(roles));
    }

    [HttpGet("{roleId:guid}")]
    public async Task<ActionResult<OrganizationRoleRecord>> GetById(
        Guid orgId, Guid roleId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var role = await db.OrganizationRoles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleId && r.OrganizationId == orgId, ct);
        if (role is null) return NotFound();

        return Ok(Mapper.Map<OrganizationRoleRecord>(role));
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationRoleRecord>> Create(
        Guid orgId, [FromBody] CreateOrgRoleRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var role = new OrganizationRole
        {
            OrganizationId     = orgId,
            Name               = request.Name.Trim(),
            Description        = request.Description?.Trim(),
            IsActive           = request.IsActive,
            SortOrder          = request.SortOrder,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value
        };

        db.OrganizationRoles.Add(role);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrganizationRole), role.Id, role, userId.Value, AppSources.WebApi));
        return CreatedAtAction(nameof(GetAll), new { orgId }, Mapper.Map<OrganizationRoleRecord>(role));
    }

    [HttpPut("{roleId:guid}")]
    public async Task<ActionResult<OrganizationRoleRecord>> Update(
        Guid orgId, Guid roleId, [FromBody] UpdateOrgRoleRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var before = await db.OrganizationRoles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleId && r.OrganizationId == orgId, ct);
        if (before is null) return NotFound();

        var role = await db.OrganizationRoles
            .FirstOrDefaultAsync(r => r.Id == roleId && r.OrganizationId == orgId, ct);

        role!.Name               = request.Name.Trim();
        role.Description        = request.Description?.Trim();
        role.IsActive           = request.IsActive;
        role.SortOrder          = request.SortOrder;
        role.DateUpdated        = DateTime.UtcNow;
        role.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(OrganizationRole), roleId, before, role!, userId.Value, AppSources.WebApi));
        return Ok(Mapper.Map<OrganizationRoleRecord>(role));
    }

    [HttpDelete("{roleId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid roleId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var role = await db.OrganizationRoles
            .Include(r => r.Permissions)
            .Include(r => r.Members)
            .FirstOrDefaultAsync(r => r.Id == roleId && r.OrganizationId == orgId, ct);
        if (role is null) return NotFound();

        db.OrganizationRoles.Remove(role);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(OrganizationRole), roleId, role, userId.Value, AppSources.WebApi));
        return NoContent();
    }

    // ── Permissions ───────────────────────────────────────────────────────────

    [HttpGet("{roleId:guid}/permissions")]
    public async Task<ActionResult<IEnumerable<OrganizationRolePermissionRecord>>> GetPermissions(
        Guid orgId, Guid roleId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationRoles.AnyAsync(r => r.Id == roleId && r.OrganizationId == orgId, ct))
            return NotFound();

        var permissions = await db.OrganizationRolePermissions.AsNoTracking()
            .Where(p => p.OrganizationRoleId == roleId)
            .ToListAsync(ct);

        return Ok(Mapper.Map<IEnumerable<OrganizationRolePermissionRecord>>(permissions));
    }

    [HttpPut("{roleId:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(
        Guid orgId, Guid roleId, [FromBody] IEnumerable<SetRolePermissionRequest> permissions, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationRoles.AnyAsync(r => r.Id == roleId && r.OrganizationId == orgId, ct))
            return NotFound();

        var existing = await db.OrganizationRolePermissions
            .Where(p => p.OrganizationRoleId == roleId)
            .ToListAsync(ct);

        // ── Item 156 Phase E, decision D4: excluded areas are grayed-but-REMEMBERED ──
        // The editor disables toggles whose area the plan does not include, so an honest client
        // never sends a change to one. The server enforces the same rule: an attempted CHANGE to
        // an excluded table is refused with a sentence, and untouched excluded grants are carried
        // forward verbatim — a save of the sections you MAY edit must never silently delete the
        // sections you may not. Nothing is ever deleted by a downgrade; it resumes on upgrade.
        var includedAreas = await Ben.Data.Source.Services.TierAreaResolution
            .IncludedAreasAsync(db, orgId, ct);
        var incomingByTable = permissions
            .Where(p => p.Actions != OrganizationSecurityAction.None)
            .GroupBy(p => p.TableName)
            .ToDictionary(g => g.Key, g => g.Aggregate(OrganizationSecurityAction.None, (a, p) => a | p.Actions));
        var existingByTable = existing
            .GroupBy(p => p.TableName)
            .ToDictionary(g => g.Key, g => g.Aggregate(OrganizationSecurityAction.None, (a, p) => a | p.Actions));

        bool Excluded(OrganizationSecurityTable table)
            => Ben.Data.Common.Constants.PermissionAreas.AreaFor(table) is { } area
            && !includedAreas.Contains(area);

        foreach (var table in incomingByTable.Keys.Union(existingByTable.Keys).Where(Excluded))
        {
            var incoming = incomingByTable.GetValueOrDefault(table, OrganizationSecurityAction.None);
            var current  = existingByTable.GetValueOrDefault(table, OrganizationSecurityAction.None);
            // Absent means "the editor didn't let me touch it" — carried forward below. Present
            // and identical is a no-op round-trip. Present and DIFFERENT is a change the plan
            // does not include.
            if (incomingByTable.ContainsKey(table) && incoming != current)
            {
                var areaName = Ben.Data.Common.Constants.PermissionAreas.AreaFor(table);
                return BadRequest(
                    $"Your group's plan does not include custom-role permissions for {areaName}. "
                    + "The stored settings are kept and will apply again if the plan changes — "
                    + "see the Pricing page for what each plan includes.");
            }
        }

        db.OrganizationRolePermissions.RemoveRange(existing);

        var newPermissions = incomingByTable
            .Where(kv => !Excluded(kv.Key))
            .Select(kv => new OrganizationRolePermission
            {
                OrganizationRoleId = roleId,
                TableName          = kv.Key,
                Actions            = kv.Value,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = userId.Value
            })
            // Excluded tables: the EXISTING grants survive verbatim, whatever the payload said.
            .Concat(existingByTable.Where(kv => Excluded(kv.Key))
                .Select(kv => new OrganizationRolePermission
                {
                    OrganizationRoleId = roleId,
                    TableName          = kv.Key,
                    Actions            = kv.Value,
                    DateCreated        = DateTime.UtcNow,
                    CreatedByAppUserId = userId.Value
                }))
            .ToList();

        db.OrganizationRolePermissions.AddRange(newPermissions);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Members ───────────────────────────────────────────────────────────────

    [HttpGet("{roleId:guid}/members")]
    public async Task<ActionResult<IEnumerable<OrganizationRoleMembershipRecord>>> GetMembers(
        Guid orgId, Guid roleId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationRoles.AnyAsync(r => r.Id == roleId && r.OrganizationId == orgId, ct))
            return NotFound();

        var members = await db.OrganizationRoleMemberships.AsNoTracking()
            .Where(m => m.OrganizationRoleId == roleId)
            .ToListAsync(ct);

        return Ok(Mapper.Map<IEnumerable<OrganizationRoleMembershipRecord>>(members));
    }

    [HttpPost("{roleId:guid}/members")]
    public async Task<ActionResult<OrganizationRoleMembershipRecord>> AddMember(
        Guid orgId, Guid roleId, [FromBody] AddRoleMemberRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        if (!await db.OrganizationRoles.AnyAsync(r => r.Id == roleId && r.OrganizationId == orgId, ct))
            return NotFound();

        // Verify the membership belongs to this org
        if (!await db.OrganizationUserMemberships.AnyAsync(
                m => m.Id == request.OrganizationUserMembershipId && m.OrganizationId == orgId, ct))
            return BadRequest("OrganizationUserMembership not found in this organization.");

        // Prevent duplicate
        if (await db.OrganizationRoleMemberships.AnyAsync(
                m => m.OrganizationRoleId == roleId && m.OrganizationUserMembershipId == request.OrganizationUserMembershipId, ct))
            return Conflict("This member is already assigned to this role.");

        var membership = new OrganizationRoleMembership
        {
            OrganizationRoleId           = roleId,
            OrganizationUserMembershipId = request.OrganizationUserMembershipId,
            DateCreated                  = DateTime.UtcNow,
            CreatedByAppUserId           = userId.Value
        };

        db.OrganizationRoleMemberships.Add(membership);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrganizationRoleMembership), membership.Id, membership, userId.Value, AppSources.WebApi));
        return CreatedAtAction(nameof(GetMembers), new { orgId, roleId },
            Mapper.Map<OrganizationRoleMembershipRecord>(membership));
    }

    [HttpDelete("{roleId:guid}/members/{membershipId:guid}")]
    public async Task<IActionResult> RemoveMember(
        Guid orgId, Guid roleId, Guid membershipId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var rm = await db.OrganizationRoleMemberships
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.OrganizationRoleId == roleId, ct);
        if (rm is null) return NotFound();

        // Verify the role belongs to this org
        if (!await db.OrganizationRoles.AnyAsync(r => r.Id == roleId && r.OrganizationId == orgId, ct))
            return NotFound();

        db.OrganizationRoleMemberships.Remove(rm);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(OrganizationRoleMembership), membershipId, rm, userId.Value, AppSources.WebApi));
        return NoContent();
    }
}

public sealed record CreateOrgRoleRequest(string Name, string? Description, bool IsActive, int SortOrder);
public sealed record UpdateOrgRoleRequest(string Name, string? Description, bool IsActive, int SortOrder);
public sealed record SetRolePermissionRequest(OrganizationSecurityTable TableName, OrganizationSecurityAction Actions);
public sealed record AddRoleMemberRequest(Guid OrganizationUserMembershipId);
