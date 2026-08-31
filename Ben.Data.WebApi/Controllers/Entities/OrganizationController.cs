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

    /// <summary>
    /// Returns a single organization. Any active member may read it; everyone else needs explicit
    /// Read access or SuperAdmin.
    /// </summary>
    /// <remarks>
    /// <para>Membership alone used to be insufficient here, and that made a group's own page
    /// unreachable for most of its members. <c>HasAccessAsync</c> returns true for Owners and
    /// Administrators and then falls through to explicit grants and named roles — a plain Member
    /// with neither is refused for every table, including this one. Three of BenCo's four seeded
    /// members got a 403 from this endpoint, and the organisation hub, whose very first call this
    /// is, told them "Organization not found or you do not have access" about a group they belong
    /// to and can already post messages in.</para>
    ///
    /// <para>The record returned here is the group's own name, URL name and whether it is
    /// accepting applications — nothing a member does not already know by being one. So membership
    /// is the right bar for reading it, and the check is written here rather than inside
    /// <c>HasAccessAsync</c> deliberately: that method answers for every table, and members are
    /// emphatically not entitled to read all of them.</para>
    /// </remarks>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrganizationAdminRecord>> GetByIdWithPermissions(Guid id, CancellationToken ct)
    {
        var userId       = GetCurrentUserIdOrNull();
        if (userId is null) return Unauthorized();
        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (!isSuperAdmin)
        {
            var isActiveMember = await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == id && m.AppUserId == userId.Value && m.IsActive, ct);

            if (!isActiveMember)
            {
                var canRead = await _security.HasAccessAsync(userId.Value, id, OrganizationSecurityTable.Organization, OrganizationSecurityAction.Read, ct);
                if (!canRead) return Forbid();
            }
        }

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
        // Advertising for members is where the paid gate is felt FIRST, on purpose. Refusing only
        // at acceptance would let a free group collect applications it cannot accept — the
        // dead-end pattern items 149/150 made policy against. Turning the switch OFF is always
        // allowed: a rule that trapped somebody with a setting they could not undo would be worse
        // than the one it is enforcing.
        if (request.IsAcceptingApplications && !org.IsAcceptingApplications
            && await Services.Billing.PaidPlan.WhyCannotAddMemberAsync(db, id, ct) is { } needsPlan)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, needsPlan);
        }
        org.IsAcceptingApplications = request.IsAcceptingApplications;
        if (request.Kind is { } kind) org.Kind = kind;
        if (request.RunsPublicTours is { } runsTours) org.RunsPublicTours = runsTours;
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

        // The rows created WITH the organization, which therefore cannot be anyone's reason to
        // keep it: the founder's own membership, and the default calendar event types stamped at
        // registration. Every foreign key onto Organizations is NoAction by convention here, so
        // these have to go explicitly — and until they did, a group created after the default
        // event types shipped (item 148) could never be deleted at all: five rows nobody asked
        // for, arriving at birth, turning every delete into a 500.
        var birthChildren = await db.OrgCalendarEventTypes
            .Where(t => t.OrganizationId == id).ToListAsync(ct);
        db.OrgCalendarEventTypes.RemoveRange(birthChildren);

        var birthLevels = await db.OrganizationMemberLevels
            .Where(l => l.OrganizationId == id).ToListAsync(ct);
        db.OrganizationMemberLevels.RemoveRange(birthLevels);

        var birthDuties = await db.InvestigationDuties
            .Where(d => d.OrganizationId == id).ToListAsync(ct);
        db.InvestigationDuties.RemoveRange(birthDuties);

        // Roles and their dependents are birth children too (item 156 Phase C) — removed
        // leaf-first: assignments, then grants, then the roles themselves.
        var birthRoleIds = await db.OrganizationRoles
            .Where(r => r.OrganizationId == id).Select(r => r.Id).ToListAsync(ct);
        db.OrganizationRoleMemberships.RemoveRange(
            db.OrganizationRoleMemberships.Where(m => birthRoleIds.Contains(m.OrganizationRoleId)));
        db.OrganizationRolePermissions.RemoveRange(
            db.OrganizationRolePermissions.Where(p => birthRoleIds.Contains(p.OrganizationRoleId)));
        db.OrganizationRoles.RemoveRange(
            db.OrganizationRoles.Where(r => r.OrganizationId == id));

        var memberships = await db.OrganizationUserMemberships
            .Where(m => m.OrganizationId == id).ToListAsync(ct);
        db.OrganizationUserMemberships.RemoveRange(memberships);

        db.Organizations.Remove(org);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Everything else hanging off a group — cases, files, events, publications — is real
            // work, and refusing to delete a group that still has some is right. Saying so is the
            // part that was missing: this used to surface as an unhandled 500, which tells the
            // administrator nothing about what to do next.
            return Conflict(
                "This group still has records attached to it — cases, files, events or similar. "
                + "Remove or transfer those first, then delete the group.");
        }

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

        // The kind decides what this group STARTS as — public meeting point and public
        // events for a tour, the pre-existing private defaults for an investigation group.
        // Defaults only: everything is adjustable the moment the group wants.
        org.Kind = request.Kind;
        org.RunsPublicTours = OrganizationKindDefaults.RunsPublicTours(request.Kind);

        db.Organizations.Add(org);
        NewOrganizationDefaults.AddAll(db, org.Id, userId.Value);
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

    /// <summary>
    /// The group's roster — who belongs, in what role — readable by anybody who belongs to it.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists next to the near-identical <c>user-directory</c>:</b> that one
    /// answers "what are these people called", for name pickers. This one answers "who is in this
    /// group", which is what the hub's Members tab shows, and it needs the role and the active
    /// flag that a name directory has no business carrying.</para>
    ///
    /// <para><b>What it replaces.</b> The Members tab used to read
    /// <c>organizations/{id}/security/users</c>, whose service method requires Owner or
    /// Administrator — it is the endpoint behind *managing* access. So an ordinary member's own
    /// roster was refused, and since the website's API client turns a non-2xx into an empty list,
    /// the tab told them their group had no members at all while the Details tab beside it
    /// counted three. Item 109, and the same fault phase 5 found in messaging.</para>
    ///
    /// <para>The manage endpoint keeps its stricter gate; it is still the one used to change
    /// anybody's role. Reading a roster and editing one are different questions.</para>
    ///
    /// <para>Inactive memberships are included, because the tab shows an Active column — a
    /// roster that silently omitted lapsed members would misrepresent the group.</para>
    /// </remarks>
    [HttpGet("{organizationId:guid}/roster")]
    public async Task<ActionResult<IEnumerable<OrgRosterEntry>>> GetRoster(
        Guid organizationId, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        if (!User.IsInRole(RoleNames.SuperAdmin))
        {
            var isActiveMember = await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == organizationId && m.AppUserId == userId && m.IsActive, ct);
            if (!isActiveMember) return Forbid();
        }

        var roster = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId)
            .OrderBy(m => m.Role).ThenBy(m => m.DateCreated)
            .Join(db.AppUsers.AsNoTracking(), m => m.AppUserId, u => u.Id,
                (m, u) => new OrgRosterEntry(
                    m.Id, m.OrganizationId, m.AppUserId,
                    u.DisplayName ?? u.Email ?? u.UserName ?? u.Id.ToString(),
                    m.Role, m.IsActive, m.DateCreated, m.DateUpdated,
                    m.MemberLevelId,
                    m.MemberLevel != null ? m.MemberLevel.Name : null))
            .ToListAsync(ct);

        return Ok(roster);
    }

}

