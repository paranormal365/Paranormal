using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A group's member-title ladder (item 157): the rungs, and which member holds which.
/// </summary>
/// <remarks>
/// Titles are seniority, never permission — no endpoint here (or anywhere) may read a member's
/// level to decide access. Members read the ladder (it renders on the roster they can already
/// see); admins edit it and assign rungs. Deleting a rung clears it from members holding it
/// (SetNull) — a ladder edit is never refused because somebody is standing on the rung.
/// </remarks>
[ApiController]
[Route("api/organizations/{orgId:guid}/member-levels")]
[Authorize]
public sealed class OrganizationMemberLevelController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public OrganizationMemberLevelController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    { _db = db; _mapper = mapper; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationMemberLevelRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var levels = await db.OrganizationMemberLevels.AsNoTracking()
            .Where(l => l.OrganizationId == orgId)
            .OrderBy(l => l.SortOrder).ToListAsync(ct);

        // One query for every rung's suggestions rather than one per rung.
        var levelIds  = levels.Select(l => l.Id).ToList();
        var suggested = (await db.OrganizationMemberLevelRoles.AsNoTracking()
                .Where(r => levelIds.Contains(r.OrganizationMemberLevelId))
                .Select(r => new { r.OrganizationMemberLevelId, r.OrganizationRoleId })
                .ToListAsync(ct))
            .GroupBy(r => r.OrganizationMemberLevelId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(x => x.OrganizationRoleId).ToList());

