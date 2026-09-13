using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Admin;
using Ben.Service.Models.Admin;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Events;

/// <summary>
/// The numbers behind the SuperAdmin's events dashboard (item 235 phase 17b).
/// </summary>
/// <remarks>
/// <para><b>Counts, not people.</b> Like the site dashboard, every answer is a shape — totals, series, top groups and
/// venues by name — and none names a guest. The events list is where individual events are.</para>
///
/// <para><b>Two clocks.</b> The stat cards say where things stand now; the series and "bought" and "spent" cover the
/// chosen window, so a quiet month reads as a quiet month rather than as a small site.</para>
/// </remarks>
public static class HostedEventOversightStats
{
    public static async Task<AdminHostedEventStats> ReadAsync(BenDataContext db, int days, DateTime now, int topN, CancellationToken ct)
    {
        var since = now.Date.AddDays(-(days - 1));

        var byState = await db.HostedEvents.AsNoTracking()
            .GroupBy(e => e.LifecycleState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int Count(params HostedEventLifecycleState[] states) => byState.Where(s => states.Contains(s.State)).Sum(s => s.Count);

        var organizers = await db.HostedEvents.AsNoTracking().Select(e => e.OrganizationId).Distinct().CountAsync(ct);
        var venues = await db.OrganizationVenueProfiles.AsNoTracking().CountAsync(v => v.IsPublished, ct);
        var verified = await db.OrganizationVenueProfiles.AsNoTracking().CountAsync(v => v.IsPublished && v.VerifiedUtc != null, ct);

        var bought = await db.EventCredits.AsNoTracking()
            .Where(c => c.PurchasedUtc >= since && c.GrantedReason == null)
            .GroupBy(c => c.PurchasedUtc.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var spent = await db.EventCredits.AsNoTracking()
            .Where(c => c.SpentUtc >= since)
            .GroupBy(c => c.SpentUtc!.Value.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var held = await db.EventCredits.AsNoTracking()
            .CountAsync(c => c.SpentUtc == null && c.RefundedUtc == null && c.ExpiresUtc > now, ct);

        var created = await db.HostedEvents.AsNoTracking()
            .Where(e => e.DateCreated >= since)
            .GroupBy(e => e.DateCreated.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var published = await db.HostedEvents.AsNoTracking()
            .Where(e => e.FirstPublishedUtc >= since)
            .GroupBy(e => e.FirstPublishedUtc!.Value.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);
        var bookings = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.DateCreated >= since)
            .GroupBy(b => b.DateCreated.Date).Select(g => new { Day = g.Key, Count = g.Count() }).ToListAsync(ct);

        var bookingsByStatus = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.DateCreated >= since)
            .GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);

        var upcoming = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.Status == HostedEventBookingStatus.Confirmed
                     && HostedEventStates.TakingBookings.Contains(b.HostedEvent.LifecycleState))
            .SumAsync(b => (int?)b.PartySize, ct) ?? 0;

        var topOrganizers = await db.HostedEvents.AsNoTracking()
            .Where(e => e.LifecycleState != HostedEventLifecycleState.Draft)
            .GroupBy(e => e.OrganizationId).Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(topN).ToListAsync(ct);
        var orgIds = topOrganizers.Select(x => x.Key).ToList();
        var orgNames = await db.Organizations.AsNoTracking().Where(o => orgIds.Contains(o.Id))
            .ToDictionaryAsync(o => o.Id, o => o.Name, ct);

        var topVenues = await db.HostedEvents.AsNoTracking()
            .Where(e => e.LifecycleState != HostedEventLifecycleState.Draft)
            .GroupBy(e => e.PlaceId).Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(topN).ToListAsync(ct);
        var placeIds = topVenues.Select(x => x.Key).ToList();
        var placeNames = await db.Places.AsNoTracking().Where(p => placeIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name ?? p.City ?? "A place", ct);

        var topEvents = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.Status == HostedEventBookingStatus.Confirmed && b.HostedEvent.EndsOn >= since)
            .GroupBy(b => b.HostedEventId).Select(g => new { g.Key, Count = g.Sum(b => b.PartySize) })
            .OrderByDescending(x => x.Count).Take(topN).ToListAsync(ct);
        var eventIds = topEvents.Select(x => x.Key).ToList();
        var eventNames = await db.HostedEvents.AsNoTracking().Where(e => eventIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Name, ct);

