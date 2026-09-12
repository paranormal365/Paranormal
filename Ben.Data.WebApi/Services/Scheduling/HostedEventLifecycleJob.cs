using Ben.Data.Common;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Moves a hosted event through its own life, so nobody has to remember to (item 235 phase 3).
/// </summary>
/// <remarks>
/// <para><b>Why a job and not a date comparison.</b> "Is it on tonight" was being worked out by
/// every screen that wanted to know, from two dates and a time zone, and the answers drifted. One
/// job writes the state and everything else reads it. It also means the state can be indexed and
/// queried, which a comparison never can.</para>
///
/// <para><b>The venue's clock decides, never the server's.</b> An event at the Thomas House starts
/// at seven Central whatever the rack in the data centre thinks the time is, so every boundary here
/// is computed through <see cref="HostedEventCalendarSync.Window"/> — the same arithmetic the
/// calendar row uses, so the two can never disagree about when tonight began.</para>
///
/// <para><b>Nothing here is undone by running twice.</b> Each move is guarded by the state it
/// moves out of, and each letter by a marker of its own. The scheduler makes no promise about how
/// often it calls, and a job that reminded somebody every five minutes for a week would be the
/// same as not reminding them at all.</para>
///
/// <para><b>Archiving happens a pass AFTER the requests are expired</b>, deliberately. An event
/// that ends with people still waiting on an answer has to tell them, and folding "tell them" and
/// "file it away" into one pass means a failure in the first silently loses the second. Decision 11
/// (Ben, 2026-09-12): fourteen days, the note reads "the event has passed", and the organizer is
/// told what was closed on their behalf.</para>
/// </remarks>
public sealed class HostedEventLifecycleJob : IScheduledJob
{
    public string Name => "hosted-event-lifecycle";

    /// <summary>How long an ended event stays on the lists before it is filed away.</summary>
    /// <remarks>
    /// A fortnight, which is long enough for the organizer to work through what happened and short
    /// enough that last month's weekend is not still advertising itself.
    /// </remarks>
    internal static readonly TimeSpan ArchiveAfter = TimeSpan.FromDays(14);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IEmailService _email;
    private readonly PlatformMessageService _messages;
    private readonly SiteIdentity _site;
    private readonly ILogger<HostedEventLifecycleJob> _logger;

