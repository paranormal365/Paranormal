using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// One event's numbers for its host (item 235 phase 17a, audit finding A5).
/// </summary>
/// <remarks>
/// <para><b>Places are counted per night.</b> A room on a three-night plan is three places to sell, and a party in it
/// for two of them has taken two. A unit held back for one night is one fewer place; held back for every night, it is
/// none.</para>
///
/// <para><b>Held counts as taken</b>, as it does on every plan: nobody else can pick it while somebody is deciding.</para>
/// </remarks>
public static class HostedEventSummary
{
    public static async Task<HostedEventSummaryRecord?> ReadAsync(BenDataContext db, Guid eventId, CancellationToken ct)
    {
        var ev = await db.HostedEvents.AsNoTracking()
            .Where(e => e.Id == eventId)
            .Select(e => new { e.DayPassCapacity, e.MinimumGuests })
            .FirstOrDefaultAsync(ct);
        if (ev is null) return null;

        var bookings = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.HostedEventId == eventId)
            .Select(b => new { b.Id, b.Status, b.Kind, b.PartySize })
            .ToListAsync(ct);

        var nights = await db.HostedEventNights.CountAsync(n => n.HostedEventId == eventId, ct);
        var units = await db.HostedEventLayoutUnits.CountAsync(u => u.HostedEventId == eventId, ct);
        var blocks = await db.HostedEventUnitBlocks.AsNoTracking()
            .Where(b => b.HostedEventLayoutUnit.HostedEventId == eventId)
            .Select(b => new { b.HostedEventLayoutUnitId, b.HostedEventNightId })
            .ToListAsync(ct);
        // A unit blocked for every night covers all of them; a unit also blocked on one night is not blocked twice.
        var everyNight = blocks.Where(b => b.HostedEventNightId == null).Select(b => b.HostedEventLayoutUnitId).ToHashSet();
        var heldBack = everyNight.Count * nights
                     + blocks.Where(b => b.HostedEventNightId != null && !everyNight.Contains(b.HostedEventLayoutUnitId))
                             .Select(b => (b.HostedEventLayoutUnitId, b.HostedEventNightId)).Distinct().Count();

        var taken = await db.HostedEventBookingNights.AsNoTracking()
            .Where(n => n.HostedEventBooking.HostedEventId == eventId
                     && n.HostedEventLayoutUnitId != null
                     && n.ReleasedUtc == null
                     && (n.HostedEventBooking.Status == HostedEventBookingStatus.Confirmed
                         || n.HostedEventBooking.Status == HostedEventBookingStatus.Held))
            .Select(n => new { n.HostedEventNightId, n.HostedEventLayoutUnitId })
            .Distinct()
            .CountAsync(ct);

        var arrived = await db.HostedEventCheckIns.AsNoTracking()
            .Where(c => c.HostedEventBooking.HostedEventId == eventId)
            .Select(c => c.HostedEventBookingId)
            .Distinct()
            .CountAsync(ct);
        var walkUps = await db.HostedEventWalkUps.AsNoTracking()
            .Where(w => w.HostedEventNight.HostedEventId == eventId)
            .SumAsync(w => (int?)w.People, ct) ?? 0;

        var (average, count) = await HostedEventReviews.RatingAsync(db, eventId, ct);
        var letters = await db.HostedEventAnnouncements.CountAsync(a => a.HostedEventId == eventId, ct);

        var confirmed = bookings.Where(b => b.Status == HostedEventBookingStatus.Confirmed).ToList();
        return new HostedEventSummaryRecord(
            Asking: bookings.Count(b => b.Status == HostedEventBookingStatus.Requested),
            Holding: bookings.Count(b => b.Status == HostedEventBookingStatus.Held),
            ConfirmedParties: confirmed.Count,
            ConfirmedPeople: confirmed.Sum(b => b.PartySize),
            NotComing: bookings.Count(b => b.Status is HostedEventBookingStatus.TurnedDown
                                                     or HostedEventBookingStatus.Expired
                                                     or HostedEventBookingStatus.Cancelled),
            PlacesOffered: Math.Max(0, units * nights - heldBack),
            PlacesTaken: taken,
            DayPassCapacity: ev.DayPassCapacity,
            DayPassPeople: confirmed.Where(b => b.Kind == HostedEventBookingKind.DayPass).Sum(b => b.PartySize),
            ArrivedParties: arrived,
            WalkUpPeople: walkUps,
            ReviewAverage: average,
            ReviewCount: count,
            LettersSent: letters,
            MinimumGuests: ev.MinimumGuests);
    }
}
