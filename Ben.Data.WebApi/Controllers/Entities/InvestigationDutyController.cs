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
/// A group's investigation duties (item 158): the list of jobs handed out per visit.
/// </summary>
/// <remarks>
/// Members read (the duty board renders for the whole team); admins edit. Deleting a duty takes
/// its assignments with it in the same save — an assignment to a duty that no longer exists
/// means nothing, and the NoAction foreign key would otherwise refuse the delete.
/// </remarks>
[ApiController]
[Route("api/organizations/{orgId:guid}/investigation-duties")]
[Authorize]
public sealed class InvestigationDutyController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public InvestigationDutyController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    { _db = db; _mapper = mapper; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<InvestigationDutyRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var duties = await db.InvestigationDuties.AsNoTracking()
            .Include(d => d.MinimumMemberLevel)
            .Where(d => d.OrganizationId == orgId)
            .OrderBy(d => d.SortOrder).ToListAsync(ct);
        return Ok(duties.Select(ToRecord));
    }

    /// <summary>
    /// The whole title-by-duty matrix for this group (item 160): which titles may hold which
    /// duties, and what each duty confers.
    /// </summary>
    /// <remarks>
    /// Readable by any member, like the duty list itself — knowing what a title opens up is how
    /// somebody knows what to work towards. Editing stays with the group's administrators.
    /// </remarks>
    [HttpGet("matrix")]
    public async Task<ActionResult<DutyEligibilityMatrix>> GetMatrix(Guid orgId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);

        var titles = await db.OrganizationMemberLevels.AsNoTracking()
            .Where(l => l.OrganizationId == orgId && l.IsActive)
            .OrderBy(l => l.SortOrder)
            .Select(l => new DutyMatrixTitle(l.Id, l.Name, l.SortOrder))
            .ToListAsync(ct);

        var duties = await db.InvestigationDuties.AsNoTracking()
            .Include(d => d.MinimumMemberLevel)
            .Where(d => d.OrganizationId == orgId && d.IsActive)
            .OrderBy(d => d.SortOrder)
            .ToListAsync(ct);

        var dutyIds = duties.Select(d => d.Id).ToList();
        var cells = await db.InvestigationDutyEligibilities.AsNoTracking()
            .Where(e => dutyIds.Contains(e.InvestigationDutyId))
            .Select(e => new { e.InvestigationDutyId, e.OrganizationMemberLevelId })
            .ToListAsync(ct);

        var rows = duties.Select(d => new DutyMatrixRow(
            d.Id, d.Name, d.SortOrder, d.IsSingleHolder, d.Capabilities, d.IsEnforced,
            [.. cells.Where(c => c.InvestigationDutyId == d.Id).Select(c => c.OrganizationMemberLevelId)],
            d.MinimumMemberLevelId, d.MinimumMemberLevel?.Name)).ToList();

        return Ok(new DutyEligibilityMatrix(titles, rows));
    }

    /// <summary>Sets one duty's row of the matrix — the whole set of titles, and what it confers.</summary>
    [HttpPut("{id:guid}/eligibility")]
    public async Task<ActionResult<DutyEligibilityMatrix>> SetEligibility(
        Guid orgId, Guid id, [FromBody] SetDutyEligibilityRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);

        var duty = await db.InvestigationDuties
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == orgId, ct);
        if (duty is null) return NotFound();

        // Titles are checked against this group's own ladder. A rung from somebody else's group
        // would be a cell nobody could ever satisfy, and the matrix would quietly stop matching
        // anybody rather than say why.
        var wanted = (request.TitleIds ?? []).Distinct().ToList();
        if (wanted.Count > 0)
        {
            var mine = await db.OrganizationMemberLevels.AsNoTracking()
                .Where(l => l.OrganizationId == orgId && wanted.Contains(l.Id))
                .Select(l => l.Id).ToListAsync(ct);
            if (mine.Count != wanted.Count)
                return BadRequest("One of those titles does not belong to this group.");
        }

        var existing = await db.InvestigationDutyEligibilities
            .Where(e => e.InvestigationDutyId == id).ToListAsync(ct);

        db.InvestigationDutyEligibilities.RemoveRange(
            existing.Where(e => !wanted.Contains(e.OrganizationMemberLevelId)));

        var userId = GetCurrentUserId();
        foreach (var titleId in wanted.Where(t => existing.All(e => e.OrganizationMemberLevelId != t)))
        {
            db.InvestigationDutyEligibilities.Add(new InvestigationDutyEligibility
            {
                InvestigationDutyId = id,
                OrganizationMemberLevelId = titleId,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            });
        }

        duty.Capabilities = request.Capabilities;
        duty.IsEnforced = request.IsEnforced;
        duty.DateUpdated = DateTime.UtcNow;
        duty.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        return await GetMatrix(orgId, ct);
    }

    [HttpPost]
    public async Task<ActionResult<InvestigationDutyRecord>> Create(
        Guid orgId, [FromBody] UpsertInvestigationDutyRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("A duty needs a name.");

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await MinimumLevelForeignAsync(db, orgId, request.MinimumMemberLevelId, ct))
            return BadRequest("That title does not belong to this group.");

        var entity = new InvestigationDuty
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            Name = request.Name.Trim(), SortOrder = request.SortOrder,
            IsActive = request.IsActive, IsSingleHolder = request.IsSingleHolder,
            MinimumMemberLevelId = request.MinimumMemberLevelId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.InvestigationDuties.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetAll), new { orgId }, ToRecord(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<InvestigationDutyRecord>> Update(
        Guid orgId, Guid id, [FromBody] UpsertInvestigationDutyRequest request, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("A duty needs a name.");

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (await MinimumLevelForeignAsync(db, orgId, request.MinimumMemberLevelId, ct))
            return BadRequest("That title does not belong to this group.");

        var entity = await db.InvestigationDuties
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();

        entity.Name = request.Name.Trim(); entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive; entity.IsSingleHolder = request.IsSingleHolder;
        entity.MinimumMemberLevelId = request.MinimumMemberLevelId;
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);

        entity.MinimumMemberLevel = request.MinimumMemberLevelId is { } lid
            ? await db.OrganizationMemberLevels.AsNoTracking().FirstOrDefaultAsync(l => l.Id == lid, ct)
            : null;
        return Ok(ToRecord(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid id, CancellationToken ct)
    {
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.InvestigationDuties
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();

        var assignments = await db.InvestigationDutyAssignments
            .Where(x => x.InvestigationDutyId == id).ToListAsync(ct);
        db.InvestigationDutyAssignments.RemoveRange(assignments);
        db.InvestigationDuties.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>A minimum level from another group must never be attachable here.</summary>
    private static async Task<bool> MinimumLevelForeignAsync(
        BenDataContext db, Guid orgId, Guid? levelId, CancellationToken ct)
        => levelId is { } lid
        && !await db.OrganizationMemberLevels.AnyAsync(l => l.Id == lid && l.OrganizationId == orgId, ct);

    private static InvestigationDutyRecord ToRecord(InvestigationDuty d) => new()
    {
        Id = d.Id, OrganizationId = d.OrganizationId, Name = d.Name,
        SortOrder = d.SortOrder, IsActive = d.IsActive, IsSingleHolder = d.IsSingleHolder,
        MinimumMemberLevelId = d.MinimumMemberLevelId,
        Capabilities = d.Capabilities,
        IsEnforced = d.IsEnforced,
        MinimumMemberLevelName = d.MinimumMemberLevel?.Name,
    };

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
