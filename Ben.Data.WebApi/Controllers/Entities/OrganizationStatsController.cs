using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// One group's own numbers, for the panel on its Details tab.
/// </summary>
/// <remarks>
/// <para>Gated on being able to read that group's cases, not on any administrator role: these are
/// counts of things a member can already open one by one in the tabs beside the panel. Someone
/// who cannot read the cases cannot read how many there are either — otherwise the count becomes
/// a side channel that answers "is anything happening here" for a group you were removed from.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/organizations/{orgId:guid}/stats")]
public sealed class OrganizationStatsController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;

    public OrganizationStatsController(IDbContextFactory<BenDataContext> dbContextFactory)
        => _dbContextFactory = dbContextFactory;

    [HttpGet]
    public async Task<ActionResult<OrgStatsSummary>> Get(Guid orgId, CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        // Active membership, or SuperAdmin — the same bar CaseController.CanReadAsync sets for
        // reading the cases these numbers count. Cases have no OrganizationSecurityTable entry,
        // so membership is the check, not a grant.
        if (!User.IsInRole(RoleNames.SuperAdmin))
        {
            var userId = GetCurrentUserId();
            if (userId == Guid.Empty) return Unauthorized();

            var isMember = await db.OrganizationUserMemberships.AnyAsync(
                m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);

            if (!isMember) return Forbid();
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
        var openStatuses = new[] { CaseStatus.Closed, CaseStatus.Transferred };

        return Ok(new OrgStatsSummary(
            Members: await db.OrganizationUserMemberships
                .CountAsync(m => m.OrganizationId == orgId && m.IsActive, ct),
            Cases: byStatus.Sum(s => s.Count),
            Investigations: await db.Investigations.CountAsync(i => i.OrganizationId == orgId, ct),
            OpenCases: byStatus.Where(s => !openStatuses.Contains(s.Key)).Sum(s => s.Count),
            CasesByStatus: byStatus
                .OrderBy(s => s.Key)
                .Select(s => new StatSlice(s.Key.ToString(), s.Count))
                .ToList(),
            CasesPerMonth: casesPerMonth));
    }
}
