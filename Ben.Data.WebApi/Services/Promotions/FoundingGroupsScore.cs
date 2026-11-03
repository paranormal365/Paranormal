using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Promotions;

/// <summary>
/// How active a founding group is (Ben, 11/02/2026): field sessions, investigations, events and evidence, scored
/// per member so a group of three can beat a group of thirty. Each paid referral adds to the group's points.
/// </summary>
/// <remarks>
/// <para><b>What counts.</b> A field session with readings (an empty one earns nothing); an investigation of the
/// group's that happened, for each member who was there; evidence a member offered that was accepted, at an event
/// or on one of the group's cases; and, for the group as a whole, its hosted events that happened and each group
/// it referred that paid.</para>
///
/// <para><b>Per member</b> is the group's points divided by its members with confirmed email addresses, so
/// growing doesn't buy a score and a small group isn't outnumbered.</para>
/// </remarks>
public static class FoundingGroupsScore
{
    public sealed record MemberPoints(
        Guid AppUserId, string Name, int Sessions, int Investigations, int Evidence, int Points);

    public sealed record GroupScore(
        int Members, int Events, int Referrals, int GroupPoints, int Total, decimal PerMember,
        IReadOnlyList<MemberPoints> People);

    /// <summary>
    /// The group's score for activity from <paramref name="fromUtc"/> up to <paramref name="toUtc"/>. Callers end the
    /// window at now at the latest, which is what keeps a planned investigation from counting before it happens.
    /// </summary>
    public static async Task<GroupScore> ScoreAsync(
        BenDataContext db, Promotion p, Guid organizationId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var people = await (
            from m in db.OrganizationUserMemberships.AsNoTracking()
            join u in db.Users.AsNoTracking() on m.AppUserId equals u.Id
            where m.OrganizationId == organizationId && m.IsActive && u.EmailConfirmed
            select new { u.Id, Name = u.DisplayName ?? u.UserName ?? "A member" }).ToListAsync(ct);
        var ids = people.Select(x => x.Id).ToList();

        var sessions = await db.FieldSessionUploads.AsNoTracking()
            .Where(s => ids.Contains(s.SubmittedByAppUserId) && s.StartedAt >= fromUtc && s.StartedAt < toUtc
                     && s.ReadingCount >= p.MinimumReadingsPerSession)
            .GroupBy(s => s.SubmittedByAppUserId).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);

        var investigations = await (
            from a in db.InvestigationAttendees.AsNoTracking()
            join i in db.Investigations.AsNoTracking() on a.InvestigationId equals i.Id
            where i.OrganizationId == organizationId && ids.Contains(a.AppUserId)
               && i.ScheduledDateTime >= fromUtc && i.ScheduledDateTime < toUtc
               && i.Status != InvestigationStatus.Cancelled
               && (a.DidAttend == true || (a.DidAttend == null && a.Rsvp == RsvpStatus.Accepted))
            group a by a.AppUserId into g
            select new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

        var eventEvidence = await db.EventEvidenceSubmissions.AsNoTracking()
            .Where(e => ids.Contains(e.SubmittedByAppUserId) && e.Status == EvidenceSubmissionStatus.Accepted
                     && e.DateCreated >= fromUtc && e.DateCreated < toUtc)
            .GroupBy(e => e.SubmittedByAppUserId).Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N, ct);

        var caseEvidence = await (
            from t in db.CaseTimelineEntries.AsNoTracking()
            join c in db.Cases.AsNoTracking() on t.CaseId equals c.Id
            where c.OrganizationId == organizationId && t.EntryType == CaseTimelineEntryType.Evidence
               && ids.Contains(t.CreatedByAppUserId) && t.DateCreated >= fromUtc && t.DateCreated < toUtc
            group t by t.CreatedByAppUserId into g
            select new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

        var events = await db.HostedEvents.AsNoTracking()
            .CountAsync(e => e.OrganizationId == organizationId
                          && (e.LifecycleState == HostedEventLifecycleState.Ended || e.LifecycleState == HostedEventLifecycleState.Archived)
                          && e.EndsOn >= fromUtc && e.EndsOn < toUtc, ct);

        var referrals = await db.OrganizationFreeMonths.AsNoTracking()
            .CountAsync(m => m.OrganizationId == organizationId && m.Reason == FreeMonthReason.Referral && m.RevokedUtc == null
                          && m.EarnedUtc >= fromUtc && m.EarnedUtc < toUtc, ct);

        var rows = people.Select(x =>
        {
            var s = sessions.GetValueOrDefault(x.Id);
            var i = investigations.GetValueOrDefault(x.Id);
            var ev = eventEvidence.GetValueOrDefault(x.Id) + caseEvidence.GetValueOrDefault(x.Id);
            return new MemberPoints(x.Id, x.Name, s, i, ev,
                s * p.PointsPerSession + i * p.PointsPerInvestigation + ev * p.PointsPerEvidence);
        }).OrderByDescending(x => x.Points).ThenBy(x => x.Name).ToList();

        var groupPoints = events * p.PointsPerEvent + referrals * p.PointsPerReferral;
        var total = rows.Sum(x => x.Points) + groupPoints;
        var perMember = Math.Round((decimal)total / Math.Max(1, rows.Count), 2);
        return new GroupScore(rows.Count, events, referrals, groupPoints, total, perMember, rows);
    }

    /// <summary>
    /// The entry's own contest so far: from qualifying to its twelve months' end, or now if sooner.
    /// </summary>
    public static (DateTime From, DateTime To)? Window(PromotionEntry e, DateTime now)
        => e is { State: PromotionEntryState.Qualified, QualifiedUtc: { } from, FreeYearEndsUtc: { } ends }
            ? (from, now < ends ? now : ends)
            : null;

    /// <summary>
    /// Points per member per month so far, so groups at different stages rank fairly on the leaderboard.
    /// </summary>
    public static decimal PerMemberPerMonth(GroupScore score, DateTime from, DateTime to)
    {
        var months = Math.Max(1m, (decimal)(to - from).TotalDays / 30.4375m);
        return Math.Round(score.PerMember / months, 2);
    }
}
