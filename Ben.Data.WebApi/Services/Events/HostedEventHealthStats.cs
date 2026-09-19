using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The numbers behind the SuperAdmin's Event health tab: whether hosted events are working, rather than how the business
/// is doing (Ben, 2026-09-14).
/// </summary>
/// <remarks>
/// <para><b>What it answers.</b> Are holds lapsing faster than anybody answers them? How long do parties wait? Is mail
/// going out? Are the event addresses throwing? Is somebody hitting the booking limits? Each panel is a question a
/// developer would otherwise answer by reading rows or the log.</para>
///
/// <para><b>Counts, never people</b>, as on the business tab. The error panels come from the server's log and are read
/// separately (<see cref="HostedEventErrorLog"/>), because that table is not the application's and is not there on
/// every database.</para>
/// </remarks>
public static class HostedEventHealthStats
{
    /// <summary>The limits that stand in front of booking and attendance.</summary>
    public static readonly string[] EventRateLimits =
        [RateLimiting.HostedBookingPolicy, RateLimiting.HostedEmailPickPolicy, RateLimiting.EventAttendancePolicy];

    public static async Task<AdminHostedEventHealth> ReadAsync(
        BenDataContext db, int days, DateTime now, HostedEventErrorLog.Result errors,
        DateTime jobsSinceUtc, IReadOnlyList<ScheduledJobRunRecord> jobs, CancellationToken ct)
    {
        var since = now.Date.AddDays(-(days - 1));
        var bookings = db.HostedEventBookings.AsNoTracking();

        var holdsLive = await bookings
            .Where(b => b.Status == HostedEventBookingStatus.Held && b.HoldExpiresUtc > now)
            .Select(b => b.HoldExpiresUtc!.Value)
            .ToListAsync(ct);

        var waiting = await bookings
            .Where(b => (b.Status == HostedEventBookingStatus.Requested || b.Status == HostedEventBookingStatus.Held)
                     && HostedEventStates.TakingBookings.Contains(b.HostedEvent.LifecycleState))
            .Select(b => b.DateCreated)
            .ToListAsync(ct);

        var made = await bookings.Where(b => b.DateCreated >= since)
            .GroupBy(b => b.DateCreated.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);

        var answers = await bookings
            .Where(b => b.DecidedUtc >= since
                     && (b.Status == HostedEventBookingStatus.Confirmed || b.Status == HostedEventBookingStatus.TurnedDown
                      || b.Status == HostedEventBookingStatus.Cancelled))
            .Select(b => new { b.DateCreated, Decided = b.DecidedUtc!.Value })
            .ToListAsync(ct);

        var lapsed = await bookings
            .Where(b => b.Status == HostedEventBookingStatus.Expired && b.HoldExpiresUtc >= since)
            .GroupBy(b => b.HoldExpiresUtc!.Value.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);

        var outbox = db.OutboxEmails.AsNoTracking();
        var lettersWaiting = await outbox.CountAsync(m => m.AcceptedBySmtpUtc == null && m.FailedUtc == null, ct);
        var queued = await outbox.Where(m => m.CreatedUtc >= since)
            .GroupBy(m => m.CreatedUtc.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var sent = await outbox.Where(m => m.AcceptedBySmtpUtc >= since)
            .GroupBy(m => m.AcceptedBySmtpUtc!.Value.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var failed = await outbox.Where(m => m.FailedUtc >= since)
            .GroupBy(m => m.FailedUtc!.Value.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);

        var refusals = await db.RateLimitRefusals.AsNoTracking()
            .Where(r => EventRateLimits.Contains(r.PolicyName))
            .OrderByDescending(r => r.Refusals)
            .Select(r => new { r.PolicyName, r.Refusals, r.DateLastSeen })
            .ToListAsync(ct);

        return new AdminHostedEventHealth(
            HoldsLiveNow: holdsLive.Count,
            HoldsLapsingNextDay: holdsLive.Count(expires => expires <= now.AddDays(1)),
            WaitingNow: waiting.Count,
            OldestWaitHours: waiting.Count == 0 ? null : Math.Round((now - waiting.Min()).TotalHours, 1),
            LettersWaitingNow: lettersWaiting,
            LettersFailedInPeriod: failed.Sum(x => x.Count),
            BookingsMadePerDay: AdminStatsController.FillDays(made.Select(x => (x.Day, x.Count)), since, days),
            AnsweredPerDay: AdminStatsController.FillDays(
                answers.GroupBy(a => a.Decided.Date).Select(g => (g.Key, g.Count())), since, days),
            HoldsLapsedPerDay: AdminStatsController.FillDays(lapsed.Select(x => (x.Day, x.Count)), since, days),
            TimeToAnswer: AnswerBuckets(answers.Select(a => a.Decided - a.DateCreated)),
            LettersQueuedPerDay: AdminStatsController.FillDays(queued.Select(x => (x.Day, x.Count)), since, days),
            LettersSentPerDay: AdminStatsController.FillDays(sent.Select(x => (x.Day, x.Count)), since, days),
            LettersFailedPerDay: AdminStatsController.FillDays(failed.Select(x => (x.Day, x.Count)), since, days),
            ErrorsPerDay: errors.PerDay,
            ErrorsByAddress: errors.ByAddress,
            ErrorsUnavailable: errors.Unavailable,
            RateLimitRefusals: [.. refusals.Select(r => new StatSlice(
                $"{r.PolicyName} · last {r.DateLastSeen:MM/dd/yyyy}", (int)Math.Min(r.Refusals, int.MaxValue)))],
            JobsSinceUtc: jobsSinceUtc,
            Jobs: jobs);
    }

    /// <summary>How long parties waited for an answer, in the buckets a person would describe it by.</summary>
    /// <remarks>Every bucket is returned, empty or not, so the chart keeps its shape from one week to the next.</remarks>
    public static IReadOnlyList<StatSlice> AnswerBuckets(IEnumerable<TimeSpan> waits)
    {
        (string Label, TimeSpan UpTo)[] buckets =
        [
            ("Within an hour", TimeSpan.FromHours(1)),
            ("1 to 6 hours", TimeSpan.FromHours(6)),
            ("6 to 24 hours", TimeSpan.FromDays(1)),
            ("1 to 3 days", TimeSpan.FromDays(3)),
            ("Over 3 days", TimeSpan.MaxValue),
        ];
        var counts = new int[buckets.Length];
        foreach (var wait in waits)
            counts[Array.FindIndex(buckets, b => wait <= b.UpTo)]++;
        return [.. buckets.Select((b, i) => new StatSlice(b.Label, counts[i]))];
    }
}
