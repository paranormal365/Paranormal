using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Mail;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Promotions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Keeps the founding groups promotion moving, and referral months coming (Ben, 11/02/2026). Every step is safe
/// to repeat: the job runs every few minutes and each thing it gives is keyed, stamped or stated once.
/// </summary>
/// <remarks>
/// In order: counts members during each group's week and ends the week; moves a group that grew onto its bigger
/// plan, still free; decides each month's group of the month; freezes each group's score when its twelve months
/// end and gives the reachable score's months; decides the top three once every group is done; warns a month and a
/// week before a free year ends; earns referral months; and adds every waiting month to plans that are running.
/// Referral months and waiting months run whether or not the promotion is open.
/// </remarks>
public sealed class FoundingGroupsJob : IScheduledJob
{
    public string Name => "founding-groups";

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly PlatformMessageService _messages;
    private readonly SiteIdentity _site;
    private readonly ILogger<FoundingGroupsJob> _logger;

    public FoundingGroupsJob(
        IDbContextFactory<BenDataContext> dbFactory, PlatformMessageService messages,
        IOptions<SiteIdentity> site, ILogger<FoundingGroupsJob> logger)
    {
        _dbFactory = dbFactory;
        _messages = messages;
        _site = site.Value;
        _logger = logger;
    }

    public Task RunAsync(CancellationToken ct) => RunAsync(DateTime.UtcNow, ct);

    /// <summary>One pass, as of <paramref name="now"/> (a test can move the clock).</summary>
    public async Task RunAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var p = await FoundingGroups.GetAsync(db, ct);

        if (p.OpenedUtc is not null)
        {
            await GatherAsync(db, p, now, ct);
            await GrowAsync(db, now, ct);
            await GroupOfTheMonthAsync(db, p, now, ct);
            await YearsEndAsync(db, p, now, ct);
            await DecideAsync(db, p, now, ct);
            await YearEndNoticesAsync(db, now, ct);
        }

        var earned = await FreeMonths.EarnReferralMonthsAsync(db, now, ct);
        var given = db.ChangeTracker.Entries<OrganizationFreeMonths>()
            .Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToList();
        await db.SaveChangesAsync(ct);
        foreach (var m in given)
            await TellAsync(db, m.OrganizationId, "You earned a free month",
                $"{m.Note} Your group gets a free month on its plan.", ct);
        if (earned > 0) _logger.LogInformation("Earned {Count} referral month(s).", earned);

