using Ben.Data.Common.Enums;
using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// The next few things the signed-in person is going to, for the countdown on Home (10/16/2026).
/// </summary>
/// <remarks>
/// <para>Ben: "When a person signs up for an event, maybe we show how long until the event starts
/// with a tiny card on the home page after they log in." The answers already exist on four screens —
/// <c>/my-events</c> (hosted bookings), the RSVPs behind <c>api/public/events/mine</c>, tour seats,
/// and <c>/my-investigations</c> — and combining them in the browser would be four calls and four
/// copies of "which of these count" on every visit to Home. One small read, here, with each source's
/// own rule restated beside it.</para>
///
/// <para><b>What counts as going.</b></para>
/// <list type="bullet">
/// <item>A hosted-event booking that is Confirmed, Held or Requested — the three /my-events lists as
/// "coming". The two that hold no place are labeled as such ("Asked for", "Held for you"), never
/// "You're coming": the date is in their diary, and a countdown that silently ignored it would
/// disagree with the page the bell sends them to.</item>
/// <item>A calendar event or tour date they said yes to (RSVP Accepted), or a tour seat they asked
/// for and nobody has turned down yet. A bare invitation is not "going".</item>
/// <item>An investigation they are on the team of, not declined and not called off — the member
/// desk's rule for its "Next investigation".</item>
/// </list>
///
/// <para><b>Nothing already over.</b> An event under way is kept: the card says "Happening now".</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me/next-events")]
public sealed class MyNextEventsController : BenControllerBase
{
    /// <summary>How many the card is handed: one to show, and the next few to move on to.</summary>
    public const int Cap = 10;

    /// <summary>An investigation with no end time is treated as running this long.</summary>
    public static readonly TimeSpan InvestigationWithoutEnd = TimeSpan.FromHours(4);

    private readonly IDbContextFactory<BenDataContext> _db;