        var records = _mapper.Map<IEnumerable<OrganizationMemberLevelRecord>>(levels)
            .Select(r => r with
            {
                SuggestedRoleIds = suggested.TryGetValue(r.Id, out var ids) ? ids : [],
            });
        return Ok(records);
    }

    [HttpPost]
    public async Task<ActionResult<OrganizationMemberLevelRecord>> Create(
        Guid orgId, [FromBody] UpsertMemberLevelRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("A title needs a name.");

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = new OrganizationMemberLevel
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            Name = request.Name.Trim(), SortOrder = request.SortOrder,
            IsActive = request.IsActive, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.OrganizationMemberLevels.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetAll), new { orgId },
            _mapper.Map<OrganizationMemberLevelRecord>(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrganizationMemberLevelRecord>> Update(
        Guid orgId, Guid id, [FromBody] UpsertMemberLevelRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("A title needs a name.");

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.OrganizationMemberLevels
            .FirstOrDefaultAsync(l => l.Id == id && l.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();
        entity.Name = request.Name.Trim(); entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive; entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<OrganizationMemberLevelRecord>(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid id, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.OrganizationMemberLevels
            .FirstOrDefaultAsync(l => l.Id == id && l.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();
        db.OrganizationMemberLevels.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Sets (or clears, with null) one member's title.</summary>
    /// <remarks>Keyed by membership rather than user: the same person holds a different title in
    /// each of their groups, which is the whole reason titles live on the membership row.</remarks>
    [HttpPut("assign/{membershipId:guid}")]
    public async Task<IActionResult> Assign(
        Guid orgId, Guid membershipId, [FromBody] AssignMemberLevelRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var membership = await db.OrganizationUserMemberships
            .FirstOrDefaultAsync(m => m.Id == membershipId && m.OrganizationId == orgId, ct);
        if (membership is null) return NotFound();

        if (request.MemberLevelId is { } levelId
            && !await db.OrganizationMemberLevels.AnyAsync(
                    l => l.Id == levelId && l.OrganizationId == orgId, ct))
        {
            // A level from another organization must never be assignable here — the ladder is
            // per-group by design, not by accident.
            return BadRequest("That title does not belong to this group.");
        }

        membership.MemberLevelId = request.MemberLevelId;
        membership.DateUpdated = DateTime.UtcNow;
        membership.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;

        // ── Step 5: the roles this title usually carries, if the assigner asked for them ──
        // Opt-in, and only ever ADDITIVE. Clearing a title takes nothing away and neither does
        // moving somebody down the ladder: access is removed by removing a role, on the roles
        // screen, deliberately. A title screen that quietly revoked things would be the silent
        // re-grant this design exists to avoid, pointed the other way.
        if (request.ApplySuggestedRoles && request.MemberLevelId is { } applyLevelId)
        {
            var suggested = await db.OrganizationMemberLevelRoles.AsNoTracking()
                .Where(r => r.OrganizationMemberLevelId == applyLevelId)
                .Select(r => r.OrganizationRoleId)
                .ToListAsync(ct);

            var alreadyHeld = await db.OrganizationRoleMemberships.AsNoTracking()
                .Where(rm => rm.OrganizationUserMembershipId == membershipId)
                .Select(rm => rm.OrganizationRoleId)
                .ToListAsync(ct);

            // Re-checked against this organization rather than trusted from the join: a role row
            // that somehow pointed elsewhere must not become a grant in a group it never belonged
            // to.
            var grantable = await db.OrganizationRoles.AsNoTracking()
                .Where(r => suggested.Contains(r.Id) && r.OrganizationId == orgId && r.IsActive)
                .Select(r => r.Id)
                .ToListAsync(ct);

            foreach (var roleId in grantable.Except(alreadyHeld))
            {
                db.OrganizationRoleMemberships.Add(new OrganizationRoleMembership
                {
                    Id = Guid.NewGuid(),
                    OrganizationRoleId = roleId,
                    OrganizationUserMembershipId = membershipId,
                    DateCreated = DateTime.UtcNow,
                    CreatedByAppUserId = userId,
                });
            }
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Sets one member's title, optionally granting the roles that title suggests.
    /// </summary>
    /// <param name="MemberLevelId">The rung, or null to clear the title.</param>
    /// <param name="ApplySuggestedRoles">
    /// When true, also give this member the roles the new title suggests. Defaults to false so
    /// that an existing caller — and there are several — keeps assigning a title and nothing else.
    /// </param>
    public sealed record AssignMemberLevelRequest(Guid? MemberLevelId, bool ApplySuggestedRoles = false);

    // ── The suggestions themselves ────────────────────────────────────────────

    /// <summary>The roles this title suggests. Readable by any member — it explains the ladder.</summary>
    [HttpGet("{id:guid}/suggested-roles")]
    public async Task<ActionResult<IEnumerable<Guid>>> GetSuggestedRoles(
        Guid orgId, Guid id, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await db.OrganizationMemberLevels.AnyAsync(l => l.Id == id && l.OrganizationId == orgId, ct))
            return NotFound();

        return Ok(await db.OrganizationMemberLevelRoles.AsNoTracking()
            .Where(r => r.OrganizationMemberLevelId == id)
            .Select(r => r.OrganizationRoleId)
            .ToListAsync(ct));
    }

    /// <summary>
    /// Replaces the roles this title suggests.
    /// </summary>
    /// <remarks>
    /// <b>This changes nothing about who may do what.</b> It changes what the next assignment of
    /// this title will OFFER. Everybody already holding the title keeps exactly the roles they
    /// were given, which is the point of copying rather than inheriting.
    /// </remarks>
    [HttpPut("{id:guid}/suggested-roles")]
    public async Task<IActionResult> SetSuggestedRoles(
        Guid orgId, Guid id, [FromBody] SetSuggestedRolesRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await db.OrganizationMemberLevels.AnyAsync(l => l.Id == id && l.OrganizationId == orgId, ct))
            return NotFound();

        var wanted = request.RoleIds.Distinct().ToList();
        var foreign = await db.OrganizationRoles.AsNoTracking()
            .AnyAsync(r => wanted.Contains(r.Id) && r.OrganizationId != orgId, ct);
        if (foreign) return BadRequest("A role from another group cannot be suggested here.");

        var existing = await db.OrganizationMemberLevelRoles
            .Where(r => r.OrganizationMemberLevelId == id)
            .ToListAsync(ct);

        db.OrganizationMemberLevelRoles.RemoveRange(
            existing.Where(r => !wanted.Contains(r.OrganizationRoleId)));

        foreach (var roleId in wanted.Except(existing.Select(r => r.OrganizationRoleId)))
            db.OrganizationMemberLevelRoles.Add(new OrganizationMemberLevelRole
            {
                Id = Guid.NewGuid(),
                OrganizationMemberLevelId = id,
                OrganizationRoleId = roleId,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            });

        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record SetSuggestedRolesRequest(IReadOnlyList<Guid> RoleIds);

    private async Task<bool> IsOrgMemberAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgMemberAsync(db, orgId, userId, ct);
    }

    private async Task<bool> IsOrgAdminAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgAdminAsync(db, orgId, userId, ct);
    }
}
