using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>Org-side: propose investigation dates and track client responses.</summary>
[ApiController]
[Route("api/orgs/{orgId:guid}/cases/{caseId:guid}/schedule-proposals")]
[Authorize]
public sealed class ScheduleProposalController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public ScheduleProposalController(IDbContextFactory<BenDataContext> db,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    { _db = db; _security = security; }

    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ScheduleProposalDto>>> GetAll(Guid orgId, Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Read, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var proposals = await db.InvestigationScheduleProposals.AsNoTracking()
            .Include(p => p.Slots)
            .Where(p => p.CaseId == caseId)
            .OrderByDescending(p => p.DateCreated)
            .ToListAsync(ct);

        return Ok(proposals.Select(ToDto));
    }

    [HttpPost]
    public async Task<ActionResult<ScheduleProposalDto>> Create(
        Guid orgId, Guid caseId, [FromBody] CreateProposalRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Create, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        if (request.Slots is null || request.Slots.Count == 0) return BadRequest("At least one proposed slot is required.");

        var proposal = new InvestigationScheduleProposal
        {
            Id = Guid.NewGuid(), CaseId = caseId, Notes = request.Notes?.Trim(),
            Status = ScheduleProposalStatus.Pending,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.InvestigationScheduleProposals.Add(proposal);

        var order = 10;
        foreach (var slot in request.Slots)
        {
            db.ScheduleProposalSlots.Add(new ScheduleProposalSlot
            {
                Id = Guid.NewGuid(), ProposalId = proposal.Id,
                StartDateTime = slot.StartDateTime, EndDateTime = slot.EndDateTime,
                SortOrder = order,
            });
            order += 10;
        }
        await db.SaveChangesAsync(ct);

        var saved = await db.InvestigationScheduleProposals.AsNoTracking()
            .Include(p => p.Slots)
            .FirstAsync(p => p.Id == proposal.Id, ct);
        return Ok(ToDto(saved));
    }

    [HttpDelete("{proposalId:guid}")]
    public async Task<IActionResult> Withdraw(Guid orgId, Guid caseId, Guid proposalId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationPermissionArea.Cases, OrganizationSecurityAction.Delete, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var proposal = await db.InvestigationScheduleProposals
            .FirstOrDefaultAsync(p => p.Id == proposalId && p.CaseId == caseId, ct);
        if (proposal is null) return NotFound();

        proposal.Status = ScheduleProposalStatus.Withdrawn;
        proposal.DateUpdated = DateTime.UtcNow;
        proposal.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Org manually converts a pending proposal to an Investigation (bypasses client acceptance).</summary>
    [HttpPost("{proposalId:guid}/convert")]
    public async Task<ActionResult<ScheduleProposalDto>> Convert(
        Guid orgId, Guid caseId, Guid proposalId, [FromBody] ConvertProposalRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationPermissionArea.Investigations, OrganizationSecurityAction.Create, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var proposal = await db.InvestigationScheduleProposals.Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.Id == proposalId && p.CaseId == caseId, ct);
        if (proposal is null) return NotFound();

        var slot = proposal.Slots.FirstOrDefault(s => s.Id == request.SlotId)
                ?? proposal.Slots.OrderBy(s => s.SortOrder).First();

        var investigation = new Investigation
        {
            // OrganizationId is a direct FK and is NOT derived from the case at read time, so
            // omitting it here left the investigation owned by Guid.Empty — belonging to no group,
            // absent from every org-scoped list, while the proposal happily showed "Converted".
            // The one investigation-creating path that had this wrong was the one the CLIENT
            // starts by accepting a date.
            Id = Guid.NewGuid(), OrganizationId = orgId, CaseId = caseId,
            Title = request.Title?.Trim() ?? "Investigation",
            ScheduledDateTime = slot.StartDateTime,
            EndDateTime = slot.EndDateTime,
            Status = InvestigationStatus.Scheduled,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.Investigations.Add(investigation);

        proposal.Status = ScheduleProposalStatus.Converted;
        proposal.InvestigationId = investigation.Id;
        proposal.DateUpdated = DateTime.UtcNow;
        proposal.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(ToDto(proposal));
    }

    /// <summary>Whether the caller may take <paramref name="action"/> in the given area.</summary>
    /// <remarks>
    /// Was <c>IsOrgMember</c>, asking <c>Case.Read</c> for every endpoint — so a member who could
    /// only read a case could send the client date proposals in the group's name, withdraw them,
    /// and convert one into a scheduled investigation. Converting creates an investigation, so it
    /// is asked of the Investigations area rather than Cases: it is the same act as scheduling one
    /// directly, and should need the same grant.
    /// </remarks>
    private Task<bool> MayAsync(Guid orgId, OrganizationPermissionArea area,
        OrganizationSecurityAction action, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
            ? Task.FromResult(true)
            : _security.MayAsync(GetCurrentUserId(), orgId, area, action, ct);

    private static ScheduleProposalDto ToDto(InvestigationScheduleProposal p) => new(
        p.Id, p.CaseId, p.Status, p.Notes, p.AcceptedSlotId,
        p.ClientCounterDateTime, p.ClientResponseNotes, p.ClientRespondedAt,
        p.InvestigationId, p.DateCreated,
        p.Slots.OrderBy(s => s.SortOrder).Select(s => new SlotDto(s.Id, s.StartDateTime, s.EndDateTime, s.SortOrder)).ToList());
}

public sealed record CreateProposalRequest(string? Notes, IReadOnlyList<SlotInput> Slots);
public sealed record SlotInput(DateTime StartDateTime, DateTime? EndDateTime);
public sealed record ConvertProposalRequest(Guid? SlotId, string? Title);

public sealed record ScheduleProposalDto(
    Guid                                            Id,
    Guid                                            CaseId,
    Ben.Data.Common.Enums.ScheduleProposalStatus    Status,
    string?                                         Notes,
    Guid?                                           AcceptedSlotId,
    DateTime?                                       ClientCounterDateTime,
    string?                                         ClientResponseNotes,
    DateTime?                                       ClientRespondedAt,
    Guid?                                           InvestigationId,
    DateTime                                        DateCreated,
    IReadOnlyList<SlotDto>                          Slots);

public sealed record SlotDto(Guid Id, DateTime StartDateTime, DateTime? EndDateTime, int SortOrder);
