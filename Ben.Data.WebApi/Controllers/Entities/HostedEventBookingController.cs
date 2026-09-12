using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Who is coming to a hosted event, where they are sleeping, and what the venue said (item 235
/// phase 2).
/// </summary>
/// <remarks>
/// <para><b>The site never takes the money</b> (DECISION 1). Confirming a booking means the venue
/// and the guest have settled payment between them, exactly as approving a walk's seat does.
/// Nothing here is a payment record.</para>
///
/// <para><b>Confirmation is the moment everything else notices.</b> It writes the umbrella
/// calendar attendee row with <c>Seats</c> equal to the party size, so the public list, the
/// twenty-four-hour reminder, the calendar file, evidence submission and the phone already in
/// people's pockets all go on meaning <i>has a place</i> — none of which had to learn what a
/// booking is.</para>
///
/// <para><b>Every refusal names the room, the night and the number left.</b> A host told "full"
/// cannot tell whether to offer a smaller room, split the party, or say no, and this screen exists
/// to be decided from.</para>
///
/// <para>Reading takes membership; deciding takes the settings key, the same permission that opens
/// billing — and it moves to the <c>Events</c> area in phase 5 when per-event staff arrive.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/bookings")]
public sealed class HostedEventBookingController : OrgCmsControllerBase
{
    private readonly HostedEventCalendarSync _sync;

    public HostedEventBookingController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security,
        HostedEventCalendarSync sync)
        : base(dbFactory, mapper, security)
    { _sync = sync; }

    // ── reading ──────────────────────────────────────────────────────────────

    /// <summary>The whole weekend: rooms offered, how full each is per night, and every booking.</summary>
    [HttpGet]
    public async Task<ActionResult<HostedEventBookingBoardRecord>> GetBoard(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await IsMemberAsync(db, orgId, userId.Value, ct)) return Forbid();

        var ev = await LoadEventAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        return Ok(await BoardAsync(db, ev, ct));
    }

    // ── deciding ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Agrees to a booking and puts the party in rooms.
    /// </summary>
    /// <remarks>
    /// The rooms sent here are where they ACTUALLY sleep, which need not be what they asked for —
    /// moving a party of three out of a double and into the suite is the commonest thing a host
    /// does, and making them turn it down and ask again would be absurd.
    /// </remarks>
    [HttpPost("{bookingId:guid}/confirm")]
    public async Task<ActionResult<HostedEventBookingRecord>> Confirm(
        Guid orgId, Guid eventId, Guid bookingId,
        [FromBody] ConfirmHostedEventBookingRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, ct)) return Forbid();

        var ev = await LoadEventAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        var booking = await LoadBookingAsync(db, eventId, bookingId, ct);
        if (booking is null) return NotFound();
        if (booking.Status == HostedEventBookingStatus.Confirmed)
            return BadRequest("That booking is already confirmed.");

        var nights = request.Nights ?? [];
        if (booking.Kind == HostedEventBookingKind.Overnight && nights.Count == 0)
            return BadRequest("Say which room they are in on each night, or turn the booking down.");

        var all = await AllBookingsAsync(db, eventId, ct);

        if (await WhyItCannotBeConfirmedAsync(db, ev, booking, nights, all, ct) is { } refusal)
            return BadRequest(refusal);

        ReplaceNights(db, booking, nights);
        booking.Status = HostedEventBookingStatus.Confirmed;
        booking.DecidedUtc = DateTime.UtcNow;
        booking.DecidedByAppUserId = userId.Value;
        booking.DecisionNote = Trimmed(request.DecisionNote);
        // A decision the guest has not seen yet, whatever they had seen before.
        booking.GuestAcknowledgedUtc = null;
        Touch(booking, userId.Value);

        await AttachUmbrellaAttendeeAsync(db, ev, booking, userId.Value, ct);
        await db.SaveChangesAsync(ct);

        return Ok(await OneAsync(db, eventId, booking.Id, ct));
    }

    /// <summary>Says no. The guest is told, because a guest who is not coming must know.</summary>
    [HttpPost("{bookingId:guid}/turn-down")]
    public Task<ActionResult<HostedEventBookingRecord>> TurnDown(
        Guid orgId, Guid eventId, Guid bookingId,
        [FromBody] DecideHostedEventBookingRequest request, CancellationToken ct)
        => DecideAsync(orgId, eventId, bookingId, HostedEventBookingStatus.TurnedDown,
                       request.DecisionNote, ct);

    /// <summary>
    /// Releases a booking, whether it was confirmed or still waiting.
    /// </summary>
    /// <remarks>
    /// Frees the room-nights and removes the umbrella attendee row, so every count that already
    /// exists stops including them. The booking itself stays: the venue catered against it.
    /// </remarks>
    [HttpPost("{bookingId:guid}/cancel")]
    public Task<ActionResult<HostedEventBookingRecord>> Cancel(
        Guid orgId, Guid eventId, Guid bookingId,
        [FromBody] DecideHostedEventBookingRequest request, CancellationToken ct)
        => DecideAsync(orgId, eventId, bookingId, HostedEventBookingStatus.Cancelled,
                       request.DecisionNote, ct);

    // ── changing one that exists ─────────────────────────────────────────────

    /// <summary>
    /// Changes a booking: the party size, the rooms, the guests, the note.
    /// </summary>
    /// <remarks>
    /// <para>Ben asked for editable reservations, and a real weekend needs them: somebody drops out
    /// on the Thursday, a party moves rooms, a name was spelled wrong.</para>
    ///
    /// <para><b>An edit re-checks capacity</b>, so it can never do what a booking could not — but
    /// it excludes the booking's own beds, so a party moving from the Blue Room to the Suite is not
    /// refused by the beds it is about to leave.</para>
    /// </remarks>
    [HttpPut("{bookingId:guid}")]
    public async Task<ActionResult<HostedEventBookingRecord>> Edit(
        Guid orgId, Guid eventId, Guid bookingId,
        [FromBody] EditHostedEventBookingRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, ct)) return Forbid();

        var ev = await LoadEventAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        var booking = await LoadBookingAsync(db, eventId, bookingId, ct);
        if (booking is null) return NotFound();

        if (request.PartySize is int size) booking.PartySize = EventCapacity.ClampPartySize(size);
        if (request.Note is not null) booking.Note = Trimmed(request.Note);

        var nights = request.Nights ?? booking.Nights
            .Select(n => new HostedEventBookingNightChoice(n.HostedEventNightId, n.PlaceRoomId))
            .ToList();

        // Only a booking that HOLDS beds can be over capacity, so a request is edited freely and
        // meets the rule when somebody confirms it.
        if (EventCapacity.Holds(booking.Status))
        {
            var all = await AllBookingsAsync(db, eventId, ct);
            if (await WhyItCannotBeConfirmedAsync(db, ev, booking, nights, all, ct) is { } refusal)
                return BadRequest(refusal);
        }

        if (request.Nights is not null) ReplaceNights(db, booking, nights);
        if (request.Guests is not null) ReplaceGuests(db, booking, request.Guests);

        Touch(booking, userId.Value);

        // The umbrella row carries the party size, so an edit that changes it has to say so or
        // every count on the site keeps the old number.
        if (EventCapacity.Holds(booking.Status))
            await AttachUmbrellaAttendeeAsync(db, ev, booking, userId.Value, ct);

        await db.SaveChangesAsync(ct);
        return Ok(await OneAsync(db, eventId, booking.Id, ct));
    }

    // ── the venue booking somebody in ────────────────────────────────────────

    /// <summary>
    /// Creates a booking for somebody who asked by phone, by email or at the door.
    /// </summary>
    /// <remarks>
    /// Takes an existing account, and nothing else for now: creating an account for a person who
    /// never asked for one goes through the guest-invite door the walk-up sign-up already owns, and
    /// wiring that in is phase 2.2's email half rather than a second copy of it here.
    /// </remarks>
    [HttpPost("on-behalf")]
    public async Task<ActionResult<HostedEventBookingRecord>> CreateOnBehalf(
        Guid orgId, Guid eventId,
        [FromBody] CreateHostedEventBookingOnBehalfRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, ct)) return Forbid();

        var ev = await LoadEventAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        if (request.LeadAppUserId is not { } leadId)
            return BadRequest("Choose who this booking is for.");
        if (!await db.AppUsers.AnyAsync(u => u.Id == leadId, ct))
            return BadRequest("That person no longer has an account here.");

        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(),
            HostedEventId = eventId,
            LeadAppUserId = leadId,
            PartySize = EventCapacity.ClampPartySize(request.PartySize),
            Kind = request.Kind,
            Status = HostedEventBookingStatus.Requested,
            Note = Trimmed(request.Note),
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value,
        };
        db.HostedEventBookings.Add(booking);
        ReplaceNights(db, booking, request.Nights ?? []);
        ReplaceGuests(db, booking, request.Guests ?? []);

        if (request.ConfirmImmediately)
        {
            var nights = request.Nights ?? [];
            if (booking.Kind == HostedEventBookingKind.Overnight && nights.Count == 0)
                return BadRequest("Say which room they are in on each night.");

            var all = await AllBookingsAsync(db, eventId, ct);
            if (await WhyItCannotBeConfirmedAsync(db, ev, booking, nights, all, ct) is { } refusal)
                return BadRequest(refusal);

            booking.Status = HostedEventBookingStatus.Confirmed;
            booking.DecidedUtc = DateTime.UtcNow;
            booking.DecidedByAppUserId = userId.Value;
            await AttachUmbrellaAttendeeAsync(db, ev, booking, userId.Value, ct);
        }

        await db.SaveChangesAsync(ct);
        return Ok(await OneAsync(db, eventId, booking.Id, ct));
    }

    // ── the work ─────────────────────────────────────────────────────────────

    private async Task<ActionResult<HostedEventBookingRecord>> DecideAsync(
        Guid orgId, Guid eventId, Guid bookingId,
        HostedEventBookingStatus status, string? note, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, ct)) return Forbid();

        var booking = await LoadBookingAsync(db, eventId, bookingId, ct);
        if (booking is null) return NotFound();

        booking.Status = status;
        booking.DecidedUtc = DateTime.UtcNow;
        booking.DecidedByAppUserId = userId.Value;
        booking.DecisionNote = Trimmed(note);
        booking.GuestAcknowledgedUtc = null;
        Touch(booking, userId.Value);

        // Whatever they held, they hold no longer. Removing the umbrella row is what makes every
        // existing count stop including them, which is the whole reason that row exists.
        await ReleaseUmbrellaAttendeeAsync(db, booking, ct);

        await db.SaveChangesAsync(ct);
        return Ok(await OneAsync(db, eventId, booking.Id, ct));
    }

    /// <summary>
    /// Why this party cannot be confirmed into these rooms, or null when they can.
    /// </summary>
    /// <remarks>
    /// Checked room by room and night by night rather than in total: a party that fits in the
    /// building and not in the room they are being put in is exactly the mistake worth catching.
    /// </remarks>
    private async Task<string?> WhyItCannotBeConfirmedAsync(
        BenDataContext db,
        HostedEvent ev,
        HostedEventBooking booking,
        IReadOnlyList<HostedEventBookingNightChoice> nights,
        IReadOnlyList<HostedEventBooking> all,
        CancellationToken ct)
    {
        var party = EventCapacity.ClampPartySize(booking.PartySize);

        if (booking.Kind == HostedEventBookingKind.DayPass)
        {
            var taken = EventCapacity.DayPassesTaken(all, excludingBookingId: booking.Id);
            return EventCapacity.WhyTheseDayPassesCannotBeGiven(ev.DayPassCapacity, taken, party);
        }

        var offered = await db.HostedEventRooms
            .Include(r => r.PlaceRoom)
            .Where(r => r.HostedEventId == ev.Id)
            .ToListAsync(ct);
        var eventNights = await db.HostedEventNights
            .Where(n => n.HostedEventId == ev.Id)
            .ToDictionaryAsync(n => n.Id, ct);

        foreach (var choice in nights)
        {
            if (!eventNights.TryGetValue(choice.HostedEventNightId, out var night))
                return "One of those nights is not part of this event any more.";

            var room = offered.FirstOrDefault(r => r.PlaceRoomId == choice.PlaceRoomId);
            if (room is null)
                return "That room is not one this event is offering. Add it to the event first.";

            var taken = EventCapacity.PeopleIn(all, choice.HostedEventNightId, choice.PlaceRoomId,
                                               excludingBookingId: booking.Id);
            if (EventCapacity.WhyThisRoomCannotTakeThem(
                    room.PlaceRoom.Name, night.Date, EventCapacity.CapacityOf(room), taken, party)
                is { } refusal)
                return refusal;
        }

        return null;
    }

    /// <summary>
    /// Writes, or updates, the umbrella calendar attendee row this booking's place is counted by.
    /// </summary>
    /// <remarks>
    /// <c>RsvpStatus.Accepted</c> alongside <c>TourSeatStatus.Reserved</c>, exactly as a walk's
    /// approved seat does, because that is the pair every existing count already understands.
    /// </remarks>
    private async Task AttachUmbrellaAttendeeAsync(
        BenDataContext db, HostedEvent ev, HostedEventBooking booking, Guid actorId,
        CancellationToken ct)
    {
        var umbrella = await _sync.SyncAsync(db, ev, actorId, ct);

        var attendee = booking.UmbrellaAttendeeId is { } id
            ? await db.OrgCalendarEventAttendees.FirstOrDefaultAsync(a => a.Id == id, ct)
            : await db.OrgCalendarEventAttendees.FirstOrDefaultAsync(
                a => a.OrgCalendarEventId == umbrella.Id && a.AppUserId == booking.LeadAppUserId, ct);

        if (attendee is null)
        {
            attendee = new OrgCalendarEventAttendee
            {
                Id = Guid.NewGuid(),
                OrgCalendarEventId = umbrella.Id,
                AppUserId = booking.LeadAppUserId,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = actorId,
            };
            db.OrgCalendarEventAttendees.Add(attendee);
        }

        attendee.RsvpStatus = RsvpStatus.Accepted;
        attendee.SeatStatus = TourSeatStatus.Reserved;
        attendee.Seats = EventCapacity.ClampPartySize(booking.PartySize);
        attendee.DateRsvp = DateTime.UtcNow;
        attendee.SeatDecidedUtc = DateTime.UtcNow;
        attendee.SeatDecidedByAppUserId = actorId;

        booking.UmbrellaAttendeeId = attendee.Id;
    }

    private async Task ReleaseUmbrellaAttendeeAsync(
        BenDataContext db, HostedEventBooking booking, CancellationToken ct)
    {
        if (booking.UmbrellaAttendeeId is not { } id) return;

        var attendee = await db.OrgCalendarEventAttendees.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (attendee is not null) db.OrgCalendarEventAttendees.Remove(attendee);
        booking.UmbrellaAttendeeId = null;
    }

    private static void ReplaceNights(
        BenDataContext db, HostedEventBooking booking,
        IReadOnlyList<HostedEventBookingNightChoice> nights)
    {
        if (booking.Nights.Count > 0) db.HostedEventBookingNights.RemoveRange(booking.Nights);
        booking.Nights.Clear();

        // Distinct by night: a party cannot hold two rooms on one night, and the unique index
        // would otherwise refuse the whole save with an error nobody could act on.
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

    private static void ReplaceGuests(
        BenDataContext db, HostedEventBooking booking,
        IReadOnlyList<HostedEventBookingGuestInput> guests)
    {
        if (booking.Guests.Count > 0) db.HostedEventBookingGuests.RemoveRange(booking.Guests);
        booking.Guests.Clear();

        var order = 0;
        foreach (var guest in guests)
        {
            var name = Trimmed(guest.DisplayName);
            if (name is null) continue;   // a nameless guest is a blank row somebody left behind

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

    // ── loading ──────────────────────────────────────────────────────────────

    private static Task<HostedEvent?> LoadEventAsync(
        BenDataContext db, Guid orgId, Guid eventId, CancellationToken ct)
        => db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);

    private static Task<HostedEventBooking?> LoadBookingAsync(
        BenDataContext db, Guid eventId, Guid bookingId, CancellationToken ct)
        => db.HostedEventBookings
            .Include(b => b.Nights)
            .Include(b => b.Guests)
            .FirstOrDefaultAsync(b => b.Id == bookingId && b.HostedEventId == eventId, ct);

    private static async Task<IReadOnlyList<HostedEventBooking>> AllBookingsAsync(
        BenDataContext db, Guid eventId, CancellationToken ct)
        => await db.HostedEventBookings
            .Include(b => b.Nights)
            .Where(b => b.HostedEventId == eventId)
            .ToListAsync(ct);

    private async Task<HostedEventBookingRecord> OneAsync(
        BenDataContext db, Guid eventId, Guid bookingId, CancellationToken ct)
    {
        var board = await BookingsAsync(db, eventId, ct);
        return board.First(b => b.Id == bookingId);
    }

    private static async Task<IReadOnlyList<HostedEventBookingRecord>> BookingsAsync(
        BenDataContext db, Guid eventId, CancellationToken ct)
    {
        var bookings = await db.HostedEventBookings
            .AsNoTracking()
            .Include(b => b.Nights).ThenInclude(n => n.PlaceRoom)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventNight)
            .Include(b => b.Guests)
            .Include(b => b.LeadAppUser)
            .Include(b => b.DecidedByAppUser)
            .Where(b => b.HostedEventId == eventId)
            .OrderBy(b => b.DateCreated)
            .ToListAsync(ct);

        return bookings.Select(b => new HostedEventBookingRecord(
            b.Id, b.HostedEventId, b.LeadAppUserId,
            b.LeadAppUser?.DisplayName ?? "Somebody",
            b.LeadAppUser?.Email,
            b.PartySize, b.Kind, b.Status,
            b.DecidedUtc, b.DecidedByAppUser?.DisplayName, b.DecisionNote,
            b.GuestAcknowledgedUtc, b.Note,
            b.CancellationRequestedUtc, b.CancellationReason, b.DateCreated,
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
                .ToList()))
            .ToList();
    }

    private async Task<HostedEventBookingBoardRecord> BoardAsync(
        BenDataContext db, HostedEvent ev, CancellationToken ct)
    {
        var offered = await db.HostedEventRooms
            .AsNoTracking()
            .Include(r => r.PlaceRoom)
            .Where(r => r.HostedEventId == ev.Id)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.PlaceRoom.Name)
            .ToListAsync(ct);

        var all = await AllBookingsAsync(db, ev.Id, ct);
        var nights = ev.Nights.OrderBy(n => n.Date).ToList();

        var roomNights = new List<HostedEventRoomNightRecord>();
        foreach (var night in nights)
        {
            foreach (var room in offered)
            {
                roomNights.Add(new HostedEventRoomNightRecord(
                    night.Id, night.Date, room.PlaceRoomId, room.PlaceRoom.Name,
                    EventCapacity.CapacityOf(room),
                    EventCapacity.PeopleIn(all, night.Id, room.PlaceRoomId),
                    AskedFor(all, night.Id, room.PlaceRoomId)));
            }
        }

        return new HostedEventBookingBoardRecord(
            ev.Id,
            EventCapacity.IsOpenForRequests(ev, DateTime.UtcNow),
            ev.BookingsCloseAtUtc,
            ev.DayPassCapacity,
            EventCapacity.DayPassesTaken(all),
            all.Where(b => b.Kind == HostedEventBookingKind.DayPass
                        && b.Status == HostedEventBookingStatus.Requested)
               .Sum(b => Math.Max(1, b.PartySize)),
            offered.Select(r => new HostedEventRoomRecord(
                r.Id, r.PlaceRoomId, r.PlaceRoom.Name, r.PlaceRoom.Floor, r.PlaceRoom.BedNote,
                EventCapacity.CapacityOf(r), r.CapacityOverride, r.Note, r.SortOrder)).ToList(),
            roomNights,
            await BookingsAsync(db, ev.Id, ct));
    }

    /// <summary>
    /// People who have asked for a room on a night and hold nothing.
    /// </summary>
    /// <remarks>
    /// Shown beside what is taken because over-asking is a fact a host needs: it is the difference
    /// between a full house and a popular one, and it is what turns the overflow into a waiting
    /// list rather than a closed door.
    /// </remarks>
    private static int AskedFor(
        IEnumerable<HostedEventBooking> bookings, Guid nightId, Guid placeRoomId)
        => bookings
            .Where(b => b.Status == HostedEventBookingStatus.Requested)
            .Where(b => b.Nights.Any(n => n.HostedEventNightId == nightId
                                       && n.PlaceRoomId == placeRoomId))
            .Sum(b => Math.Max(1, b.PartySize));

    // ── plumbing ─────────────────────────────────────────────────────────────

    private Task<bool> CanDecideAsync(Guid userId, Guid orgId, CancellationToken ct)
        => IsCmsAuthorizedAsync(userId, orgId,
               OrganizationSecurityTable.OrganizationSettings,
               OrganizationSecurityAction.Update, ct);

    private async Task<bool> IsMemberAsync(
        BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
        || await Services.Access.FileAudienceAccess.IsOrgMemberAsync(db, orgId, userId, ct);

    private static void Touch(HostedEventBooking booking, Guid userId)
    {
        booking.DateUpdated = DateTime.UtcNow;
        booking.UpdatedByAppUserId = userId;
    }

    private static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;
}
