using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// A guest's own side of a hosted event: asking for a place, seeing what the venue said, and
/// changing or withdrawing it (item 235 phase 2).
/// </summary>
/// <remarks>
/// <para><b>Asking is not having.</b> A request holds no room and no day pass — the venue decides
/// (DECISION 7) — so this door can stay open on an event that is already full, which is what turns
/// the overflow into a waiting list the host can work through rather than a closed door.</para>
///
/// <para><b>Withdrawing means two different things.</b> While it is still a request, a guest simply
/// takes it back and it is gone from the host's queue. Once the venue has confirmed, the venue has
/// catered, staffed and possibly turned somebody else away against it, so the guest ASKS to cancel
/// and the host releases it. Letting a confirmed booking vanish on a click would free a room the
/// host does not know is free.</para>
///
/// <para><b>One booking per person per event.</b> Pressing the button again is the same statement,
/// not a second party — the same rule a tour date's sign-up keeps, and for the same reason.</para>
/// </remarks>
[Authorize]
[Route("api/public/hosted-events")]
public sealed class PublicHostedEventBookingController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicHostedEventBookingController(IDbContextFactory<BenDataContext> db) { _db = db; }

    // ── what I have asked for ────────────────────────────────────────────────

    /// <summary>Every hosted event this person has a booking at, coming up or recently past.</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<MyHostedEventBookingRecord>>> GetMine(
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var bookings = await MineQuery(db, userId)
            .OrderBy(b => b.HostedEvent.StartsOn)
            .ToListAsync(ct);

        return Ok(bookings.Select(ToMine).ToList());
    }

    /// <summary>This person's booking at one event, or 404 when they have none.</summary>
    [HttpGet("{eventId:guid}/my-booking")]
    public async Task<ActionResult<MyHostedEventBookingRecord>> GetMyBooking(
        Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var booking = await MineQuery(db, userId)
            .FirstOrDefaultAsync(b => b.HostedEventId == eventId, ct);

        return booking is null ? NotFound() : Ok(ToMine(booking));
    }

    /// <summary>
    /// What is being served, for a guest whose place the venue has agreed to.
    /// </summary>
    /// <remarks>
    /// <para><b>Confirmed guests, not the world.</b> A menu is part of what somebody has booked,
    /// and a venue that has not sold the weekend yet may not want its catering costed by the hotel
    /// down the road. A guest still waiting is told the venue publishes it once the place is
    /// agreed, which is a sentence they can act on.</para>
    ///
    /// <para>Every sitting of every night comes back in one answer — dinner, the late supper, the
    /// breakfast the next morning — in the order the host arranged them.</para>
    /// </remarks>
    [HttpGet("{eventId:guid}/menus")]
    public async Task<ActionResult<HostedEventMenusRecord>> GetMenus(
        Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var confirmed = await db.HostedEventBookings.AsNoTracking()
            .AnyAsync(b => b.HostedEventId == eventId && b.LeadAppUserId == userId
                        && b.Status == HostedEventBookingStatus.Confirmed, ct);
        if (!confirmed)
            return StatusCode(StatusCodes.Status403Forbidden,
                "The venue publishes the menu once your place is agreed.");

        return Ok(await Entities.HostedEventMenuController.MenusAsync(db, eventId, ct));
    }

    // ── asking ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Asks the venue for a place.
    /// </summary>
    /// <remarks>
    /// <para>Records a REQUEST and nothing more. The rooms named here are a preference — the venue
    /// puts them where it can when it confirms — so this is deliberately not checked against
    /// capacity: refusing a request because a room is full would close the waiting list the host
    /// wants.</para>
    ///
    /// <para>What IS checked: the event exists and is published, it has not been called off, and
    /// the deadline has not passed.</para>
    /// </remarks>
    [HttpPost("{eventId:guid}/bookings")]
    public async Task<ActionResult<MyHostedEventBookingRecord>> Request(
        Guid eventId, [FromBody] RequestHostedEventBookingRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var ev = await BookableEventAsync(db, eventId, ct);
        if (ev is null) return NotFound();
        if (ev.CancelledAtUtc is not null)
            return Conflict("This event has been called off.");
        if (!EventCapacity.IsOpenForRequests(ev, DateTime.UtcNow))
            return Conflict("This event has stopped taking bookings.");
        if (request.Kind == HostedEventBookingKind.DayPass && ev.DayPassCapacity is 0)
            return Conflict("This event isn't selling day passes.");

        // Pressing the button twice is the same statement, not a second party.
        var existing = await db.HostedEventBookings
            .Include(b => b.Nights).Include(b => b.Guests)
            .FirstOrDefaultAsync(b => b.HostedEventId == eventId && b.LeadAppUserId == userId
                                   && b.Status != HostedEventBookingStatus.Cancelled, ct);
        if (existing is not null)
            return Conflict("You have already asked for a place at this event.");

        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(),
            HostedEventId = eventId,
            LeadAppUserId = userId,
            PartySize = EventCapacity.ClampPartySize(request.PartySize),
            Kind = request.Kind,
            Status = HostedEventBookingStatus.Requested,
            Note = Trimmed(request.Note),
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.HostedEventBookings.Add(booking);

        if (await WhyTheseNightsAreNotRealAsync(db, eventId, request.Nights ?? [], ct) is { } bad)
            return BadRequest(bad);

        WriteNights(booking, request.Nights ?? []);
        WriteGuests(booking, request.Guests ?? []);

        await db.SaveChangesAsync(ct);
        return Ok(await ReloadAsync(db, userId, booking.Id, ct));
    }

    /// <summary>
    /// Changes a booking the guest already has.
    /// </summary>
    /// <remarks>
    /// Allowed while it is still a request, and after it is confirmed too — a party genuinely does
    /// lose somebody on the Thursday. But a change to a CONFIRMED booking sends it back to the
    /// venue as a request, because the thing the venue agreed to is not the thing being asked for
    /// any more, and a party that quietly grew from two to six would be sleeping in a room nobody
    /// checked.
    /// </remarks>
    [HttpPut("{eventId:guid}/my-booking")]
    public async Task<ActionResult<MyHostedEventBookingRecord>> UpdateMyBooking(
        Guid eventId, [FromBody] EditHostedEventBookingRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var booking = await db.HostedEventBookings
            .Include(b => b.Nights).Include(b => b.Guests)
            .FirstOrDefaultAsync(b => b.HostedEventId == eventId && b.LeadAppUserId == userId
                                   && b.Status != HostedEventBookingStatus.Cancelled, ct);
        if (booking is null) return NotFound();
        if (booking.Status == HostedEventBookingStatus.TurnedDown)
            return Conflict("This booking was turned down. Ask the venue if anything has changed.");

        var wasConfirmed = booking.Status == HostedEventBookingStatus.Confirmed;
        var changesWhatWasAgreed =
            (request.PartySize is int size && EventCapacity.ClampPartySize(size) != booking.PartySize)
            || request.Nights is not null;

        if (request.PartySize is int party)
            booking.PartySize = EventCapacity.ClampPartySize(party);
        if (request.Note is not null) booking.Note = Trimmed(request.Note);

        if (request.Nights is not null)
        {
            if (await WhyTheseNightsAreNotRealAsync(db, eventId, request.Nights, ct) is { } bad)
                return BadRequest(bad);
            db.HostedEventBookingNights.RemoveRange(booking.Nights);
            booking.Nights.Clear();
            WriteNights(booking, request.Nights);
        }

        if (request.Guests is not null)
        {
            db.HostedEventBookingGuests.RemoveRange(booking.Guests);
            booking.Guests.Clear();
            WriteGuests(booking, request.Guests);
        }

        // Back to the queue, and the room-nights it held are released with the umbrella row: what
        // the venue agreed to is not what is being asked for any more.
        if (wasConfirmed && changesWhatWasAgreed)
        {
            booking.Status = HostedEventBookingStatus.Requested;
            booking.DecidedUtc = null;
            booking.DecidedByAppUserId = null;
            booking.DecisionNote = null;
            booking.GuestAcknowledgedUtc = null;
            await ReleaseUmbrellaAsync(db, booking, ct);
        }

        booking.DateUpdated = DateTime.UtcNow;
        booking.UpdatedByAppUserId = userId;

        await db.SaveChangesAsync(ct);
        return Ok(await ReloadAsync(db, userId, booking.Id, ct));
    }

    // ── reading the answer ───────────────────────────────────────────────────

    /// <summary>
    /// Says the guest has read what the venue decided. Clears their bell.
    /// </summary>
    /// <remarks>
    /// Any answer, not only a yes. A refusal that could never be acknowledged would sit on
    /// somebody's bell for ever, and saying "got it" to bad news is exactly as reasonable as
    /// saying it to good news.
    /// </remarks>
    [HttpPost("{eventId:guid}/my-booking/acknowledge")]
    public async Task<ActionResult<MyHostedEventBookingRecord>> Acknowledge(
        Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var booking = await db.HostedEventBookings
            .FirstOrDefaultAsync(b => b.HostedEventId == eventId && b.LeadAppUserId == userId, ct);
        if (booking is null) return NotFound();

        if (booking.Status is not (HostedEventBookingStatus.Confirmed
                                or HostedEventBookingStatus.TurnedDown
                                or HostedEventBookingStatus.Cancelled))
            return Conflict("The venue hasn't answered this one yet.");

        // Idempotent, and the FIRST time is the one worth keeping.
        booking.GuestAcknowledgedUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Ok(await ReloadAsync(db, userId, booking.Id, ct));
    }

    // ── withdrawing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Takes a request back, or asks the venue to release a confirmed booking.
    /// </summary>
    /// <remarks>
    /// <para><b>A request is simply withdrawn</b> — nobody was holding anything, and leaving it in
    /// the host's queue would have them decide on a party that is not coming.</para>
    ///
    /// <para><b>A confirmed booking is not.</b> The venue has catered, staffed and possibly turned
    /// somebody else away against it, so this records the ask and tells them; the host releases it
    /// from their own screen. A room freed without the host knowing is a room that stays empty.</para>
    /// </remarks>
    [HttpDelete("{eventId:guid}/my-booking")]
    public async Task<ActionResult<MyHostedEventBookingRecord>> Withdraw(
        Guid eventId, [FromQuery] string? reason, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var booking = await db.HostedEventBookings
            .Include(b => b.Nights).Include(b => b.Guests)
            .FirstOrDefaultAsync(b => b.HostedEventId == eventId && b.LeadAppUserId == userId
                                   && b.Status != HostedEventBookingStatus.Cancelled, ct);
        if (booking is null) return NotFound();

        if (booking.Status == HostedEventBookingStatus.Requested)
        {
            db.HostedEventBookingNights.RemoveRange(booking.Nights);
            db.HostedEventBookingGuests.RemoveRange(booking.Guests);
            db.HostedEventBookings.Remove(booking);
            await db.SaveChangesAsync(ct);
            return NoContent();
        }

        // Confirmed, or already turned down: record the ask rather than acting on it.
        booking.CancellationRequestedUtc ??= DateTime.UtcNow;
        booking.CancellationReason = Trimmed(reason);
        booking.DateUpdated = DateTime.UtcNow;
        booking.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        return Ok(await ReloadAsync(db, userId, booking.Id, ct));
    }

    // ── the work ─────────────────────────────────────────────────────────────

    /// <summary>
    /// An event a stranger may book: published, not archived, and belonging to a live group.
    /// </summary>
    /// <remarks>
    /// A draft is not bookable. Publishing is the moment an event exists for anybody else, and it
    /// is the moment that spends a credit — so a booking against a draft would be a place at
    /// something nobody has paid for.
    /// </remarks>
    private static Task<HostedEvent?> BookableEventAsync(
        BenDataContext db, Guid eventId, CancellationToken ct)
        => db.HostedEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.IsPublished && e.ArchivedAtUtc == null, ct);

    /// <summary>
    /// Why these nights are not nights of this event, or null when they are.
    /// </summary>
    /// <remarks>
    /// A room is NOT checked for being offered: a guest naming a room the event is not using is
    /// expressing a preference the venue will override anyway, and refusing it would be a form
    /// error about somebody else's booking system.
    /// </remarks>
    private static async Task<string?> WhyTheseNightsAreNotRealAsync(
        BenDataContext db, Guid eventId,
        IReadOnlyList<HostedEventBookingNightChoice> nights, CancellationToken ct)
    {
        if (nights.Count == 0) return null;

        var real = await db.HostedEventNights
            .Where(n => n.HostedEventId == eventId)
            .Select(n => n.Id)
            .ToListAsync(ct);

        return nights.Any(n => !real.Contains(n.HostedEventNightId))
            ? "One of those nights isn't part of this event."
            : null;
    }

    private static void WriteNights(
        HostedEventBooking booking, IReadOnlyList<HostedEventBookingNightChoice> nights)
    {
        foreach (var choice in nights.GroupBy(n => n.HostedEventNightId).Select(g => g.Last()))
        {
            booking.Nights.Add(new HostedEventBookingNight
            {
                Id = Guid.NewGuid(),
                HostedEventBookingId = booking.Id,
                HostedEventNightId = choice.HostedEventNightId,
                PlaceRoomId = choice.PlaceRoomId,
                DateCreated = DateTime.UtcNow,
            });
        }
    }

    private static void WriteGuests(
        HostedEventBooking booking, IReadOnlyList<HostedEventBookingGuestInput> guests)
    {
        var order = 0;
        foreach (var guest in guests)
        {
            var name = Trimmed(guest.DisplayName);
            if (name is null) continue;

            booking.Guests.Add(new HostedEventBookingGuest
            {
                Id = Guid.NewGuid(),
                HostedEventBookingId = booking.Id,
                DisplayName = name,
                AppUserId = guest.AppUserId,
                DietaryNotes = Trimmed(guest.DietaryNotes),
                SortOrder = order++,
                DateCreated = DateTime.UtcNow,
            });
        }
    }

    private static async Task ReleaseUmbrellaAsync(
        BenDataContext db, HostedEventBooking booking, CancellationToken ct)
    {
        if (booking.UmbrellaAttendeeId is not { } id) return;

        var attendee = await db.OrgCalendarEventAttendees.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attendee is not null) db.OrgCalendarEventAttendees.Remove(attendee);
        booking.UmbrellaAttendeeId = null;
    }

    private static IQueryable<HostedEventBooking> MineQuery(BenDataContext db, Guid userId)
        => db.HostedEventBookings
            .AsNoTracking()
            .Include(b => b.HostedEvent).ThenInclude(e => e.Organization)
            .Include(b => b.HostedEvent).ThenInclude(e => e.Place)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventNight)
            .Include(b => b.Nights).ThenInclude(n => n.PlaceRoom)
            .Include(b => b.Guests)
            .Where(b => b.LeadAppUserId == userId
                     && b.Status != HostedEventBookingStatus.Cancelled);

    private async Task<MyHostedEventBookingRecord> ReloadAsync(
        BenDataContext db, Guid userId, Guid bookingId, CancellationToken ct)
        => ToMine(await MineQuery(db, userId).FirstAsync(b => b.Id == bookingId, ct));

    /// <summary>
    /// What a guest is shown about their own booking.
    /// </summary>
    /// <remarks>
    /// Deliberately not the host's record: it carries no other party's details, no decision-maker's
    /// name and no dietary note but their own party's, because a guest's screen is not a window
    /// into the venue's book.
    /// </remarks>
    private static MyHostedEventBookingRecord ToMine(HostedEventBooking b)
        => new(
            b.Id,
            b.HostedEventId,
            b.HostedEvent?.Name ?? "An event",
            b.HostedEvent?.UrlName,
            b.HostedEvent?.Organization?.Name,
            b.HostedEvent?.Organization?.UrlName,
            b.HostedEvent?.Place?.Name,
            b.HostedEvent?.StartsOn ?? default,
            b.HostedEvent?.EndsOn ?? default,
            b.PartySize,
            b.Kind,
            b.Status,
            b.DecisionNote,
            b.GuestAcknowledgedUtc,
            b.CancellationRequestedUtc,
            b.Note,
            b.Nights
                .OrderBy(n => n.HostedEventNight.Date)
                .Select(n => new HostedEventBookingNightRecord(
                    n.HostedEventNightId, n.HostedEventNight.Date,
                    n.PlaceRoomId, n.PlaceRoom.Name))
                .ToList(),
            b.Guests
                .OrderBy(g => g.SortOrder)
                .Select(g => new HostedEventBookingGuestRecord(
                    g.Id, g.DisplayName, g.AppUserId, g.DietaryNotes, g.SortOrder))
                .ToList());

    private static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;
}