    public MyNextEventsController(IDbContextFactory<BenDataContext> db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<MyNextEventsResponse>> Get(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var items = new List<MyNextEventItem>();
        items.AddRange(await HostedBookingsAsync(db, userId, now, ct));
        items.AddRange(await CalendarSignUpsAsync(db, userId, now, ct));
        items.AddRange(await InvestigationsAsync(db, userId, now, ct));

        var upcoming = items
            .Where(i => i.EndsAtUtc > now)
            .OrderBy(i => i.StartsAtUtc)
            .ThenBy(i => i.Title, StringComparer.Ordinal)
            .ToList();

        return Ok(new MyNextEventsResponse([.. upcoming.Take(Cap)], upcoming.Count));
    }

    // ── Hosted events ────────────────────────────────────────────────────────

    private static async Task<List<MyNextEventItem>> HostedBookingsAsync(
        BenDataContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var yesterday = now.Date.AddDays(-1);
        var bookings = await PublicHostedEventBookingController.MineQuery(db, userId)
            .Where(b => b.Status == HostedEventBookingStatus.Confirmed
                     || b.Status == HostedEventBookingStatus.Held
                     || b.Status == HostedEventBookingStatus.Requested)
            // A called-off event is not counted down to. Its own page says what happened.
            .Where(b => b.HostedEvent.LifecycleState != HostedEventLifecycleState.Cancelled
                     && b.HostedEvent.LifecycleState != HostedEventLifecycleState.VenueWithdrawn
                     && b.HostedEvent.LifecycleState != HostedEventLifecycleState.Removed)
            // A coarse cut in the database; the exact one is on the night's end, below.
            .Where(b => b.HostedEvent.EndsOn >= yesterday)
            .ToListAsync(ct);

        // One per event, the live booking first and then the newest: the same choice /my-events makes.
        bookings = [.. bookings
            .GroupBy(b => b.HostedEventId)
            .Select(g => g.OrderBy(b => b.Status switch
                {
                    HostedEventBookingStatus.Confirmed => 0,
                    HostedEventBookingStatus.Held => 1,
                    _ => 2,
                })
                .ThenByDescending(b => b.DateCreated)
                .First())];

        // A day pass books no night; it is the event's whole span. Read once for all of them.
        var dayPassEvents = bookings.Where(b => !b.Nights.Any(n => n.ReleasedUtc == null))
                                    .Select(b => b.HostedEventId).Distinct().ToList();
        var allNights = dayPassEvents.Count == 0
            ? []
            : await db.HostedEventNights.AsNoTracking()
                .Where(n => dayPassEvents.Contains(n.HostedEventId))
                .ToListAsync(ct);

        var list = new List<MyNextEventItem>();
        foreach (var b in bookings)
        {
            // The nights still theirs: one a venue released is not a night they are coming to.
            var booked = b.Nights.Where(n => n.ReleasedUtc == null).Select(n => n.HostedEventNight).ToList();
            var nights = booked.Count > 0
                ? booked.OrderBy(n => n.Date).ToList()
                : allNights.Where(n => n.HostedEventId == b.HostedEventId).OrderBy(n => n.Date).ToList();

            // The event's own window rule (doors at seven unless a night says otherwise), on the
            // nights this person is coming to.
            var (startUtc, endUtc) = HostedEventCalendarSync.Window(b.HostedEvent, nights);

            var href = b.Status == HostedEventBookingStatus.Confirmed
                ? $"/my-events/{b.HostedEventId}/pass"
                : b.HostedEvent.Organization?.UrlName is { Length: > 0 } org && b.HostedEvent.UrlName is { Length: > 0 } slug
                    ? $"/o/{org}/events/{slug}"
                    : "/my-events";

            list.Add(new MyNextEventItem(
                MyNextEventKind.HostedEvent, b.HostedEventId, b.HostedEvent.Name,
                b.HostedEvent.Organization?.Name, startUtc, endUtc, b.HostedEvent.TimeZoneId, href,
                b.Status switch
                {
                    HostedEventBookingStatus.Confirmed => MyNextEventStanding.Going,
                    HostedEventBookingStatus.Held => MyNextEventStanding.HeldForYou,
                    _ => MyNextEventStanding.AskedFor,
                },
                b.DateCreated));
        }
        return list;
    }

    // ── Calendar events and tour dates ───────────────────────────────────────

    private static async Task<List<MyNextEventItem>> CalendarSignUpsAsync(
        BenDataContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var groups = await PublicEventController.ReaderOrganizationIdsAsync(db, userId, ct);

        var rows = await db.OrgCalendarEventAttendees.AsNoTracking()
            .Where(a => a.AppUserId == userId
                     // Going: a yes, or a tour seat asked for and not turned down.
                     && (a.RsvpStatus == RsvpStatus.Accepted
                         || (a.SeatStatus == TourSeatStatus.Requested && a.RsvpStatus != RsvpStatus.Declined))
                     && a.SeatStatus != TourSeatStatus.TurnedDown
                     && a.OrgCalendarEvent.EndDateTime > now
                     // A hosted event's calendar row mirrors its bookings, which are counted above.
                     && a.OrgCalendarEvent.HostedEventId == null)
            .Select(a => new
            {
                a.OrgCalendarEventId,
                a.RsvpStatus,
                a.SeatStatus,
                SignedUp = a.DateRsvp ?? a.DateCreated,
                Event = a.OrgCalendarEvent,
                OrgName = a.OrgCalendarEvent.Organization.Name,
                OrgUrl = a.OrgCalendarEvent.Organization.UrlName,
                TourZone = a.OrgCalendarEvent.Tour != null ? a.OrgCalendarEvent.Tour.TimeZoneId : null,
                PlaceKind = a.OrgCalendarEvent.Place != null ? (PlaceKind?)a.OrgCalendarEvent.Place.Kind : null,
            })
            .ToListAsync(ct);

        var list = new List<MyNextEventItem>();
        foreach (var r in rows)
        {
            var e = r.Event;
            var isPublicPage = e.IsPublic && e.CaseId == null
                            && (r.PlaceKind is null || r.PlaceKind == PlaceKind.PublicLocation)
                            && !string.IsNullOrWhiteSpace(e.UrlName);
            var isMember = groups.Contains(e.OrganizationId);

            // Somewhere to go, or it is not shown: the public page, or the group's calendar for a
            // member. An event neither can open is a card that leads nowhere.
            string? href = isPublicPage ? $"/o/{r.OrgUrl}/events/{e.UrlName}"
                         : isMember ? $"/organizations/{e.OrganizationId}/calendar"
                         : null;
            if (href is null) continue;

            list.Add(new MyNextEventItem(
                e.TourId is null ? MyNextEventKind.Event : MyNextEventKind.TourDate,
                e.Id, e.Title, r.OrgName, e.StartDateTime, e.EndDateTime,
                e.TimeZoneId ?? r.TourZone, href,
                r.RsvpStatus == RsvpStatus.Accepted ? MyNextEventStanding.Going : MyNextEventStanding.AskedFor,
                r.SignedUp));
        }
        return list;
    }

    // ── Investigations ───────────────────────────────────────────────────────

    private static async Task<List<MyNextEventItem>> InvestigationsAsync(
        BenDataContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var horizon = now - InvestigationWithoutEnd;

        // The member desk's rule: on the team, not declined, not called off, not over.
        var rows = await db.InvestigationAttendees.AsNoTracking()
            .Where(a => a.AppUserId == userId
                     && a.Rsvp != RsvpStatus.Declined
                     && a.Investigation.Status != InvestigationStatus.Cancelled
                     && a.Investigation.Status != InvestigationStatus.Completed
                     && (a.Investigation.EndDateTime != null
                            ? a.Investigation.EndDateTime > now
                            : a.Investigation.ScheduledDateTime > horizon))
            .Select(a => new
            {
                a.Investigation.Id, a.Investigation.Title, a.Investigation.UrlName,
                a.Investigation.ScheduledDateTime, a.Investigation.EndDateTime,
                OrgName = a.Investigation.Organization.Name,
                OrgUrl = a.Investigation.Organization.UrlName,
                Zone = a.Investigation.TimeZoneId,
                CaseZone = a.Investigation.Case != null ? a.Investigation.Case.TimeZoneId : null,
                OrgZone = a.Investigation.Organization.TimeZoneId,
                a.Rsvp,
                a.DateCreated,
            })
            .ToListAsync(ct);

        return [.. rows.Select(r => new MyNextEventItem(
            MyNextEventKind.Investigation, r.Id, r.Title, r.OrgName,
            r.ScheduledDateTime,
            r.EndDateTime is { } end && end > r.ScheduledDateTime ? end : r.ScheduledDateTime + InvestigationWithoutEnd,
            Zones.Effective(r.Zone, r.CaseZone, r.OrgZone),
            string.IsNullOrWhiteSpace(r.UrlName) ? "/my-investigations" : $"/o/{r.OrgUrl}/investigations/{r.UrlName}",
            r.Rsvp == RsvpStatus.Accepted ? MyNextEventStanding.Going : MyNextEventStanding.Invited,
            r.DateCreated))];
    }
}
