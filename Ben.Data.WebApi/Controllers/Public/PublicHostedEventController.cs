using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Entities;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Hosted events as a visitor sees them (item 235).
/// </summary>
/// <remarks>
/// <para><b>Anonymous, like every other public surface here.</b> Somebody deciding whether to come
/// to a weekend is exactly the person with no account, and asking them to make one before they can
/// read what it is would be the wrong way round.</para>
///
/// <para><b>Published only.</b> A draft has no page at all — not a page that says "not yet", which
/// would leak that something is being planned and when. It answers 404, as though it did not
/// exist, because to a visitor it does not.</para>
///
/// <para><b>The umbrella row is still the one that carries sign-ups and reminders.</b> This adds
/// what an umbrella cannot say: the separate dates, the venue's own name, and whether it is a stay
/// or a run.</para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/hosted-events")]
public sealed class PublicHostedEventController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicHostedEventController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>One event, by its id.</summary>
    [HttpGet("{eventId:guid}")]
    public async Task<ActionResult<PublicHostedEventRecord>> GetOne(Guid eventId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var found = await LoadAsync(db, e => e.Id == eventId, ct);
        return found is null ? NotFound() : Ok(found);
    }

    /// <summary>
    /// One event, by the organization and the slug — the address on the poster.
    /// </summary>
    /// <remarks>
    /// The same slug the umbrella calendar row carries, so <c>/o/{org}/events/{slug}</c> resolves
    /// whichever way a reader arrives at it.
    /// </remarks>
    [HttpGet("~/api/public/organizations/{orgUrlName}/hosted-events/{slug}")]
    public async Task<ActionResult<PublicHostedEventRecord>> GetBySlug(
        string orgUrlName, string slug, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var found = await LoadAsync(
            db, e => e.UrlName == slug && e.Organization.UrlName == orgUrlName, ct);
        return found is null ? NotFound() : Ok(found);
    }

    /// <summary>Upcoming published events, newest date first, optionally for one organization.</summary>
    /// <remarks>
    /// An event that finished yesterday is not "what's on", so the list is bounded by the last
    /// date rather than the first: a weekend already under way is still worth showing on its
    /// Saturday.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicHostedEventRecord>>> GetUpcoming(
        [FromQuery] string? organization, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var today = DateTime.UtcNow.Date.AddDays(-1);
        var query = Visible(db).Where(e => e.EndsOn >= today);

        if (!string.IsNullOrWhiteSpace(organization))
            query = query.Where(e => e.Organization.UrlName == organization);

        var rows = await query
            .OrderBy(e => e.StartsOn)
            .Take(Math.Clamp(take, 1, 200))
            .Select(e => new Row(e, e.Organization.Name, e.Organization.UrlName, e.Place,
                                 db.OrgCalendarEvents.Where(c => c.HostedEventId == e.Id)
                                     .Select(c => c.Id).FirstOrDefault(),
                                 e.Nights.OrderBy(n => n.Date).ToList()))
            .ToListAsync(ct);

        return Ok(rows.Select(ToRecord).ToList());
    }

    /// <summary>
    /// The plan of a published event, and what every square is on every night (phase 6).
    /// </summary>
    /// <remarks>
    /// <para><b>Anonymous, like the page it draws.</b> Somebody deciding whether to come is exactly
    /// the person with no account, and a seating plan they cannot see until they make one is a
    /// seating plan that sells nothing. Signed out, every square is drawn and none of them answer.
    /// </para>
    ///
    /// <para><b>States, never names.</b> Who is in row C is the venue's business. This says only
    /// what a square IS, which is everything a guest choosing one needs and nothing more.</para>
    ///
    /// <para><b>Only the squares that are not free come back</b>, because on the day an event opens
    /// almost all of them are, and a four-hundred-seat house across three nights is twelve hundred
    /// rows of "nothing here".</para>
    /// </remarks>
    [HttpGet("{eventId:guid}/plan")]
    public async Task<ActionResult<PublicHostedEventPlanRecord>> GetPlan(
        Guid eventId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var ev = await Visible(db)
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return NotFound();

        var units = await db.HostedEventLayoutUnits.AsNoTracking()
            .Include(u => u.PlaceRoom)
            .Where(u => u.HostedEventId == eventId)
            .OrderBy(u => u.SortOrder)
            .ToListAsync(ct);

        var occupancy = await PlanOccupancy.ReadAsync(db, eventId, ct);
        var blocks = await PlanOccupancy.BlocksAsync(db, eventId, ct);

        // WHO IS READING, WHEN THE SERVER CAN TELL.
        //
        // On an [AllowAnonymous] endpoint only the default scheme populates User, so a reader
        // signed in some other way arrives here as a stranger and their own squares would come
        // back as somebody else's holds. The page therefore marks its own cells as well, from the
        // booking it already has. This is the better answer where it is available, not the only
        // one.
        var reader = GetCurrentUserIdOrNull();
        var mine = reader is { } who
            ? (await db.HostedEventBookingNights.AsNoTracking()
                .Where(n => n.HostedEventBooking.HostedEventId == eventId
                         && n.HostedEventBooking.LeadAppUserId == who
                         && n.HostedEventLayoutUnitId != null
                         && n.ReleasedUtc == null)
                .Select(n => new { n.HostedEventNightId, UnitId = n.HostedEventLayoutUnitId!.Value })
                .ToListAsync(ct))
                .Select(n => (n.HostedEventNightId, n.UnitId))
                .ToHashSet()
            : [];

        var nights = ev.Nights.OrderBy(n => n.Date).ToList();
        var cells = new List<PublicHostedEventPlanCellRecord>();

        foreach (var night in nights)
        {
            foreach (var unit in units)
            {
                var state = CellState(occupancy, blocks, mine, night.Id, unit.Id);
                if (state == HostedEventPlanCellState.Free) continue;

                cells.Add(new PublicHostedEventPlanCellRecord(night.Id, unit.Id, state));
            }
        }

        var closed = WhyNothingCanBeBooked(ev, DateTime.UtcNow);

        return Ok(new PublicHostedEventPlanRecord(
            ev.Id,
            ev.LayoutKind,
            ev.BookingMode,
            IsPicking: ev.BookingMode == HostedEventBookingMode.Pick && closed is null,
            ClosedSentence: closed,
            ev.HoldMinutes,
            EventCapacity.MaxPartySize,
            [.. nights.Select(n => HostedEventController.ToNight(n, ev))],
            [.. units.Select(u => new PublicHostedEventPlanUnitRecord(
                u.Id,
                EventCapacity.NameOf(u),
                u.Section,
                EventCapacity.CapacityOf(u),
                u.Price,
                u.LayoutRow,
                u.LayoutColumn,
                u.SortOrder))],
            cells));
    }

    /// <summary>
    /// What one square is on one night, in the order the answers override each other.
    /// </summary>
    /// <remarks>
    /// <b>Mine first.</b> A square this reader is holding is theirs whether the venue has agreed
    /// yet or not, and drawing it as "somebody else is holding this" would tell a guest their own
    /// choice had gone. Then what is actually held, then what the venue has kept back — a blocked
    /// square that somebody is nevertheless in is a fact the venue needs and a guest cannot act
    /// on either way.
    /// </remarks>
    private static HostedEventPlanCellState CellState(
        IReadOnlyDictionary<(Guid NightId, Guid UnitId), PlanOccupancy.Cell> occupancy,
        IReadOnlyList<HostedEventUnitBlock> blocks,
        IReadOnlySet<(Guid NightId, Guid UnitId)> mine,
        Guid nightId, Guid unitId)
    {
        if (mine.Contains((nightId, unitId))) return HostedEventPlanCellState.Mine;

        if (occupancy.TryGetValue((nightId, unitId), out var cell) && cell.HeldAs is { } held)
            return held == HostedEventBookingStatus.Held
                ? HostedEventPlanCellState.Pending
                : HostedEventPlanCellState.Taken;

        var block = blocks.FirstOrDefault(
            b => b.HostedEventLayoutUnitId == unitId
              && (b.HostedEventNightId is null || b.HostedEventNightId == nightId));

        return block is null ? HostedEventPlanCellState.Free
            : block.Kind == HostedEventBlockKind.HouseHeld
                ? HostedEventPlanCellState.Blocked
                : HostedEventPlanCellState.NotOffered;
    }

    /// <summary>Why no place can be had at this event, in words, or null when one can.</summary>
    /// <remarks>
    /// A greyed-out grid with no sentence beside it is the commonest way a page wastes somebody's
    /// afternoon: "called off", "bookings closed on Friday" and "this event has happened" are
    /// three different facts and only one of them is worth waiting for.
    /// </remarks>
    private static string? WhyNothingCanBeBooked(HostedEvent ev, DateTime utcNow)
    {
        if (HostedEventStates.CalledOff.Contains(ev.LifecycleState))
            return ev.LifecycleState == HostedEventLifecycleState.VenueWithdrawn
                ? "The venue has withdrawn, so nothing can be booked."
                : "This event has been called off.";

        if (!HostedEventStates.TakingBookings.Contains(ev.LifecycleState))
            return "This event has happened.";

        return EventCapacity.IsOpenForRequests(ev, utcNow)
            ? null
            : "Bookings for this one have closed.";
    }

    private static IQueryable<HostedEvent> Visible(BenDataContext db)
        => db.HostedEvents.AsNoTracking()
            .Where(e => HostedEventStates.OnThePublicSite.Contains(e.LifecycleState));

    private static async Task<PublicHostedEventRecord?> LoadAsync(
        BenDataContext db, System.Linq.Expressions.Expression<Func<HostedEvent, bool>> match,
        CancellationToken ct)
    {
        var row = await Visible(db).Where(match)
            .Select(e => new Row(e, e.Organization.Name, e.Organization.UrlName, e.Place,
                                 db.OrgCalendarEvents.Where(c => c.HostedEventId == e.Id)
                                     .Select(c => c.Id).FirstOrDefault(),
                                 e.Nights.OrderBy(n => n.Date).ToList()))
            .FirstOrDefaultAsync(ct);

        return row is null ? null : ToRecord(row);
    }

    private sealed record Row(
        HostedEvent Event, string OrgName, string OrgUrlName, Place? Place,
        Guid UmbrellaId, List<HostedEventNight> Nights);

    private static PublicHostedEventRecord ToRecord(Row r)
    {
        // The address is withheld the same way a public calendar event withholds it: a visitor
        // who has not got a place sees the town, and the host decides whether that is enough.
        var hidden = r.Event.HideExactLocation;
        var exact = hidden || r.Place is null
            ? null
            : string.Join(", ", new[]
                {
                    r.Place.StreetAddress1, r.Place.StreetAddress2,
                    r.Place.City, r.Place.State, r.Place.ZipCode,
                }.Where(p => !string.IsNullOrWhiteSpace(p)));

        // Asked once: the sentence and the flag are two readings of one fact, and computing it
        // twice is how they eventually disagree.
        var closed = WhyNothingCanBeBooked(r.Event, DateTime.UtcNow);

        return new PublicHostedEventRecord(
            r.Event.Id,
            r.UmbrellaId,
            r.OrgName,
            r.OrgUrlName,
            r.Event.Name,
            r.Event.UrlName,
            r.Event.Tagline,
            r.Event.Description,
            r.Event.TimeZoneId,
            r.Event.StartsOn,
            r.Event.EndsOn,
            r.Event.DatesAreSeparate,
            HostedEventController.DateNoun(r.Event),
            r.Place?.Name,
            r.Place?.City,
            r.Place?.State,
            exact is { Length: > 0 } ? exact : null,
            hidden,
            r.Place?.Latitude,
            r.Place?.Longitude,
            r.Event.DayPassCapacity,
            r.Event.ContactLine,
            r.Event.CoverUploadFileId,
            HostedEventStates.CalledOff.Contains(r.Event.LifecycleState),
            r.Event.CancelledReason,
            r.Event.CollectsEvidence,
            [.. r.Nights.Select(n => HostedEventController.ToNight(n, r.Event))],
            r.Event.BookingMode,
            r.Event.LifecycleState,
            IsTakingBookings: closed is null,
            NotTakingBookingsSentence: closed,
            r.Event.BookingsCloseAtUtc,
            r.Event.DayPassPrice,
            r.Event.MinimumGuests,
            r.Event.GoNoGoDeadlineUtc,
            r.Event.GoNoGoDecision);
    }
}
