using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Read-only cross-organization case listing for SuperAdmin (backlog item #2 — SuperAdmin
/// visibility into all cases). Org-scoped case reads already bypass membership checks for
/// SuperAdmin (see CaseController.CanReadAsync); this fills the gap of not having to already
/// know which org to look in.
/// </summary>
[ApiController]
[Route("api/admin/cases")]
[Authorize(Policy = RoleNames.SuperAdmin)]
public sealed class AdminCaseController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public AdminCaseController(IDbContextFactory<BenDataContext> db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AdminCaseSummaryRecord>>> GetAll(
        CancellationToken ct, [FromQuery] int? page = null, [FromQuery] int? pageSize = null)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var cases = await db.Cases.AsNoTracking()
            .Include(c => c.Organization)
            .OrderByDescending(c => c.DateCaseOpened)
            .ToListAsync(ct);

        var records = cases.Select(c => new AdminCaseSummaryRecord
        {
            Id               = c.Id,
            OrganizationId   = c.OrganizationId,
            OrganizationName = c.Organization.Name,
            Title            = c.Title,
            CaseYear         = c.CaseYear,
            OrgCaseNumber    = c.OrgCaseNumber,
            Status           = c.Status,
            City             = c.City,
            State            = c.State,
            DateCaseOpened   = c.DateCaseOpened,
            DateCaseClosed   = c.DateCaseClosed,
        }).ToList();
        return Ok(ListPaging.Apply(records, page, pageSize, Response));
    }
}
