using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Aggregate numbers for the administrator's dashboard.
/// </summary>
/// <remarks>
/// <para>Read-only and count-only. Every response here is a shape — totals, distributions,
/// series — and none of it names a person, a case or an address. That is a deliberate boundary:
/// a dashboard answering "how much" needs no ability to answer "who", and keeping it that way
/// means this controller never becomes a second route to data the rest of the API gates
/// carefully.</para>
///
/// <para>SuperAdmin, because the numbers span every organization. A group's own figures live on
/// the organization stats endpoint, where group membership is the bar.</para>
///
/// <para><b>On "visitors".</b> There is no anonymous-traffic count here and cannot be: nothing
/// records people who are not signed in. Answering "new versus returning visitors" would mean
/// building page-view tracking, with the retention and privacy questions that follow — a
/// separate decision, not a chart. What is here counts registrations, sign-ins and the accounts
/// behind them, which is the half the platform can answer honestly.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/stats")]
public sealed class AdminStatsController : BenControllerBase
{
    /// <summary>How many rows a "top N" chart returns. Beyond this a bar chart is a wall.</summary>
    private const int TopN = 8;

    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;

    public AdminStatsController(IDbContextFactory<BenDataContext> dbContextFactory)
        => _dbContextFactory = dbContextFactory;

    /// <summary>The headline counts.</summary>
    [HttpGet("summary")]
    public async Task<ActionResult<AdminStatsSummary>> GetSummary(CancellationToken ct)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var weekAgo = DateTime.UtcNow.AddDays(-7);

