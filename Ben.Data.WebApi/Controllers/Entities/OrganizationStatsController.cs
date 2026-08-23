using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Admin;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// One group's own numbers, for the panel on its Details tab.
/// </summary>
/// <remarks>
/// <para>The member count is baseline — the roster is a tab every member holds. The case and
/// investigation numbers follow the same gate as the tabs that list them (Ben, 2026-08-23:
/// "the gates count as tabs"): SuperAdmin, the Owner/Administrator bypass, or a role grant,
/// through the same <c>HasAccessAsync</c> chain <c>CaseController.CanReadAsync</c> uses.
/// Someone the Cases tab is hidden from cannot read how many cases there are either —
/// otherwise the count is a side channel that answers "is anything happening here" for work
/// you were not given.</para>
///
/// <para>The refused parts arrive as NULL, never zero: the panel hides a null widget, and a
/// zero would read as "an idle group", which is a lie.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/organizations/{orgId:guid}/stats")]
public sealed class OrganizationStatsController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IOrganizationSecurityService _security;

    public OrganizationStatsController(
        IDbContextFactory<BenDataContext> dbContextFactory, IOrganizationSecurityService security)
    {
        _dbContextFactory = dbContextFactory;
        _security         = security;
    }

    [HttpGet]
    public async Task<ActionResult<OrgStatsSummary>> Get(Guid orgId, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var isSuperAdmin = User.IsInRole(RoleNames.SuperAdmin);
        var userId       = GetCurrentUserId();
        if (!isSuperAdmin && userId == Guid.Empty) return Unauthorized();

        if (!isSuperAdmin)
        {
            var isMember = await db.OrganizationUserMemberships.AnyAsync(
                m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);

            if (!isMember) return Forbid();
        }

        var canReadCases = isSuperAdmin || await _security.HasAccessAsync(
            userId, orgId, OrganizationSecurityTable.Case, OrganizationSecurityAction.Read, ct);
        var canReadInvestigations = isSuperAdmin || await _security.HasAccessAsync(
            userId, orgId, OrganizationSecurityTable.Investigation, OrganizationSecurityAction.Read, ct);

        var members = await db.OrganizationUserMemberships
            .CountAsync(m => m.OrganizationId == orgId && m.IsActive, ct);

        var investigations = canReadInvestigations
            ? await db.Investigations.CountAsync(i => i.OrganizationId == orgId, ct)
            : (int?)null;

        if (!canReadCases)
        {
            return Ok(new OrgStatsSummary(
                Members: members, Cases: null, Investigations: investigations,
                OpenCases: null, CasesByStatus: null, CasesPerMonth: null));
        }

        var byStatus = await db.Cases
            .Where(c => c.OrganizationId == orgId)
            .GroupBy(c => c.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // A year of months. Grouped in the database by year and month, then shaped here — a
        // DateOnly per month is a chart's x-axis, but not something SQL Server will construct.
        var since = DateTime.UtcNow.Date.AddMonths(-11);
        var monthly = await db.Cases
            .Where(c => c.OrganizationId == orgId && c.DateCreated >= since)
            .GroupBy(c => new { c.DateCreated.Year, c.DateCreated.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(ct);

        var byMonth = monthly.ToDictionary(m => (m.Year, m.Month), m => m.Count);
        var start = new DateTime(since.Year, since.Month, 1);

        var casesPerMonth = Enumerable.Range(0, 12)
            .Select(offset =>
            {
                var month = start.AddMonths(offset);
                return new StatPoint(
                    DateOnly.FromDateTime(month),
                    byMonth.GetValueOrDefault((month.Year, month.Month)));
            })
            .ToList();

        // "Open" is everything that has not reached a resting state. Closed and Transferred are
        // the two that mean nobody is expected to act; the rest are live work.
        var openStatuses = new[] { CaseStatus.Closed, CaseStatus.Transferred, CaseStatus.Paused };

        return Ok(new OrgStatsSummary(
            Members: members,
            Cases: byStatus.Sum(s => s.Count),
            Investigations: investigations,
            OpenCases: byStatus.Where(s => !openStatuses.Contains(s.Key)).Sum(s => s.Count),
            CasesByStatus: byStatus
                .OrderBy(s => s.Key)
                .Select(s => new StatSlice(s.Key.ToString(), s.Count))
                .ToList(),
            CasesPerMonth: casesPerMonth));
    }
}
