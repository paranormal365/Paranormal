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
        return Ok(_mapper.Map<IEnumerable<OrganizationMemberLevelRecord>>(levels));
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
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record AssignMemberLevelRequest(Guid? MemberLevelId);

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
