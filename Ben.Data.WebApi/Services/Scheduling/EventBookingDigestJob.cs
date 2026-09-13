using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Where each event stands, once a day while it is selling and once a week otherwise
/// (item 235 phase 8).
/// </summary>
/// <remarks>
/// <para><b>The letter for the person who does not want every request as it comes</b>, and the
/// safety net under the one who does: a request that arrived while their alerts were switched off,
/// or a hold about to lapse that nobody has looked at, is in here whatever else happened.</para>
///
/// <para><b>Never when empty.</b> <see cref="EventBookingDigest"/> decides what is worth saying; a
/// letter that says "nothing new" every morning trains somebody to stop opening them.</para>
///
/// <para><b>The marker moves only when a letter went</b> — or when there was nothing to say, so a
/// quiet event is not re-examined on every five-minute pass for the rest of the day.</para>
/// </remarks>
public sealed class EventBookingDigestJob : IScheduledJob
{
    public string Name => "event-booking-digest";

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly Services.Access.HostedEventAccess _access;
    private readonly EventOrganizerMailer _mailer;
    private readonly ILogger<EventBookingDigestJob> _logger;

    public EventBookingDigestJob(
        IDbContextFactory<BenDataContext> dbFactory,
        Services.Access.HostedEventAccess access,
        EventOrganizerMailer mailer,
        ILogger<EventBookingDigestJob> logger)
    {
        _dbFactory = dbFactory;
        _access = access;
        _mailer = mailer;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        if (!_mailer.IsConfigured) return;
        await RunAtAsync(DateTime.UtcNow, ct);
    }

    internal async Task RunAtAsync(DateTime now, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var events = await db.HostedEvents.AsNoTracking()
            .Where(e => HostedEventStates.TakingBookings.Contains(e.LifecycleState))
            .Select(e => e.Id)
            .ToListAsync(ct);

        foreach (var eventId in events)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await DigestAsync(db, eventId, now, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError(e, "Could not write the digest for {EventId}.", eventId);
            }
        }
    }

    private async Task DigestAsync(BenDataContext db, Guid eventId, DateTime now, CancellationToken ct)
    {
        var ev = await db.HostedEvents.AsNoTracking()
            .Include(e => e.Organization)
            .Include(e => e.Nights)
            .FirstAsync(e => e.Id == eventId, ct);

        var recipients = (await EventBookingRecipients.ForAsync(db, _access, ev, ct))
            .Where(r => r.Mode != EventBookingAlertMode.Off)
            .ToList();
        if (recipients.Count == 0) return;

        var states = await db.EventBookingAlertStates
            .Where(s => s.HostedEventId == eventId)
            .ToListAsync(ct);

        var due = recipients
            .Where(r => EventBookingDigest.IsDue(
                ev, states.FirstOrDefault(s => s.AppUserId == r.AppUserId)?.LastDigestUtc, now))
            .ToList();
        if (due.Count == 0) return;

        // The contents are the same for everybody at this event, so they are built once. Only
        // whether each person is due differs.
        var bookings = await db.HostedEventBookings.AsNoTracking()
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventNight)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
            .Where(b => b.HostedEventId == eventId)
            .ToListAsync(ct);

        var units = await db.HostedEventLayoutUnits.AsNoTracking()
            .Where(u => u.HostedEventId == eventId)
            .ToListAsync(ct);

        var blocks = await PlanOccupancy.BlocksAsync(db, eventId, ct);

        var arrivals = await db.HostedEventCheckIns.AsNoTracking()
            .Where(c => c.HostedEventBooking.HostedEventId == eventId)
            .ToListAsync(ct);

        var contents = EventBookingDigest.Build(ev, bookings, units, blocks, arrivals, now);

        foreach (var to in due)
        {
            var state = states.FirstOrDefault(s => s.AppUserId == to.AppUserId);

            if (!contents.IsEmpty)
            {
                // Their own booking is not something to decide.
                var theirs = contents with
                {
                    Waiting = [.. contents.Waiting.Where(b => b.LeadAppUserId != to.AppUserId)],
                    Lapsing = [.. contents.Lapsing.Where(b => b.LeadAppUserId != to.AppUserId)],
                };

                try
                {
                    if (!theirs.IsEmpty)
                        await _mailer.SendDigestAsync(to, ev, theirs, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    _logger.LogWarning(e, "Could not send {User} the digest for {EventId}.",
                        to.AppUserId, eventId);
                    continue;
                }
            }

            // Moved whether or not there was anything to say: a quiet event is looked at again
            // tomorrow, not on every pass for the rest of the day.
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
                states.Add(state);
            }

            state.LastDigestUtc = now;
            state.DateUpdated = now;

            await db.SaveChangesAsync(ct);
        }
    }
}
