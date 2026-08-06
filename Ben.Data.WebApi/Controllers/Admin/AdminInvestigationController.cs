using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Read-only cross-organization investigation listing for SuperAdmin (backlog item #2 —
/// SuperAdmin visibility into all cases and investigations). See <see cref="AdminCaseController"/>
/// for the case-level counterpart.
/// </summary>
[ApiController]
[Route("api/admin/investigations")]
[Authorize(Policy = RoleNames.SuperAdmin)]
public sealed class AdminInvestigationController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public AdminInvestigationController(IDbContextFactory<BenDataContext> db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminInvestigationSummaryRecord>>> GetAll(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var investigations = await db.Investigations.AsNoTracking()
            .Include(i => i.Case)
            .ThenInclude(c => c.Organization)
            .OrderByDescending(i => i.ScheduledDateTime)
            .ToListAsync(ct);

        return Ok(investigations.Select(i => new AdminInvestigationSummaryRecord
        {
            Id                = i.Id,
            CaseId            = i.CaseId,
            CaseReference     = $"#{i.Case.CaseYear}-{i.Case.OrgCaseNumber:D3}",
            OrganizationId    = i.Case.OrganizationId,
            OrganizationName  = i.Case.Organization.Name,
            Title             = i.Title,
            ScheduledDateTime = i.ScheduledDateTime,
            EndDateTime       = i.EndDateTime,
            Status            = i.Status,
        }));
    }
}
