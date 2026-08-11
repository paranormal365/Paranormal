using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
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
        if (isSuperAdmin && orgs.Count > 0)
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var orgIds = orgs.Select(o => o.Id).ToList();

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
                .Where(i => orgIds.Contains(i.Case.OrganizationId))
                .GroupBy(i => i.Case.OrganizationId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        }

        var result = new List<OrganizationListItemResponse>(orgs.Count);
        foreach (var org in orgs)
        {
            bool canEdit, canDelete;
            if (isSuperAdmin)
            {
                canEdit   = true;
                canDelete = true;
            }
            else
            {
                canEdit   = await _security.HasAccessAsync(userId.Value, org.Id, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Update, ct);
                canDelete = await _security.HasAccessAsync(userId.Value, org.Id, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Delete, ct);
            }
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
        if (string.IsNullOrWhiteSpace(request.UrlName)) return BadRequest("UrlName is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var before = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
        if (before is null) return NotFound();
        var org = await db.Organizations.FirstOrDefaultAsync(o => o.Id == id, ct);
        if (org is null) return NotFound();

        org.Name                   = request.Name.Trim();
        org.UrlName                = request.UrlName.Trim().ToLowerInvariant();
        org.IsAcceptingApplications = request.IsAcceptingApplications;
        org.PublicPhone             = request.PublicPhone?.Trim();
        org.PublicEmail             = request.PublicEmail?.Trim();
        org.PublicWebsite           = request.PublicWebsite?.Trim();
        org.DateUpdated            = DateTime.UtcNow;
        org.UpdatedByAppUserId     = userId.Value;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(Organization), id, before, org, GetCurrentUserId(), AppSources.WebApi, ct));
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
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(Organization), id, org, GetCurrentUserId(), AppSources.WebApi, ct));
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
        if (string.IsNullOrWhiteSpace(request.UrlName)) return BadRequest("UrlName is required.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var urlName = request.UrlName.Trim().ToLowerInvariant();
        if (await db.Organizations.AnyAsync(o => o.UrlName == urlName, ct))
            return BadRequest($"UrlName '{urlName}' is already in use.");

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
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(Organization), org.Id, org, GetCurrentUserId(), AppSources.WebApi, ct));

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
    string? PublicPhone = null, string? PublicEmail = null, string? PublicWebsite = null);