/// <summary>Minimal name-resolution entry for <see cref="OrganizationController.GetUserDirectory"/> —
/// deliberately excludes everything <c>AppUserRecord</c> carries beyond Id/DisplayName.</summary>
public sealed record OrgUserDirectoryEntry(Guid Id, string DisplayName);

/// <summary>
/// One line of a group's roster: who, in what role, still active or not.
/// </summary>
/// <remarks>
/// Carries no email, phone or account flags. A member may see who else is in their group and what
/// each of them does; that is not a reason to hand out contact details, which live behind the
/// consent rules on a person's own profile.
/// </remarks>
public sealed record OrgRosterEntry(
    Guid MembershipId,
    Guid OrganizationId,
    Guid AppUserId,
    string DisplayName,
    OrganizationMemberRole Role,
    bool IsActive,
    DateTime DateCreated,
    DateTime? DateUpdated,
    Guid? MemberLevelId = null,
    string? MemberLevelName = null);

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
    // Optional, and null means "leave as-is" — the same reasoning as the photo policy
    // below: an older caller that omits it must not silently reclassify a group.
    Ben.Data.Common.Enums.OrganizationKind? Kind = null,
    bool? RunsPublicTours = null,
    string? PublicPhone = null, string? PublicEmail = null, string? PublicWebsite = null,
    // Optional so an existing caller that omits it can't silently switch the policy off.
    // Null means "leave as-is"; see OrganizationController.Update.
    bool? AllowMemberPrivatePhotosToClients = null);

