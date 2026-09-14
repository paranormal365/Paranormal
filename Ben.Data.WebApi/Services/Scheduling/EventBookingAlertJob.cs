using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Tells the people who decide bookings that some have arrived (item 235 phase 8).
/// </summary>
/// <remarks>
/// <para><b>A request is answered because somebody was told.</b> When this is the first booking in a
/// while, it is written about on the next pass; when it is one of a rush, it waits for one summary.
/// <see cref="EventBookingAlerts"/> decides which, and is where the timing is tested.</para>
///
/// <para><b>Nothing is written when a booking arrives.</b> This finds new arrivals by when they were
/// made and remembers, per person per event, how far it has told them. So there is no hook in the
/// request door or the hold door to forget, and an outage costs a late letter rather than a lost
/// one.</para>
///
/// <para><b>One person, one save, inside a try</b>, like every other job here: a bad address must
/// not stop the next venue manager hearing about their weekend.</para>
/// </remarks>
public sealed class EventBookingAlertJob : IScheduledJob
{
    public string Name => "event-booking-alerts";

    /// <summary>
    /// How far back the job looks for events with something to say.
    /// </summary>
    /// <remarks>
    /// A week, which is far longer than any cursor should fall behind — so an outage over a weekend
    /// still ends in a letter — and short enough that the question stays cheap on a site with years
    /// of bookings behind it.
    /// </remarks>
    internal static readonly TimeSpan LooksBack = TimeSpan.FromDays(7);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly Services.Access.HostedEventAccess _access;
    private readonly EventOrganizerMailer _mailer;
    private readonly ILogger<EventBookingAlertJob> _logger;

    public EventBookingAlertJob(
        IDbContextFactory<BenDataContext> dbFactory,
        Services.Access.HostedEventAccess access,
        EventOrganizerMailer mailer,
        ILogger<EventBookingAlertJob> logger)
    {
        _dbFactory = dbFactory;
        _access = access;
        _mailer = mailer;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // Nothing to send with, so nothing to move: the cursors stay where they are and the letters
        // go the day mail is switched on, rather than being marked as told when nobody was.
        if (!_mailer.IsConfigured) return;

        await RunAtAsync(DateTime.UtcNow, ct);
    }

    /// <summary>One pass, at a given moment — separate so a test can move the clock.</summary>
    internal async Task RunAtAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var busy = await db.HostedEventBookings.AsNoTracking()
            .Where(b => (b.Status == HostedEventBookingStatus.Requested
                      || b.Status == HostedEventBookingStatus.Held)
                     && b.DateCreated >= now - LooksBack
                     && HostedEventStates.TakingBookings.Contains(b.HostedEvent.LifecycleState))
            .Select(b => b.HostedEventId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var eventId in busy)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await TellAboutAsync(db, eventId, now, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError(e, "Could not tell anybody about new bookings at {EventId}.", eventId);
            }
        }
    }

    private async Task TellAboutAsync(BenDataContext db, Guid eventId, DateTime now, CancellationToken ct)
    {
        var ev = await db.HostedEvents.AsNoTracking()
            .Include(e => e.Organization)
            .Include(e => e.Nights)
            .FirstAsync(e => e.Id == eventId, ct);

        var bookings = await db.HostedEventBookings.AsNoTracking()
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventNight)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
            .Where(b => b.HostedEventId == eventId && b.DateCreated >= now - LooksBack)
            .ToListAsync(ct);

        var recipients = await EventBookingRecipients.ForAsync(db, _access, ev, ct);

        foreach (var to in recipients.Where(r => r.Mode == EventBookingAlertMode.AsItHappens))
        {
            var state = await db.EventBookingAlertStates
                .FirstOrDefaultAsync(s => s.AppUserId == to.AppUserId && s.HostedEventId == eventId, ct);

            var decision = EventBookingAlerts.Decide(state, bookings, to.AppUserId, now);
            if (decision.Send == EventBookingAlerts.Send.Nothing) continue;

            try
            {
                var sent = await _mailer.SendArrivalsAsync(
                    to, ev, decision.Covers, summary: decision.Send == EventBookingAlerts.Send.Summary, ct);
                if (!sent) continue;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The cursor does NOT move: this person has not been told, and the next pass tries
                // again. A letter marked as sent that never went is the silence this job exists for.
                _logger.LogWarning(e, "Could not write to {User} about bookings at {EventId}.",
                    to.AppUserId, eventId);
                continue;
            }

            if (state is null)
            {
                state = new EventBookingAlertState
                {
                    Id = Guid.NewGuid(),
                    AppUserId = to.AppUserId,
                    HostedEventId = eventId,
                    DateCreated = now,
                    CreatedByAppUserId = to.AppUserId,
                };
                db.EventBookingAlertStates.Add(state);
            }

            state.LastAlertUtc = now;
            state.AlertsCoverUpToUtc = decision.CoversUpToUtc;
            state.DateUpdated = now;

            await db.SaveChangesAsync(ct);
        }
    }
}
