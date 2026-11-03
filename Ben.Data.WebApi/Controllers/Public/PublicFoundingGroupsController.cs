using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Promotions;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The founding groups promotion for anybody (11/02/2026): what the promotion page, the pricing notice and the
/// home page's shout-out read. Anonymous, because the visitor who matters here hasn't signed up yet.
/// </summary>
/// <remarks>
/// While the promotion is switched off this says so and nothing else, so no page can show half of it. The
/// leaderboard appears once a second group has qualified (Ben: "we don't make the leaderboard live until we have
/// more than one group"), ranked by points per member per month so a group that joined later compares fairly.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/founding-groups")]
public sealed class PublicFoundingGroupsController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;

    public PublicFoundingGroupsController(IDbContextFactory<BenDataContext> dbFactory) { _dbFactory = dbFactory; }

    [HttpGet]
    public async Task<ActionResult<FoundingGroupsPublicRecord>> Get(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var p = await FoundingGroups.GetAsync(db, ct);
        var now = DateTime.UtcNow;
        var taken = await FoundingGroups.SpotsTakenAsync(db, p, ct);
        var showing = p.IsOpen && p.OpenedUtc is not null;

        if (!showing)
            return Ok(new FoundingGroupsPublicRecord(false, false, p.Spots, p.Spots, null, 0, 0, 0, 0, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, [], null, [], false));

        var tiers = await db.SubscriptionTiers.AsNoTracking().Include(t => t.Prices)
            .Where(t => t.IsActive && t.IsBandedByMembers).OrderBy(t => t.MinMembers).ToListAsync(ct);
        var plans = tiers.Where(t => t.MaxMembers is null || t.MaxMembers >= p.MinimumMembers)
            .Select(t => new FoundingGroupsPlanRecord(t.Name, Math.Max(t.MinMembers, p.MinimumMembers), t.MaxMembers,
                t.Prices.Where(x => x.IsActive && x.Interval == BillingInterval.Monthly).Select(x => (decimal?)x.Price).FirstOrDefault()))
            .ToList();

        var qualified = await db.PromotionEntries.AsNoTracking()
            .Where(e => e.PromotionId == p.Id && e.State == PromotionEntryState.Qualified)
            .Include(e => e.Organization).ToListAsync(ct);

        List<FoundingGroupsStandingRecord>? board = null;
        if (qualified.Count >= 2)
        {
            var rows = new List<(string Name, string? Url, decimal Score, int Months, int? Final)>();
            foreach (var e in qualified)
            {
                var window = FoundingGroupsScore.Window(e, now)!.Value;
                var score = await FoundingGroupsScore.ScoreAsync(db, p, e.OrganizationId, window.From, window.To, ct);
                var months = (int)Math.Floor((window.To - window.From).TotalDays / 30.4375);
                rows.Add((e.Organization.Name, e.Organization.UrlName,
                    FoundingGroupsScore.PerMemberPerMonth(score, window.From, window.To), months, e.FinalRank));
            }
            board = [.. rows
                .OrderBy(r => r.Final ?? int.MaxValue).ThenByDescending(r => r.Score).ThenBy(r => r.Name)
                .Select((r, i) => new FoundingGroupsStandingRecord(i + 1, r.Name, r.Url, r.Score, r.Months, r.Final))];
        }

        var winners = await (
            from m in db.OrganizationFreeMonths.AsNoTracking()
            join o in db.Organizations.AsNoTracking() on m.OrganizationId equals o.Id
            where m.Reason == FreeMonthReason.GroupOfTheMonth && m.RevokedUtc == null
            orderby m.EarnedUtc descending
            select new { m.Note, m.Key, o.Name, o.UrlName }).Take(12).ToListAsync(ct);

        return Ok(new FoundingGroupsPublicRecord(
            Showing: true,
            ClaimsOpen: FoundingGroups.ClaimsOpen(p, taken, now),
            Spots: p.Spots,
            SpotsLeft: Math.Max(0, p.Spots - taken),
            ClaimsCloseUtc: FoundingGroups.ClaimsCloseUtc(p),
            p.GatherDays, p.MinimumMembers, p.FreeMonths, p.Winners, p.WinnerMonths, p.GroupOfTheMonthMonths,
            p.ReachedScoreMonths, p.ReachableScorePerMember, p.PointsPerSession, p.PointsPerInvestigation,
            p.PointsPerEvent, p.PointsPerEvidence, p.PointsPerReferral, p.MinimumReadingsPerSession,
            plans, board,
            [.. winners.Select(w => new FoundingGroupsMonthWinnerRecord(MonthOf(w.Key), w.Name, w.UrlName))],
            p.DecidedUtc is not null));
    }

    /// <summary>The month in a group-of-the-month key, "month:{promotion}:{yyyy-MM}".</summary>
    private static DateTime MonthOf(string key)
        => DateTime.TryParseExact(key[(key.LastIndexOf(':') + 1)..] + "-01", "yyyy-MM-dd",
               System.Globalization.CultureInfo.InvariantCulture,
               System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d)
            ? d : default;
}
