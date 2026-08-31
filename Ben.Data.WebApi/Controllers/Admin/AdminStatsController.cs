using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Aggregate numbers for the administrator's dashboard.
/// </summary>
/// <remarks>
/// <para>Read-only. The summary and the charts are count-only: every response is a shape —
/// totals, distributions, series — naming no person, case or address. That boundary is worth
/// keeping, because a dashboard answering "how much" needs no ability to answer "who", and it
/// stops this controller becoming a second route to data the rest of the API gates carefully.</para>
///
/// <para><b>The sign-in insights are the deliberate exception</b> (Ben, 2026-08-31). "Who signed
/// in last" and "who is failing to" cannot be answered by a shape, and an administrator watching
/// a launch needs both. What they name is bounded on purpose: an account, a count, and a time —
/// never what that account then did. Nothing here reaches a case, an address or a group's
/// contents, so the exception widens what the dashboard says about sign-ins and nothing else.</para>
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

    /// <summary>How many distinct accounts the "who has been here" list shows.</summary>
    private const int RecentPeople = 10;

    /// <summary>Failures below this are noise — a person mistyping their own password.</summary>
    private const int FailureFloor = 4;

    /// <summary>Silence longer than this makes a return worth remarking on.</summary>
    private const int DormantDays = 60;

    /// <summary>How many returning sleepers to name before the panel becomes a list.</summary>
    private const int TopWoke = 5;

    /// <summary>The UTC band called "the small hours", and the share of sign-ins that makes it
    /// worth mentioning at all.</summary>
    private const int SmallHoursFromUtc = 5;
    private const int SmallHoursToUtc = 10;
    private const int SmallHoursSharePercent = 25;

    /// <summary>How long a new account gets before never having signed in is worth counting.</summary>
    private const int NeverSignedInGraceDays = 7;

    /// <summary>A doubling means nothing off a base this small; two failures becoming five is a
    /// Tuesday, not a surge.</summary>
    private const int SurgeFloor = 10;

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


    /// <summary>
    /// Who has been signing in, and anything odd about how (Ben, 2026-08-31).
    /// </summary>
    /// <remarks>
    /// <para><b>This is the one endpoint here that names people</b>, and the class remarks explain
    /// why that is deliberate rather than a slip. Everything it names is an account and a count —
    /// never an address, a case, or anything the account did after arriving.</para>
    ///
    /// <para><b>Password and Apple only.</b> Entra sessions are bearer tokens validated
    /// per-request, so no moment in them is "the sign-in"; they are absent from every figure
    /// below and the response says so rather than letting a reader assume otherwise.</para>
    /// </remarks>
    [HttpGet("sign-ins")]
    public async Task<ActionResult<AdminSignInInsights>> GetSignInInsights(
        [FromQuery] int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 7, 365);

        await using var db = await _dbContextFactory.CreateDbContextAsync(ct);

        var now   = DateTime.UtcNow;
        var since = now.Date.AddDays(-(days - 1));

        // ── the last ten DISTINCT accounts to arrive ─────────────────────────
        // Grouped before taking, or one person signing in eleven times fills the whole list and
        // the panel answers "who is busy" instead of "who has been here".
        var recentPairs = await db.SignInEvents
            .Where(e => e.Succeeded && e.AppUserId != null)
            .GroupBy(e => e.AppUserId!.Value)
            .Select(g => new { AppUserId = g.Key, Utc = g.Max(x => x.Utc) })
            .OrderByDescending(x => x.Utc)
            .Take(RecentPeople)
            .ToListAsync(ct);

        var recentIds  = recentPairs.Select(p => p.AppUserId).ToList();
        var recentUtcs = recentPairs.Select(p => p.Utc).ToList();

        // Matched on the exact instant as well as the account, so this fetches about ten rows
        // rather than every sign-in those ten people have ever made.
        var recentMethods = await db.SignInEvents
            .Where(e => e.Succeeded && e.AppUserId != null
                     && recentIds.Contains(e.AppUserId.Value) && recentUtcs.Contains(e.Utc))
            .Select(e => new { e.AppUserId, e.Utc, e.Method })
            .ToListAsync(ct);

        // ── ranked accounts, successes and failures ──────────────────────────
        var topRaw = await db.SignInEvents
            .Where(e => e.Succeeded && e.AppUserId != null && e.Utc >= since)
            .GroupBy(e => e.AppUserId!.Value)
            .OrderByDescending(g => g.Count())
            .Take(TopN)
            .Select(g => new { AppUserId = g.Key, Count = g.Count(), Last = g.Max(x => x.Utc) })
            .ToListAsync(ct);

        var failRaw = await db.SignInEvents
            .Where(e => !e.Succeeded && e.AppUserId != null && e.Utc >= since)
            .GroupBy(e => e.AppUserId!.Value)
            .OrderByDescending(g => g.Count())
            .Take(TopN)
            .Select(g => new { AppUserId = g.Key, Count = g.Count(), Last = g.Max(x => x.Utc) })
            .ToListAsync(ct);

        // ── the shapes that need no names ────────────────────────────────────
        var byMethod = await db.SignInEvents
            .Where(e => e.Succeeded && e.Utc >= since)
            .GroupBy(e => e.Method)
            .Select(g => new { Method = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var byHour = await db.SignInEvents
            .Where(e => e.Succeeded && e.Utc >= since)
            .GroupBy(e => e.Utc.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // ── groups, by their members' sign-ins ───────────────────────────────
        // The membership side is fetched and joined in memory. Joining SignInEvents to
        // memberships in SQL is a many-to-many fan-out whose GroupBy EF has refused to translate
        // elsewhere in this controller (see TopGroupsByMembers), and the membership table is
        // small enough that the honest version costs less than fighting the translator.
        var signInsPerUser = await db.SignInEvents
            .Where(e => e.Succeeded && e.AppUserId != null && e.Utc >= since)
            .GroupBy(e => e.AppUserId!.Value)
            .Select(g => new { AppUserId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var memberships = await db.OrganizationUserMemberships
            .Where(m => m.IsActive)
            .Select(m => new { m.AppUserId, m.OrganizationId })
            .ToListAsync(ct);

        var perUser = signInsPerUser.ToDictionary(x => x.AppUserId, x => x.Count);

        var groupTotals = memberships
            .Where(m => perUser.ContainsKey(m.AppUserId))
            .GroupBy(m => m.OrganizationId)
            .Select(g => new { OrganizationId = g.Key, Count = g.Sum(m => perUser[m.AppUserId]) })
            .OrderByDescending(x => x.Count)
            .Take(TopN)
            .ToList();

        // ── put names on everything at once ──────────────────────────────────
        var namedIds = recentIds
            .Concat(topRaw.Select(x => x.AppUserId))
            .Concat(failRaw.Select(x => x.AppUserId))
            .Distinct()
            .ToList();

        var people = await db.AppUsers
            .Where(u => namedIds.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.Handle })
            .ToDictionaryAsync(u => u.Id, u => u, ct);

        string NameOf(Guid id) =>
            people.TryGetValue(id, out var u)
                ? (!string.IsNullOrWhiteSpace(u.DisplayName) ? u.DisplayName!
                   : !string.IsNullOrWhiteSpace(u.Handle) ? "@" + u.Handle
                   : "Account " + id.ToString()[..8])
                // A deleted account still has sign-in rows: SignInEvent has no FK to AppUser
                // precisely so the history survives the person. Saying so beats a blank cell.
                : "(deleted account)";

        string? HandleOf(Guid id) => people.TryGetValue(id, out var u) ? u.Handle : null;

        var groupOrgIds = groupTotals.Select(x => x.OrganizationId).ToList();
        var orgNames = await db.Organizations
            .Where(o => groupOrgIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name, ct);

        var recent = recentPairs
            .Select(p => new RecentSignIn(
                p.AppUserId, NameOf(p.AppUserId), HandleOf(p.AppUserId),
                recentMethods.FirstOrDefault(m => m.AppUserId == p.AppUserId && m.Utc == p.Utc)?.Method
                    ?? RecordingSignInManager.PasswordMethod,
                p.Utc))
            .ToList();

        var topPeople = topRaw
            .Select(x => new SignInPerson(
                x.AppUserId, NameOf(x.AppUserId), HandleOf(x.AppUserId), x.Count, x.Last))
            .ToList();

        var mostFailures = failRaw
            .Select(x => new SignInPerson(
                x.AppUserId, NameOf(x.AppUserId), HandleOf(x.AppUserId), x.Count, x.Last))
            .ToList();

        var oddities = await FindOddities(db, since, now, days, failRaw
            .ToDictionary(x => x.AppUserId, x => x.Count), NameOf, ct);

        return Ok(new AdminSignInInsights(
            Recent: recent,
            TopPeople: topPeople,
            TopGroups: groupTotals
                .Select(x => new StatSlice(orgNames.GetValueOrDefault(x.OrganizationId, "—"), x.Count))
                .ToList(),
            ByMethod: byMethod
                .OrderByDescending(x => x.Count)
                .Select(x => new StatSlice(MethodLabel(x.Method), x.Count))
                .ToList(),
            ByHourUtc: Enumerable.Range(0, 24)
                .Select(h => new StatSlice(
                    $"{h:00}:00",
                    byHour.FirstOrDefault(x => x.Hour == h)?.Count ?? 0))
                .ToList(),
            MostFailures: mostFailures,
            Oddities: oddities,
            CoversAppleSignIns: await db.SignInEvents
                .AnyAsync(e => e.Method == RecordingSignInManager.AppleMethod, ct)));
    }

    /// <summary>The method slug in the words an administrator would use.</summary>
    private static string MethodLabel(string method) => method switch
    {
        RecordingSignInManager.PasswordMethod => "Password",
        RecordingSignInManager.AppleMethod    => "Apple",
        _                                     => method,
    };

    /// <summary>
    /// The patterns worth a second look, most pointed first.
    /// </summary>
    /// <remarks>
    /// <para>Every one of these is phrased as an observation with its basis attached, never as a
    /// verdict. An administrator acting on "twelve failures against one account" can go and look;
    /// one acting on "account compromised" has been told something the data does not support.</para>
    ///
    /// <para>None of them needs an IP address, a user agent or a location, which is what keeps
    /// <c>SignInEvent</c> a counting table rather than a tracking one.</para>
    /// </remarks>
    private static async Task<List<SignInOddity>> FindOddities(
        BenDataContext db, DateTime since, DateTime now, int days,
        Dictionary<Guid, int> failuresByUser, Func<Guid, string> nameOf, CancellationToken ct)
    {
        var oddities = new List<SignInOddity>();

        // ── an account failing more than it succeeds ─────────────────────────
        // Being locked out and being probed look identical from here, and both are worth a look.
        if (failuresByUser.Count > 0)
        {
            var probedIds = failuresByUser.Keys.ToList();
            var successes = await db.SignInEvents
                .Where(e => e.Succeeded && e.AppUserId != null
                         && probedIds.Contains(e.AppUserId.Value) && e.Utc >= since)
                .GroupBy(e => e.AppUserId!.Value)
                .Select(g => new { AppUserId = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var successByUser = successes.ToDictionary(x => x.AppUserId, x => x.Count);

            foreach (var (userId, failures) in failuresByUser
                         .Where(kv => kv.Value >= FailureFloor)
                         .OrderByDescending(kv => kv.Value))
            {
                var succeeded = successByUser.GetValueOrDefault(userId);
                if (failures <= succeeded) continue;

                oddities.Add(new SignInOddity(
                    "failures",
                    $"{nameOf(userId)} failed to sign in {failures} times",
                    succeeded == 0
                        ? $"No successful sign-in in the last {days} days — locked out, or somebody "
                          + "else is guessing."
                        : $"Against {succeeded} success{(succeeded == 1 ? "" : "es")} in the same period.",
                    userId));
            }
        }

        // ── a dormant account that woke up ───────────────────────────────────
        // The signal is the GAP, not the sign-in: an account silent since spring that arrives
        // this week is either good news or somebody else's news.
        var wokeCutoff = since.AddDays(-DormantDays);
        var activeIds = await db.SignInEvents
            .Where(e => e.Succeeded && e.AppUserId != null && e.Utc >= since)
            .Select(e => e.AppUserId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var priorLast = await db.SignInEvents
            .Where(e => e.Succeeded && e.AppUserId != null
                     && activeIds.Contains(e.AppUserId.Value) && e.Utc < since)
            .GroupBy(e => e.AppUserId!.Value)
            .Select(g => new { AppUserId = g.Key, Last = g.Max(x => x.Utc) })
            .ToListAsync(ct);

        // Capped, and longest-silence first. A migration or a mailshot can wake a hundred accounts
        // at once, and a hundred rows saying the same thing is not a panel anybody reads — the
        // five deepest sleepers carry the signal.
        foreach (var prior in priorLast
                     .Where(p => p.Last < wokeCutoff)
                     .OrderBy(p => p.Last)
                     .Take(TopWoke))
        {
            var gap = (int)(since - prior.Last).TotalDays;
            oddities.Add(new SignInOddity(
                "woke",
                $"{nameOf(prior.AppUserId)} came back after {gap} quiet days",
                $"Last seen {prior.Last:MM/dd/yyyy} before signing in during this period.",
                prior.AppUserId));
        }

        // ── sign-ins in the small hours ──────────────────────────────────────
        // Named as UTC, because that is what is recorded. Calling it "overnight" would be a claim
        // about where somebody lives that the row does not make.
        var smallHours = await db.SignInEvents
            .CountAsync(e => e.Succeeded && e.Utc >= since
                          && e.Utc.Hour >= SmallHoursFromUtc && e.Utc.Hour < SmallHoursToUtc, ct);
        var allSignIns = await db.SignInEvents.CountAsync(e => e.Succeeded && e.Utc >= since, ct);

        if (allSignIns > 0 && smallHours * 100 / allSignIns >= SmallHoursSharePercent)
        {
            oddities.Add(new SignInOddity(
                "hours",
                $"{smallHours * 100 / allSignIns}% of sign-ins fell between "
                    + $"{SmallHoursFromUtc:00}:00 and {SmallHoursToUtc:00}:00 UTC",
                $"{smallHours:N0} of {allSignIns:N0}. Worth knowing for a site whose users work at "
                    + "night — unremarkable if that is who they are.",
                null));
        }

        // ── accounts that registered and never arrived ───────────────────────
        // The funnel's quietest failure: they are not signing in badly, they are not signing in.
        var staleCutoff = now.AddDays(-NeverSignedInGraceDays);
        var everSignedIn = db.SignInEvents.Where(e => e.Succeeded && e.AppUserId != null)
            .Select(e => e.AppUserId!.Value);
        var neverArrived = await db.AppUsers
            .CountAsync(u => u.DateCreated < staleCutoff && !everSignedIn.Contains(u.Id), ct);

        if (neverArrived > 0)
        {
            oddities.Add(new SignInOddity(
                "never",
                $"{neverArrived:N0} account{(neverArrived == 1 ? "" : "s")} have never signed in",
                $"Registered more than {NeverSignedInGraceDays} days ago and never came back. "
                    + "Not a security signal — a funnel one.",
                null));
        }

        // ── failures rising against the period before ────────────────────────
        var priorSince = since.AddDays(-days);
        var failuresNow = await db.SignInEvents.CountAsync(e => !e.Succeeded && e.Utc >= since, ct);
        var failuresBefore = await db.SignInEvents
            .CountAsync(e => !e.Succeeded && e.Utc >= priorSince && e.Utc < since, ct);

        if (failuresBefore >= SurgeFloor && failuresNow > failuresBefore * 2)
        {
            oddities.Add(new SignInOddity(
                "surge",
                $"Failed sign-ins more than doubled — {failuresBefore:N0} to {failuresNow:N0}",
                $"Comparing these {days} days with the {days} before them.",
                null));
        }

        return oddities;
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
