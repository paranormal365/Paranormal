using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

[Route("api/organizations")]
public sealed class OrganizationController : EntityReadControllerBase<Organization, OrganizationRecord>
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IMapper _mapper2;
    private readonly IOrganizationSecurityService _security;
    private readonly IAuditLogService _auditLog;

    public OrganizationController(
        IDbContextFactory<BenDataContext> dbContextFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        IAuditLogService auditLog)
        : base(dbContextFactory, mapper)
    {
        _dbFactory = dbContextFactory;
        _mapper2   = mapper;
        _security  = security;
        _auditLog  = auditLog;
    }

    // ── Suppress base read-only GET endpoints ─────────────────────────────────

    [NonAction]
    public override Task<ActionResult<IEnumerable<OrganizationRecord>>> GetAll(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    [NonAction]
    public override Task<ActionResult<OrganizationRecord>> GetById(Guid id, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    // ── Permission-aware GET endpoints ────────────────────────────────────────

    /// <summary>
    /// Returns organizations visible to the current user with per-org CanEdit and CanDelete flags.
    /// SuperAdmins see all organizations; others see only orgs they are active members of.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationListItemResponse>>> GetAllWithPermissions(CancellationToken ct)
    {
        var userId       = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        var orgs = await _security.GetOrganizationsForUserAsync(userId.Value, ct);

        // Member/case/investigation counts are only ever shown to SuperAdmins (the list view's own
        // per-org visibility already scopes non-admins to orgs they belong to), so skip the extra
        // grouped queries entirely for the common non-admin case.
        Dictionary<Guid, int> memberCounts = [];
        Dictionary<Guid, int> caseCounts = [];
        Dictionary<Guid, int> investigationCounts = [];

        // Edit/delete permission per org, batched: previously called HasAccessAsync (which opens its
        // own DbContext and issues up to 4 queries) twice per org in a loop -- up to 8N queries for N
        // orgs. Resolved here with 3 fixed queries total, regardless of org count.
        Dictionary<Guid, bool> canEditMap = [];
        Dictionary<Guid, bool> canDeleteMap = [];

        if (orgs.Count > 0)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var orgIds = orgs.Select(o => o.Id).ToList();

            if (isSuperAdmin)
            {
                memberCounts = await db.OrganizationUserMemberships.AsNoTracking()
                    .Where(m => orgIds.Contains(m.OrganizationId) && m.IsActive)
                    .GroupBy(m => m.OrganizationId)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                caseCounts = await db.Cases.AsNoTracking()
                    .Where(c => orgIds.Contains(c.OrganizationId))
                    .GroupBy(c => c.OrganizationId)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

                investigationCounts = await db.Investigations.AsNoTracking()
                    .Where(i => orgIds.Contains(i.OrganizationId))
                    .GroupBy(i => i.OrganizationId)
                    .Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
            }
            else
            {
                var memberships = await db.OrganizationUserMemberships.AsNoTracking()
                    .Where(m => orgIds.Contains(m.OrganizationId) && m.AppUserId == userId.Value && m.IsActive)
                    .ToDictionaryAsync(m => m.OrganizationId, ct);

                var directGrants = await db.OrganizationAccessGrants.AsNoTracking()
                    .Where(g => orgIds.Contains(g.OrganizationId) && g.AppUserId == userId.Value
                             && g.TableName == OrganizationSecurityTable.Organization)
                    .ToListAsync(ct);
                var directGrantsByOrg = directGrants.ToLookup(g => g.OrganizationId);

                var rolePermissions = await (
                    from roleMembership in db.OrganizationRoleMemberships
                    join role in db.OrganizationRoles on roleMembership.OrganizationRoleId equals role.Id
                    join permission in db.OrganizationRolePermissions on role.Id equals permission.OrganizationRoleId
                    join userMembership in db.OrganizationUserMemberships on roleMembership.OrganizationUserMembershipId equals userMembership.Id
                    where orgIds.Contains(userMembership.OrganizationId)
                        && userMembership.AppUserId == userId.Value
                        && userMembership.IsActive
                        && role.IsActive
                        && permission.TableName == OrganizationSecurityTable.Organization
                    select new { userMembership.OrganizationId, permission.Actions }
                ).ToListAsync(ct);
                var rolePermissionsByOrg = rolePermissions.ToLookup(x => x.OrganizationId);

                bool HasAction(Guid orgId, OrganizationSecurityAction action)
                {
                    if (!memberships.TryGetValue(orgId, out var membership)) return false;
                    if (membership.Role is OrganizationMemberRole.Owner or OrganizationMemberRole.Administrator) return true;
                    if (directGrantsByOrg[orgId].Any(g => (g.Actions & action) != OrganizationSecurityAction.None)) return true;
                    return rolePermissionsByOrg[orgId].Any(x => (x.Actions & action) != OrganizationSecurityAction.None);
                }

                foreach (var orgId in orgIds)
                {
                    canEditMap[orgId]   = HasAction(orgId, OrganizationSecurityAction.Update);
                    canDeleteMap[orgId] = HasAction(orgId, OrganizationSecurityAction.Delete);
                }
            }
        }

        var result = new List<OrganizationListItemResponse>(orgs.Count);
        foreach (var org in orgs)
        {
            var canEdit   = isSuperAdmin || canEditMap.GetValueOrDefault(org.Id);
            var canDelete = isSuperAdmin || canDeleteMap.GetValueOrDefault(org.Id);
            result.Add(new OrganizationListItemResponse(org.Id, org.Name, org.UrlName, org.DateCreated, org.IsAcceptingApplications, canEdit, canDelete,
                memberCounts.GetValueOrDefault(org.Id), caseCounts.GetValueOrDefault(org.Id), investigationCounts.GetValueOrDefault(org.Id)));
        }
        return Ok(result);
    }

    /// <summary>Returns a single organization for the edit form. Requires Read access or SuperAdmin.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrganizationAdminRecord>> GetByIdWithPermissions(Guid id, CancellationToken ct)
    {
        var userId       = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canRead = await _security.HasAccessAsync(userId.Value, id, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Read, ct);
            if (!canRead) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        return Ok(_mapper2.Map<OrganizationAdminRecord>(org));
    }

    // ── Mutating endpoints ────────────────────────────────────────────────────

    /// <summary>Updates Name and UrlName. Requires Update access or SuperAdmin.</summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrganizationAdminRecord>> Update(
        Guid id, [FromBody] AdminUpdateOrganizationRequest request, CancellationToken ct)
    {
        var userId       = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canEdit = await _security.HasAccessAsync(userId.Value, id, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct);
            if (!canEdit) return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name))    return BadRequest("Name is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var before = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (before is null) return NotFound();
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        // Checked on rename as well as on create. It never was, and there was no index behind
        // either — so a group could rename onto another group's address and take their traffic.
        var refusal = await OrganizationUrlNames.RefusalForAsync(db, request.UrlName, id, ct);
        if (refusal is not null) return BadRequest(refusal);

        org.Name                   = request.Name.Trim();
        // Keeps the old address working. A group's address is the one part of this product that
        // ends up on a business card, and renaming used to break every printed link in silence.
        await OrganizationUrlNames.ApplyAsync(db, org, request.UrlName, userId, ct);
        org.IsAcceptingApplications = request.IsAcceptingApplications;
        org.PublicPhone             = request.PublicPhone?.Trim();
        org.PublicEmail             = request.PublicEmail?.Trim();
        org.PublicWebsite           = request.PublicWebsite?.Trim();
        // Null leaves the policy untouched. This endpoint is the general org-settings save, so a
        // caller editing the name must not be able to revoke a privacy policy it never sent.
        if (request.AllowMemberPrivatePhotosToClients is { } allow)
            org.AllowMemberPrivatePhotosToClients = allow;
        org.DateUpdated            = DateTime.UtcNow;
        org.UpdatedByAppUserId     = userId.Value;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(Organization), id, before, org, GetCurrentUserId(), AppSources.WebApi));
        return Ok(_mapper2.Map<OrganizationAdminRecord>(org));
    }

    /// <summary>Deletes an organization. Requires Delete access or SuperAdmin.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var userId       = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        if (!isSuperAdmin)
        {
            var canDelete = await _security.HasAccessAsync(userId.Value, id, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Delete, ct);
            if (!canDelete) return Forbid();
        }

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        db.Organizations.Remove(org);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(Organization), id, org, GetCurrentUserId(), AppSources.WebApi));
        return NoContent();
    }

    /// <summary>Creates a new organization. SuperAdmin only (checked via DB role, supports both local and Entra tokens).</summary>
    [HttpPost]
    public async Task<ActionResult<OrganizationAdminRecord>> Create(
        [FromBody] AdminCreateOrganizationRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();

        if (!User.IsInRole(RoleNames.SuperAdmin)) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Name))    return BadRequest("Name is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var refusal = await OrganizationUrlNames.RefusalForAsync(db, request.UrlName, null, ct);
        if (refusal is not null) return BadRequest(refusal);

        var urlName = Ben.Data.Common.SlugText.NormalizeOrEmpty(request.UrlName);

        var org = new Organization
        {
            Name               = request.Name.Trim(),
            UrlName            = urlName,
            PublicPhone        = request.PublicPhone?.Trim(),
            PublicEmail        = request.PublicEmail?.Trim(),
            PublicWebsite      = request.PublicWebsite?.Trim(),
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value
        };

        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(Organization), org.Id, org, GetCurrentUserId(), AppSources.WebApi));

        return CreatedAtAction(nameof(GetByIdWithPermissions), new { id = org.Id },
            _mapper2.Map<OrganizationAdminRecord>(org));
    }

    /// <summary>
    /// Returns a minimal Id + DisplayName directory of this organization's active members — just
    /// enough for org-admin surfaces (e.g. the CMS permission/member pickers) to resolve names,
    /// without exposing full <c>AppUserRecord</c> (email, phone, 2FA/confirmation flags), which
    /// is SuperAdmin-only via <c>AppUserController</c> (see <see cref="EntityReadControllerBase{TEntity,TRecord}"/>'s
    /// doc comment on why that lockdown exists). Gated on the caller being an active member of
    /// this same organization — not SuperAdmin — since regular org admins are the actual callers.
    /// </summary>
    [HttpGet("{organizationId:guid}/user-directory")]
    public async Task<ActionResult<IEnumerable<OrgUserDirectoryEntry>>> GetUserDirectory(
        Guid organizationId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var isActiveMember = await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == organizationId && m.AppUserId == userId && m.IsActive, ct);
        if (!isActiveMember) return Forbid();

        var entries = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.IsActive)
            .Join(db.AppUsers.AsNoTracking(), m => m.AppUserId, u => u.Id,
                (m, u) => new OrgUserDirectoryEntry(u.Id, u.DisplayName ?? u.Email ?? u.UserName ?? u.Id.ToString()))
            .ToListAsync(ct);

        return Ok(entries);
    }

}

/// <summary>Minimal name-resolution entry for <see cref="OrganizationController.GetUserDirectory"/> —
/// deliberately excludes everything <c>AppUserRecord</c> carries beyond Id/DisplayName.</summary>
public sealed record OrgUserDirectoryEntry(Guid Id, string DisplayName);

public sealed record OrganizationListItemResponse(
    Guid Id,
    string Name,
    string UrlName,
    DateTime DateCreated,
    bool IsAcceptingApplications,
    bool CanEdit,
    bool CanDelete,
    // 0 unless the caller is SuperAdmin — see GetAllWithPermissions.
    int MemberCount = 0,
    int CaseCount = 0,
    int InvestigationCount = 0);

public sealed record AdminUpdateOrganizationRequest(string Name, string UrlName,
    bool IsAcceptingApplications = false,
    string? PublicPhone = null, string? PublicEmail = null, string? PublicWebsite = null,
    // Optional so an existing caller that omits it can't silently switch the policy off.
    // Null means "leave as-is"; see OrganizationController.Update.
    bool? AllowMemberPrivatePhotosToClients = null);