    public HostedEventLifecycleJob(
        IDbContextFactory<BenDataContext> dbFactory,
        IEmailService email,
        PlatformMessageService messages,
        IOptions<SiteIdentity> site,
        ILogger<HostedEventLifecycleJob> logger)
    {
        _dbFactory = dbFactory;
        _email = email;
        _messages = messages;
        _site = site.Value;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        // Only the states that can still move. A draft never moves on its own, and a called-off
        // event is where it is until somebody says otherwise.
        var moving = await db.HostedEvents
            .Include(e => e.Nights.OrderBy(n => n.Date))
            .Where(e => e.LifecycleState == HostedEventLifecycleState.Published
                     || e.LifecycleState == HostedEventLifecycleState.Live
                     || e.LifecycleState == HostedEventLifecycleState.Ended)
            .ToListAsync(ct);

        var moved = 0;

        foreach (var hosted in moving)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                if (await AdvanceAsync(db, hosted, now, ct)) moved++;
            }
            catch (Exception ex)
            {
                // One event that cannot be moved must not strand the rest. No state is written, so
                // the next pass tries it again.
                _logger.LogWarning(ex,
                    "Could not advance hosted event {EventId} from {State}.",
                    hosted.Id, hosted.LifecycleState);
            }
        }

        await RemindAboutDecisionsAsync(db, now, ct);

        if (moved > 0) _logger.LogInformation("Moved {Count} hosted event(s) along.", moved);
    }

    // ── one event, one step ──────────────────────────────────────────────────

    /// <summary>Takes an event at most one step, and saves only if it took one.</summary>
    /// <remarks>
    /// One step per pass on purpose. An event that ended a month ago should not be published, made
    /// live, ended and archived inside a single loop iteration: each step has a letter or a
    /// consequence attached, and doing four at once means three of them happen with no chance for
    /// the pass to fail safely in between.
    /// </remarks>
    private async Task<bool> AdvanceAsync(
        BenDataContext db, HostedEvent hosted, DateTime now, CancellationToken ct)
    {
        var (startUtc, endUtc) = HostedEventCalendarSync.Window(hosted, [.. hosted.Nights]);

        switch (hosted.LifecycleState)
        {
            // OVER IS CHECKED BEFORE STARTED, and the order is load-bearing: switch arms are tried
            // in sequence, so with "started" first a published event whose whole run finished last
            // week went Live for a pass before ending on the next one. That is a real evening
            // during which the site says an event that is over is happening now.
            case HostedEventLifecycleState.Published when now >= endUtc:
            case HostedEventLifecycleState.Live when now >= endUtc:
                hosted.LifecycleState = HostedEventLifecycleState.Ended;
                hosted.EndedAtUtc = now;
                break;

            case HostedEventLifecycleState.Published when now >= startUtc:
                hosted.LifecycleState = HostedEventLifecycleState.Live;
                hosted.LiveAtUtc = now;
                break;

            // A fortnight after the event ENDED, not a fortnight after the job noticed it had. The
            // stamp records when the state was written and can be days late for an event nobody
            // published until afterwards; the last night is the fact somebody would name.
            case HostedEventLifecycleState.Ended when now >= endUtc + ArchiveAfter:
                // Anybody still waiting is answered FIRST, and the filing happens on the next pass.
                // Folding the two together means a failure to tell them silently files the event
                // anyway, and they never hear.
                if (await CloseWhatIsStillWaitingAsync(db, hosted, now, ct)) return true;

                hosted.LifecycleState = HostedEventLifecycleState.Archived;
                hosted.ArchivedAtUtc = now;
                break;

            default:
                return false;
        }

        hosted.DateUpdated = now;
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Turns down anything still undecided on an event that has been over a fortnight.
    /// </summary>
    /// <returns>True when it closed something, which holds the archiving back one pass.</returns>
    /// <remarks>
    /// <para>Decision 11. A request nobody ever answered is worse than one turned down: the person
    /// is still, as far as they know, waiting to hear. So the site answers on the venue's behalf in
    /// the venue's absence, says exactly that, and tells the organizer what it did.</para>
    ///
    /// <para>Written through the booking's own status rather than deleted, because somebody looking
    /// at their own bookings should see what became of this one.</para>
    /// </remarks>
    private async Task<bool> CloseWhatIsStillWaitingAsync(
        BenDataContext db, HostedEvent hosted, DateTime now, CancellationToken ct)
    {
        var waiting = await db.HostedEventBookings
            .Where(b => b.HostedEventId == hosted.Id
                     && b.Status == HostedEventBookingStatus.Requested)
            .ToListAsync(ct);

        if (waiting.Count == 0) return false;

        foreach (var booking in waiting)
        {
            booking.Status = HostedEventBookingStatus.TurnedDown;
            booking.DecisionNote = "The event has passed.";
            booking.DecidedUtc = now;
            booking.DateUpdated = now;
        }

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Closed {Count} unanswered request(s) on hosted event {EventId}, which is now over.",
            waiting.Count, hosted.Id);

        await TellTheOrganizersAsync(
            db, hosted,
            $"{waiting.Count} request{(waiting.Count == 1 ? " was" : "s were")} never answered",
            $"{hosted.Name} finished a fortnight ago and {waiting.Count} "
            + $"{(waiting.Count == 1 ? "party was" : "parties were")} still waiting to hear from "
            + "you. We have told them the event has passed, so nobody is left expecting an answer.",
            ct);

        return true;
    }

    // ── the decision an event with a minimum has to make ─────────────────────

    /// <summary>
    /// Reminds an organizer a week and a day before their go-or-no-go date.
    /// </summary>
    /// <remarks>
    /// Two markers rather than a count, because they are two different letters and this runs every
    /// five minutes. Without them a host would be reminded two hundred and eighty-eight times a
    /// day, which is the same as not being reminded.
    /// </remarks>
    private async Task RemindAboutDecisionsAsync(BenDataContext db, DateTime now, CancellationToken ct)
    {
        var undecided = await db.HostedEvents
            .Where(e => e.GoNoGoDeadlineUtc != null
                     && e.GoNoGoDecision == HostedEventGoNoGo.Undecided
                     && (e.LifecycleState == HostedEventLifecycleState.Published
                      || e.LifecycleState == HostedEventLifecycleState.Draft)
                     && (!e.GoNoGoWeekReminderSent || !e.GoNoGoDayReminderSent))
            .ToListAsync(ct);

        foreach (var hosted in undecided)
        {
            if (ct.IsCancellationRequested) break;

            var deadline = hosted.GoNoGoDeadlineUtc!.Value;
            var week = deadline.AddDays(-7);
            var day = deadline.AddDays(-1);

            try
            {
                // The day's reminder wins when both are due, which happens for a deadline set
                // fewer than seven days out. Sending both at once would be two letters saying the
                // same thing a second apart.
                if (!hosted.GoNoGoDayReminderSent && now >= day)
                {
                    await RemindAsync(db, hosted, "tomorrow", deadline, ct);
                    hosted.GoNoGoWeekReminderSent = true;
                    hosted.GoNoGoDayReminderSent = true;
                }
                else if (!hosted.GoNoGoWeekReminderSent && now >= week)
                {
                    await RemindAsync(db, hosted, "in a week", deadline, ct);
                    hosted.GoNoGoWeekReminderSent = true;
                }
                else
                {
                    continue;
                }

                hosted.DateUpdated = now;
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not remind anybody about the decision on hosted event {EventId}.",
                    hosted.Id);
            }
        }
    }

    private Task RemindAsync(
        BenDataContext db, HostedEvent hosted, string when, DateTime deadline, CancellationToken ct)
        => TellTheOrganizersAsync(
            db, hosted,
            $"{hosted.Name}: decide {when} whether it is going ahead",
            $"You said {hosted.Name} needs at least {hosted.MinimumGuests} "
            + $"{(hosted.MinimumGuests == 1 ? "person" : "people")} to be worth running, and that "
            + $"you would decide by {deadline:MM/dd/yyyy}. That is {when}. Open the event to say "
            + "yes or to call it off — calling it off tells everybody who has a place, and your "
            + "event credit comes back if there are more than 48 hours to go.",
            ct);

    // ── telling somebody ─────────────────────────────────────────────────────

    /// <summary>
    /// Says something to the people who can act on it: by mail where there is an address, and on
    /// the platform always.
    /// </summary>
    /// <remarks>
    /// The platform message is not a nicety, it is the path that always exists — a deployment with
    /// no mail server must still be able to tell an organizer that their event closed itself.
    /// </remarks>
    private async Task TellTheOrganizersAsync(
        BenDataContext db, HostedEvent hosted, string subject, string body, CancellationToken ct)
    {
        var ids = await _messages.BillingRecipientsAsync(hosted.OrganizationId, ct);
        if (ids.Count == 0)
        {
            _logger.LogWarning(
                "Hosted event {EventId} has nobody to tell: {Subject}", hosted.Id, subject);
            return;
        }

        // Sent as the person who set the event up: a platform message needs a sender, and the
        // organizer is the truthful one — this is their event telling their colleagues.
        await _messages.SendAsync(subject, body, ids, hosted.CreatedByAppUserId, ct);

        if (!_email.IsConfigured) return;

        var addresses = await db.AppUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Email != null)
            .Select(u => u.Email!)
            .ToListAsync(ct);

        foreach (var address in addresses)
        {
            try
            {
                await _email.SendAsync(address, $"{_site.Name}: {subject}", $"<p>{body}</p>", ct);
            }
            catch (Exception ex)
            {
                // The platform message has already gone, so the news is not lost. Since item 239
                // this catch is nearly unreachable: the outbox writes the letter down and posts it
                // later rather than throwing here.
                _logger.LogWarning(ex, "Could not email {Address} about hosted event {EventId}.",
                    address, hosted.Id);
            }
        }
    }
}