        return Ok(new AdminStatsSummary(
            People: await db.AppUsers.CountAsync(ct),
            PeopleInAGroup: await db.OrganizationUserMemberships
                .Where(m => m.IsActive)
                .Select(m => m.AppUserId)
                .Distinct()
                .CountAsync(ct),
            Groups: await db.Organizations.CountAsync(ct),
            Cases: await db.Cases.CountAsync(ct),
            Investigations: await db.Investigations.CountAsync(ct),
            NewPeopleThisWeek: await db.AppUsers.CountAsync(u => u.DateCreated >= weekAgo, ct),
            NewCasesThisWeek: await db.Cases.CountAsync(c => c.DateCreated >= weekAgo, ct),
            SignInsThisWeek: await db.SignInEvents
                .CountAsync(e => e.Succeeded && e.Utc >= weekAgo, ct),
            // Distinct accounts, not attempts — someone signing in from three devices is one
            // active person, and conflating the two makes a quiet week look busy.
            ActivePeopleThisWeek: await db.SignInEvents
                .Where(e => e.Succeeded && e.Utc >= weekAgo && e.AppUserId != null)
                .Select(e => e.AppUserId)
                .Distinct()
                .CountAsync(ct)));
    }

    /// <summary>The charts, over a window of days.</summary>
    [HttpGet("charts")]
    public async Task<ActionResult<AdminStatsCharts>> GetCharts(
        [FromQuery] int days = 30, CancellationToken ct = default)
    {
        // Clamped rather than trusted: an unbounded window is a table scan someone can ask for
        // from a query string.
        days = Math.Clamp(days, 7, 365);

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var since = DateTime.UtcNow.Date.AddDays(-(days - 1));

        var signIns = await db.SignInEvents
            .Where(e => e.Succeeded && e.Utc >= since)
            .GroupBy(e => e.Utc.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var registrations = await db.AppUsers
            .Where(u => u.DateCreated >= since)
            .GroupBy(u => u.DateCreated.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var casesByStatus = await db.Cases
            .GroupBy(c => c.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // Count by id first, then put names on the winners. OrganizationUserMembership has no
        // navigation property to dot through, and joining before grouping produced a shape EF
        // could not translate at all ("The LINQ expression ... could not be translated"). Grouping
        // on the raw id is trivially translatable, and the second query fetches at most TopN rows.
        var memberCounts = await db.OrganizationUserMemberships
            .Where(m => m.IsActive)
            .GroupBy(m => m.OrganizationId)
            .Select(g => new { OrganizationId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(TopN)
            .ToListAsync(ct);

        var memberOrgIds = memberCounts.Select(x => x.OrganizationId).ToList();
        var orgNames = await db.Organizations
            .Where(o => memberOrgIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name, ct);

        var topGroupsByMembers = memberCounts
            .Select(x => new StatSlice(orgNames.GetValueOrDefault(x.OrganizationId, "—"), x.Count))
            .ToList();

        // "Activity" is cases plus investigations in the window. Two queries and a merge rather
        // than a union, because the two tables reach an organization by different routes and the
        // translated SQL for a union of them is worse than doing the addition here.
        var caseActivity = await db.Cases
            .Where(c => c.DateCreated >= since)
            .GroupBy(c => c.Organization.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var investigationActivity = await db.Investigations
            .Where(i => i.DateCreated >= since)
            .GroupBy(i => i.Organization.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var activity = caseActivity.Concat(investigationActivity)
            .GroupBy(x => x.Name)
            .Select(g => new StatSlice(g.Key, g.Sum(x => x.Count)))
            .OrderByDescending(s => s.Count)
            .Take(TopN)
            .ToList();

        return Ok(new AdminStatsCharts(
            SignInsPerDay: FillDays(signIns.Select(x => (x.Day, x.Count)), since, days),
            RegistrationsPerDay: FillDays(registrations.Select(x => (x.Day, x.Count)), since, days),
            CasesByStatus: casesByStatus
                .OrderBy(x => x.Key)
                .Select(x => new StatSlice(x.Key.ToString(), x.Count))
                .ToList(),
            TopGroupsByMembers: topGroupsByMembers,
            TopGroupsByActivity: activity,
            TopStatesByUser: await TopStatesAsync(db.UserAddresses.Select(a => a.State), ct),
            TopStatesByCase: await TopStatesAsync(db.Cases.Select(c => c.State), ct),
            TopStatesByInvestigation: await TopStatesAsync(
                db.Investigations.Where(i => i.Place != null).Select(i => i.Place!.State!), ct)));
    }

    /// <summary>Counts a state column, busiest first, blanks discarded.</summary>
    /// <remarks>
    /// Ordered by the aggregate itself rather than by a property of the projected record. EF can
    /// translate <c>OrderByDescending(g =&gt; g.Count())</c>; projecting into StatSlice first and
    /// ordering by <c>s.Count</c> gives it a record property to sort on, which it cannot turn into
    /// SQL — it fails at runtime, not at compile time.
    /// </remarks>
    private static async Task<IReadOnlyList<StatSlice>> TopStatesAsync(
        IQueryable<string> states, CancellationToken ct)
    {
        var rows = await states
            .Where(s => s != null && s != "")
            .GroupBy(s => s)
            .OrderByDescending(g => g.Count())
            .Take(TopN)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.Select(r => new StatSlice(r.State, r.Count)).ToList();
    }

    /// <summary>
    /// Turns sparse day-groups into one point per day across the window.
    /// </summary>
    /// <remarks>
    /// A line chart that skips empty days lies about its own shape: three sign-ins on Monday and
    /// three on Friday draw as a flat line unless the four quiet days in between are present as
    /// zeroes.
    /// </remarks>
    private static IReadOnlyList<StatPoint> FillDays(
        IEnumerable<(DateTime Day, int Count)> rows, DateTime since, int days)
    {
        var byDay = rows.ToDictionary(r => DateOnly.FromDateTime(r.Day), r => r.Count);
        var start = DateOnly.FromDateTime(since);

        return Enumerable.Range(0, days)
            .Select(offset =>
            {
                var day = start.AddDays(offset);
                return new StatPoint(day, byDay.GetValueOrDefault(day));
            })
            .ToList();
    }
}