        var regions = await db.HostedEvents.AsNoTracking()
            .Where(e => e.LifecycleState != HostedEventLifecycleState.Draft && e.Place.State != null && e.Place.State != "")
            .GroupBy(e => e.Place.State!).Select(g => new { g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count).Take(topN).ToListAsync(ct);

        var appeals = await db.HostedEventRemovals.AsNoTracking().CountAsync(r => r.AppealState == HostedEventAppealState.Waiting, ct);

        return new AdminHostedEventStats(
            OnTheSite: Count(HostedEventLifecycleState.Published, HostedEventLifecycleState.Live),
            Drafts: Count(HostedEventLifecycleState.Draft),
            Happened: Count(HostedEventLifecycleState.Ended, HostedEventLifecycleState.Archived),
            CalledOff: Count(HostedEventStates.CalledOff),
            Organizers: organizers,
            Venues: venues,
            VerifiedVenues: verified,
            CreditsBought: bought.Sum(x => x.Count),
            CreditsSpent: spent.Sum(x => x.Count),
            CreditsHeld: held,
            PeopleConfirmedUpcoming: upcoming,
            AppealsWaiting: appeals,
            EventsCreatedPerDay: AdminStatsController.FillDays(created.Select(x => (x.Day, x.Count)), since, days),
            EventsPublishedPerDay: AdminStatsController.FillDays(published.Select(x => (x.Day, x.Count)), since, days),
            BookingsPerDay: AdminStatsController.FillDays(bookings.Select(x => (x.Day, x.Count)), since, days),
            CreditsBoughtPerDay: AdminStatsController.FillDays(bought.Select(x => (x.Day, x.Count)), since, days),
            CreditsSpentPerDay: AdminStatsController.FillDays(spent.Select(x => (x.Day, x.Count)), since, days),
            EventsByState: [.. byState.OrderBy(s => s.State).Select(s => new StatSlice(StateLabel(s.State), s.Count))],
            BookingsByStatus: [.. bookingsByStatus.OrderBy(s => s.Key).Select(s => new StatSlice(StatusLabel(s.Key), s.Count))],
            TopOrganizers: [.. topOrganizers.Select(x => new StatSlice(orgNames.GetValueOrDefault(x.Key, "A group"), x.Count))],
            TopVenues: [.. topVenues.Select(x => new StatSlice(placeNames.GetValueOrDefault(x.Key, "A place"), x.Count))],
            TopEventsByPeople: [.. topEvents.Select(x => new StatSlice(eventNames.GetValueOrDefault(x.Key, "An event"), x.Count))],
            EventsByRegion: [.. regions.Select(x => new StatSlice(x.Key, x.Count))]);
    }

    public static string StateLabel(HostedEventLifecycleState state) => state switch
    {
        HostedEventLifecycleState.Draft => "Draft",
        HostedEventLifecycleState.Published => "Published",
        HostedEventLifecycleState.Live => "On now",
        HostedEventLifecycleState.Ended => "Ended",
        HostedEventLifecycleState.Archived => "Archived",
        HostedEventLifecycleState.Cancelled => "Called off",
        HostedEventLifecycleState.VenueWithdrawn => "Venue withdrew",
        HostedEventLifecycleState.Removed => "Removed",
        _ => state.ToString(),
    };

    private static string StatusLabel(HostedEventBookingStatus status) => status switch
    {
        HostedEventBookingStatus.Requested => "Asked",
        HostedEventBookingStatus.Confirmed => "Confirmed",
        HostedEventBookingStatus.TurnedDown => "Turned down",
        HostedEventBookingStatus.Held => "Holding",
        HostedEventBookingStatus.Expired => "Hold lapsed",
        HostedEventBookingStatus.Cancelled => "Released",
        _ => status.ToString(),
    };
}
