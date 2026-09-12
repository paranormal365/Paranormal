using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Gives back the places nobody answered for in time (item 235 phase 4).
/// </summary>
/// <remarks>
/// <para><b>This is what makes a hold a hold.</b> Without it a guest who picked three seats and
/// then forgot about them would keep those seats out of everybody else's reach for ever, and the
/// venue would watch a sold-out house sit half empty. The deadline is the event's own
/// <c>HoldMinutes</c>, set by the venue.</para>
///
/// <para><b>One booking, one save, inside a try.</b> A party whose release fails must not stop the
/// next one — the whole point of a batch here is that a stuck row costs one seat rather than every
/// seat on the site. Nothing is marked, so a failed booking is simply tried again next pass.</para>
///
/// <para><b>The guest is told, and stays on the list.</b> Their booking becomes Expired rather than
/// vanishing: they chose seats, nobody answered, and the seats are gone — which is a different and
/// truer thing than "you never asked". What they may do about it is pick again.</para>
///
/// <para>The organizer is deliberately NOT emailed per lapse. A busy weekend would send them a
/// letter an hour; what they get is the count in their digest (phase 8), and the board shows the
/// holds that are about to lapse while there is still time to answer.</para>
/// </remarks>
public sealed class HoldExpiryJob : IScheduledJob
{
    public string Name => "hosted-event-hold-expiry";

    /// <summary>How many lapsed holds one pass will deal with.</summary>
    /// <remarks>
    /// A bound rather than "all of them", so a backlog after an outage is worked through over
    /// several passes instead of one transaction holding every affected row at once.
    /// </remarks>
    internal const int BatchSize = 100;

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly HostedEventCalendarSync _sync;
    private readonly EventGuestMailer _mailer;
    private readonly ILogger<HoldExpiryJob> _logger;

    public HoldExpiryJob(
        IDbContextFactory<BenDataContext> dbFactory,
        HostedEventCalendarSync sync,
        EventGuestMailer mailer,
        ILogger<HoldExpiryJob> logger)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _mailer = mailer;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var lapsed = await db.HostedEventBookings
            .Include(b => b.Nights)
            .Include(b => b.HostedEvent).ThenInclude(e => e.Nights)
            .Where(b => b.Status == HostedEventBookingStatus.Held
                     && b.HoldExpiresUtc != null
                     && b.HoldExpiresUtc <= now)
            .OrderBy(b => b.HoldExpiresUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (lapsed.Count == 0) return;

        var freed = 0;

        foreach (var booking in lapsed)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                BookingTransitions.Expire(booking, now);

                // The umbrella row goes with it: somebody whose hold lapsed is not an attendee, and
                // leaving the row would have them reminded about an evening they have no place at.
                await BookingTransitions.ApplyUmbrellaAsync(
                    db, _sync, booking.HostedEvent, booking, booking.LeadAppUserId, now, ct);

                await db.SaveChangesAsync(ct);
                freed++;

                await TellTheGuestAsync(db, booking, ct);
            }
            catch (Exception ex)
            {
                // Nothing is marked, so this booking is tried again on the next pass. One stuck row
                // costs one seat rather than every seat on the site.
                _logger.LogWarning(ex,
                    "Could not give back the places held by booking {BookingId}.", booking.Id);
            }
        }

        if (freed > 0)
            _logger.LogInformation("Gave back the places held by {Count} lapsed booking(s).", freed);
    }

    /// <summary>
    /// Tells the guest their hold lapsed, after it has actually lapsed.
    /// </summary>
    /// <remarks>
    /// After the save, deliberately, and outside the same try: a letter that cannot be sent must
    /// not roll back the release, or the seats stay stuck behind a mail server. Since item 239 the
    /// letter is written down and posted later anyway, so this rarely fails at all.
    /// </remarks>
    private async Task TellTheGuestAsync(
        BenDataContext db, HostedEventBooking booking, CancellationToken ct)
    {
        try
        {
            await _mailer.SendHoldLapsedAsync(db, booking, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "The places held by booking {BookingId} went back, but the guest could not be told.",
                booking.Id);
        }
    }
}
