using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Sends the thank-you the morning after an event's last night (item 235 phase 12).
/// </summary>
/// <remarks>
/// <para><b>Once per party, however often it runs.</b> Each booking is stamped as it is thanked, so a
/// pass that fails half way finishes the rest next time without writing to anybody twice; the event is
/// stamped when nobody is left.</para>
///
/// <para><b>Only recent events.</b> An event that ended more than a week before this job first saw it —
/// every past event on the day it ships — is left alone. A thank-you for last spring is not a thank-you.</para>
///
/// <para>Nothing is stamped while mail is switched off, so the letters go the day it is switched on,
/// if that is still within the week.</para>
/// </remarks>
public sealed class HostedEventThankYouJob : IScheduledJob
{
    public string Name => "hosted-event-thank-you";

    /// <summary>After the last night ends: the next morning, not the small hours.</summary>
    internal static readonly TimeSpan After = TimeSpan.FromHours(12);

    /// <summary>How late a thank-you may still go.</summary>
    internal static readonly TimeSpan NoLaterThan = TimeSpan.FromDays(7);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly EventGuestMailer _mailer;
    private readonly ILogger<HostedEventThankYouJob> _logger;

    public HostedEventThankYouJob(
        IDbContextFactory<BenDataContext> dbFactory, EventGuestMailer mailer, ILogger<HostedEventThankYouJob> logger)
    {
        _dbFactory = dbFactory;
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

        var recent = now.Date.AddDays(-(NoLaterThan.TotalDays + 2));
        var events = await db.HostedEvents
            .Include(e => e.Nights)
            .Include(e => e.Organization)
            .Where(e => e.LifecycleState == HostedEventLifecycleState.Ended
                     && e.SendThankYou
                     && e.ThankYouSentUtc == null
                     && e.EndsOn >= recent)
            .ToListAsync(ct);

        foreach (var ev in events)
        {
            if (ct.IsCancellationRequested) break;

            var (_, endUtc) = HostedEventCalendarSync.Window(ev, [.. ev.Nights.OrderBy(n => n.Date)]);
            if (now < endUtc + After || now > endUtc + NoLaterThan) continue;

            try
            {
                await ThankAsync(db, ev, now, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _logger.LogError(e, "Could not send the thank-you for {EventId}.", ev.Id);
            }
        }
    }

    private async Task ThankAsync(BenDataContext db, HostedEvent ev, DateTime now, CancellationToken ct)
    {
        var bookings = await db.HostedEventBookings
            .Include(b => b.LeadAppUser)
            .Where(b => b.HostedEventId == ev.Id
                     && b.Status == HostedEventBookingStatus.Confirmed
                     && b.ThankedUtc == null)
            .ToListAsync(ct);

        var hasGallery = await db.HostedEventGalleryImages.AnyAsync(g => g.HostedEventId == ev.Id, ct);

        var today = now.Date;
        var upcoming = await db.HostedEvents.AsNoTracking()
            .Where(e => e.OrganizationId == ev.OrganizationId && e.Id != ev.Id
                     && (e.LifecycleState == HostedEventLifecycleState.Published
                      || e.LifecycleState == HostedEventLifecycleState.Live)
                     && e.StartsOn >= today)
            .OrderBy(e => e.StartsOn)
            .Take(3)
            .ToListAsync(ct);

        var failed = 0;
        foreach (var booking in bookings)
        {
            booking.HostedEvent = ev;
            try
            {
                await _mailer.SendThankYouAsync(booking, hasGallery, upcoming, ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Not stamped: tried again next pass.
                _logger.LogWarning(e, "Could not thank booking {BookingId}.", booking.Id);
                failed++;
                continue;
            }

            booking.ThankedUtc = now;
            await db.SaveChangesAsync(ct);
        }

        if (failed > 0) return;

        ev.ThankYouSentUtc = now;
        await db.SaveChangesAsync(ct);
    }
}