        await FreeMonths.ApplyWaitingAsync(db, now, ct);
    }

    // ── the week ──────────────────────────────────────────────────────────────

    private async Task GatherAsync(BenDataContext db, Promotion p, DateTime now, CancellationToken ct)
    {
        var gathering = await db.PromotionEntries
            .Where(e => e.PromotionId == p.Id && e.State == PromotionEntryState.Gathering).ToListAsync(ct);

        foreach (var entry in gathering)
        {
            try
            {
                if (now < entry.GatherEndsUtc)
                {
                    await FoundingGroups.CountMembersAsync(db, entry, now, ct);
                    if (entry.DayLeftSentUtc is null && entry.GatherEndsUtc - now <= TimeSpan.FromDays(1))
                    {
                        entry.DayLeftSentUtc = now;
                        await db.SaveChangesAsync(ct);
                        var counted = await db.PromotionMembers.CountAsync(c => c.PromotionEntryId == entry.Id, ct);
                        await TellAsync(db, entry.OrganizationId, "One day left to gather your founding group",
                            $"Your group's week ends on {entry.GatherEndsUtc:MM/dd/yyyy} at {entry.GatherEndsUtc:h:mm tt} UTC. "
                            + $"{Counted(counted)} so far. Members count once they've confirmed their email address and aren't "
                            + $"in another group. You need at least {p.MinimumMembers}. {Link("/founding-groups/mine")}", ct);
                    }
                    continue;
                }

                // A last count, as of the week's end, then the verdict.
                await FoundingGroups.CountMembersAsync(db, entry, entry.GatherEndsUtc, ct);
                var qualified = await FoundingGroups.EndTheWeekAsync(db, p, entry, now, ct);
                await db.SaveChangesAsync(ct);

                if (qualified)
                {
                    var tier = entry.SubscriptionTierIdEarned is { } t
                        ? await db.SubscriptionTiers.AsNoTracking().Where(x => x.Id == t).Select(x => x.Name).FirstOrDefaultAsync(ct)
                        : null;
                    await TellAsync(db, entry.OrganizationId, "Your founding group's free year has started",
                        $"{Counted(entry.MembersAtQualifying)}, so your group is on the {tier ?? "plan its size earns"} plan free "
                        + $"until {entry.FreeYearEndsUtc:MM/dd/yyyy}. It moves up for free if you grow. Your score counts from today: "
                        + $"field sessions, investigations, events and evidence, per member. {Link("/founding-groups/mine")}", ct);
                }
                else
                {
                    await TellAsync(db, entry.OrganizationId, "Your founding group's week has ended",
                        $"Your group's week ended with fewer than {p.MinimumMembers} members counted, so its spot has gone back. "
                        + "If spots are still open, you're welcome to try again with another group.", ct);
                }
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                _logger.LogWarning(ex, "Founding groups: could not move entry {Entry} on.", entry.Id);
            }
        }
    }

    private async Task GrowAsync(BenDataContext db, DateTime now, CancellationToken ct)
    {
        var growing = await db.PromotionEntries
            .Where(e => e.State == PromotionEntryState.Qualified && e.FreeYearEndsUtc > now).ToListAsync(ct);
        foreach (var entry in growing)
        {
            if (!await FoundingGroups.GrowAsync(db, entry, now, ct)) continue;
            await db.SaveChangesAsync(ct);
            var tier = await db.OrganizationSubscriptions.AsNoTracking()
                .Where(s => s.OrganizationId == entry.OrganizationId).Select(s => s.SubscriptionTier!.Name).FirstOrDefaultAsync(ct);
            await TellAsync(db, entry.OrganizationId, "Your group moved up a plan, still free",
                $"Your group has grown, so it's now on the {tier} plan, free for the rest of its year.", ct);
        }
    }

    // ── the prizes ────────────────────────────────────────────────────────────

    /// <summary>Each finished calendar month: the best per-member score that month, not the same group twice running.</summary>
    private async Task GroupOfTheMonthAsync(BenDataContext db, Promotion p, DateTime now, CancellationToken ct)
    {
        var entries = await db.PromotionEntries.AsNoTracking()
            .Where(e => e.PromotionId == p.Id && e.State == PromotionEntryState.Qualified).ToListAsync(ct);
        if (entries.Count == 0) return;

        var first = entries.Min(e => e.QualifiedUtc!.Value);
        var month = p.GroupOfTheMonthDecidedThroughUtc?.AddMonths(1) ?? new DateTime(first.Year, first.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        while (month.AddMonths(1) <= now)
        {
            var monthEnd = month.AddMonths(1);
            var candidates = new List<(PromotionEntry Entry, FoundingGroupsScore.GroupScore Score)>();
            foreach (var e in entries.Where(e => e.QualifiedUtc < monthEnd && e.FreeYearEndsUtc > month))
            {
                var from = e.QualifiedUtc!.Value > month ? e.QualifiedUtc.Value : month;
                var to = e.FreeYearEndsUtc!.Value < monthEnd ? e.FreeYearEndsUtc.Value : monthEnd;
                candidates.Add((e, await FoundingGroupsScore.ScoreAsync(db, p, e.OrganizationId, from, to, ct)));
            }

            var lastWinner = await db.OrganizationFreeMonths.AsNoTracking()
                .Where(m => m.Key == MonthKey(p, month.AddMonths(-1))).Select(m => (Guid?)m.OrganizationId).FirstOrDefaultAsync(ct);
            var winner = candidates
                .Where(c => c.Score.Total > 0 && c.Entry.OrganizationId != lastWinner)
                .OrderByDescending(c => c.Score.PerMember).ThenByDescending(c => c.Score.Total).ThenBy(c => c.Entry.QualifiedUtc)
                .Select(c => (PromotionEntry?)c.Entry).FirstOrDefault();

            if (winner is not null)
                await FreeMonths.GiveAsync(db, winner.OrganizationId, FreeMonthReason.GroupOfTheMonth, p.GroupOfTheMonthMonths,
                    MonthKey(p, month), now, Guid.Empty, ct, promotionEntryId: winner.Id,
                    note: $"Group of the month for {month:MMMM yyyy}.");
            var tracked = await db.Promotions.FirstAsync(x => x.Id == p.Id, ct);
            tracked.GroupOfTheMonthDecidedThroughUtc = month;
            await db.SaveChangesAsync(ct);
            p.GroupOfTheMonthDecidedThroughUtc = month;

            if (winner is not null)
                await TellAsync(db, winner.OrganizationId, $"Your group is the founding group of the month for {month:MMMM yyyy}",
                    $"Your group had the best score per member of the founding groups in {month:MMMM yyyy}, so it gets "
                    + $"{Months(p.GroupOfTheMonthMonths)} free on its plan. {Link("/founding-groups")}", ct);
            month = monthEnd;
        }
    }

    public static string MonthKey(Promotion p, DateTime month) => $"month:{p.Id:N}:{month:yyyy-MM}";

    /// <summary>A group's twelve months are up: its score is frozen, and reaching the score earns its months.</summary>
    private async Task YearsEndAsync(BenDataContext db, Promotion p, DateTime now, CancellationToken ct)
    {
        var done = await db.PromotionEntries
            .Where(e => e.PromotionId == p.Id && e.State == PromotionEntryState.Qualified
                     && e.FreeYearEndsUtc <= now && e.FinalScorePerMember == null).ToListAsync(ct);
        foreach (var entry in done)
        {
            var score = await FoundingGroupsScore.ScoreAsync(db, p, entry.OrganizationId, entry.QualifiedUtc!.Value, entry.FreeYearEndsUtc!.Value, ct);
            entry.FinalScorePerMember = score.PerMember;
            entry.FinalScoreTotal = score.Total;
            entry.DateUpdated = now;
            var reached = score.PerMember >= p.ReachableScorePerMember
                && await FreeMonths.GiveAsync(db, entry.OrganizationId, FreeMonthReason.ReachedTheScore, p.ReachedScoreMonths,
                    $"reached:{entry.Id:N}", now, Guid.Empty, ct, promotionEntryId: entry.Id,
                    note: $"Reached {p.ReachableScorePerMember} points per member in its founding year.");
            await db.SaveChangesAsync(ct);
            await TellAsync(db, entry.OrganizationId, "Your founding group's year is complete",
                $"Your group finished its twelve months with {score.PerMember:0.##} points per member."
                + (reached ? $" That reaches the score of {p.ReachableScorePerMember}, so it gets {Months(p.ReachedScoreMonths)} free on its plan." : "")
                + " The top three are decided once every founding group has finished its year.", ct);
        }
    }

    /// <summary>Once claims are closed and every group has finished its year: the top three win a second year.</summary>
    private async Task DecideAsync(BenDataContext db, Promotion p, DateTime now, CancellationToken ct)
    {
        if (p.DecidedUtc is not null) return;
        var entries = await db.PromotionEntries.Where(e => e.PromotionId == p.Id).ToListAsync(ct);
        var qualified = entries.Where(e => e.State == PromotionEntryState.Qualified).ToList();
        var closed = now >= FoundingGroups.ClaimsCloseUtc(p) || qualified.Count >= p.Spots;
        if (!closed || qualified.Count == 0 || entries.Any(e => e.State == PromotionEntryState.Gathering)
            || qualified.Any(e => e.FinalScorePerMember is null)) return;

        var ranked = qualified
            .OrderByDescending(e => e.FinalScorePerMember).ThenByDescending(e => e.FinalScoreTotal).ThenBy(e => e.QualifiedUtc)
            .ToList();
        for (var i = 0; i < ranked.Count; i++)
        {
            ranked[i].FinalRank = i + 1;
            ranked[i].DateUpdated = now;
            if (i < p.Winners)
                await FreeMonths.GiveAsync(db, ranked[i].OrganizationId, FreeMonthReason.SecondYear, p.WinnerMonths,
                    $"second-year:{ranked[i].Id:N}", now, Guid.Empty, ct, promotionEntryId: ranked[i].Id,
                    note: $"Placed {Place(i + 1)} of the founding groups.");
        }
        var tracked = await db.Promotions.FirstAsync(x => x.Id == p.Id, ct);
        tracked.DecidedUtc = now;
        await db.SaveChangesAsync(ct);

        foreach (var e in ranked)
            await TellAsync(db, e.OrganizationId, "The founding groups have been decided",
                e.FinalRank <= p.Winners
                    ? $"Your group placed {Place(e.FinalRank!.Value)} of the founding groups, so it gets a second year free: "
                      + $"{Months(p.WinnerMonths)} on its plan, from now. Thank you for everything you've done on IsHaunted."
                    : $"Your group placed {Place(e.FinalRank!.Value)} of {ranked.Count} founding groups. Thank you for being one "
                      + "of the first groups on IsHaunted.", ct);
    }

    /// <summary>A month and a week before a founding group's free time ends: what happens then.</summary>
    private async Task YearEndNoticesAsync(BenDataContext db, DateTime now, CancellationToken ct)
    {
        var entries = await db.PromotionEntries.Where(e => e.State == PromotionEntryState.Qualified
            && (e.YearEndMonthNoticeSentUtc == null || e.YearEndWeekNoticeSentUtc == null)).ToListAsync(ct);
        foreach (var entry in entries)
        {
            var sub = await db.OrganizationSubscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.OrganizationId == entry.OrganizationId, ct);
            if (sub is not { CurrentPeriodEnd: { } ends } || sub.PriceAtPeriodStart != 0m || ends <= now) continue;

            var month = entry.YearEndMonthNoticeSentUtc is null && ends - now <= TimeSpan.FromDays(30);
            var week = entry.YearEndWeekNoticeSentUtc is null && ends - now <= TimeSpan.FromDays(7);
            if (!month && !week) continue;
            if (month) entry.YearEndMonthNoticeSentUtc = now;
            if (week) entry.YearEndWeekNoticeSentUtc = now;
            await db.SaveChangesAsync(ct);

            await TellAsync(db, entry.OrganizationId, $"Your group's free plan ends on {ends:MM/dd/yyyy}",
                $"Your group's free time on its plan ends on {ends:MM/dd/yyyy}. To keep the plan after that, choose it on your "
                + $"group's billing page; if it isn't paid, it keeps going for two weeks and then stops, and nothing you've "
                + $"made is lost. {Link($"/organizations/{entry.OrganizationId}/billing")}", ct);
        }
    }

    // ── letters ───────────────────────────────────────────────────────────────

    /// <summary>To the group's owners, administrators and billing contacts: on the site, and by email.</summary>
    private async Task TellAsync(BenDataContext db, Guid organizationId, string subject, string text, CancellationToken ct)
    {
        try
        {
            var leaders = await db.OrganizationUserMemberships.AsNoTracking()
                .Where(m => m.OrganizationId == organizationId && m.IsActive
                         && (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator))
                .Select(m => m.AppUserId).ToListAsync(ct);
            var to = leaders.Concat(await _messages.BillingRecipientsAsync(organizationId, ct)).Distinct().ToList();
            if (to.Count == 0) return;
            await _messages.SendAsync(subject, text, to, to[0], ct, alsoPost: MailKinds.FoundingGroups);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Founding groups: could not tell group {Organization}: {Subject}", organizationId, subject);
        }
    }

    private string Link(string path) => $"See it at {_site.AbsoluteUrl(path)}";

    private static string Counted(int n) => n == 1 ? "1 member counted" : $"{n} members counted";

    private static string Months(int n) => n == 1 ? "1 month" : $"{n} months";

    private static string Place(int n) => n switch { 1 => "first", 2 => "second", 3 => "third", _ => $"number {n}" };
}
