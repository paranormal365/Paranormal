using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The door, on the night (item 235 phase 7).
/// </summary>
/// <remarks>
/// <para><b>Everything here is per night.</b> Arrival used to be one stamp on a pass, so a
/// three-night weekend could record that a party turned up — once — and never which nights. The
/// Saturday door could not tell whether the people in front of it had been in on the Friday, and
/// nobody could answer "who was here on the Saturday" afterwards.</para>
///
/// <para><b>It takes the door's own permission</b>, which a weekend steward can have without being
/// trusted with the board. That is the whole point of the staff table: a helper with a phone lets
/// people in and never sees an address.</para>
///
/// <para><b>Dietary FLAGS, for tonight only.</b> Somebody letting people in needs to know that the
/// party coming through cannot eat nuts; they do not need the guest's address, their phone number,
/// or what they wrote last year. The kitchen's sheet is a different screen with a different
/// permission.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/door")]
public sealed class HostedEventDoorController : OrgCmsControllerBase
{
    private readonly Services.Access.HostedEventAccess _access;

    public HostedEventDoorController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access)
        : base(dbFactory, mapper, security) { _access = access; }

    /// <summary>
    /// Everybody expected on one night, and who is already in.
    /// </summary>
    /// <remarks>
    /// The night defaults to <b>today at the venue</b> rather than today here: a door in Nashville
    /// opened from a laptop in London must not offer yesterday's list, and the first thing a
    /// steward does is not choose a date.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<HostedEventDoorRecord>> Get(
        Guid orgId, Guid eventId, [FromQuery] Guid? night, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanRunTheDoorAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();
        if (ev.Nights.Count == 0)
            return BadRequest("This event has no dates yet, so there is no door to run.");

        var chosen = Tonight(ev, night);
        if (chosen is null) return BadRequest("That isn't a night of this event.");

        return Ok(await DoorAsync(db, ev, chosen, ct));
    }

    /// <summary>
    /// Marks a party in.
    /// </summary>
    /// <remarks>
    /// <para><b>Twice is not an error.</b> A door that turned away the same party walking back in
    /// from the car park would be worse than one that keeps the first arrival and says nothing.
    /// The row is unique on (booking, night) and the second call updates the head count rather
    /// than making a second arrival.</para>
    ///
    /// <para><b>By name, because this is the button</b> — a scan writes the same row through
    /// <c>door/scan</c> and records that it was scanned. The difference is kept: a code in
    /// somebody's hand and a steward's judgement in a dark room are different levels of certainty
    /// about who came in.</para>
    /// </remarks>
    [HttpPost("arrive")]
    public Task<ActionResult<HostedEventDoorRecord>> Arrive(
        Guid orgId, Guid eventId, [FromBody] HostedEventDoorMoveRequest request, CancellationToken ct)
        => MoveAsync(orgId, eventId, request, Move.Arrive, ct);

    /// <summary>Marks a party as having left. Rare, and never required.</summary>
    [HttpPost("leave")]
    public Task<ActionResult<HostedEventDoorRecord>> Leave(
        Guid orgId, Guid eventId, [FromBody] HostedEventDoorMoveRequest request, CancellationToken ct)
        => MoveAsync(orgId, eventId, request, Move.Leave, ct);

    /// <summary>
    /// Takes an arrival back, because the wrong row was pressed.
    /// </summary>
    /// <remarks>
    /// A door is one thumb on a phone in the dark; the wrong name gets tapped. Without an undo the
    /// only remedy is a count that is wrong all evening and a guest marked as here who is not.
    /// </remarks>
    [HttpPost("undo")]
    public Task<ActionResult<HostedEventDoorRecord>> Undo(
        Guid orgId, Guid eventId, [FromBody] HostedEventDoorMoveRequest request, CancellationToken ct)
        => MoveAsync(orgId, eventId, request, Move.Undo, ct);

    /// <summary>
    /// Writes down somebody who turned up without a booking (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// <para><b>Ben, 2026-09-13:</b> <i>"availability count for walk ups to event and on-sight
    /// sign ups."</i> Somebody hears about it in the pub, arrives on the Saturday, hands over
    /// cash and walks in. All of that is normal and none of it is a booking.</para>
    ///
    /// <para><b>No account is invented for them.</b> The site's settled answer to an organizer
    /// signing up a stranger is to send a link rather than to create an account from an address
    /// nobody verified — and a link is no use to somebody in a doorway now. So the door records
    /// what happened, a head count with a name when there is one, and the guest door is still
    /// there for anybody who wants a real booking afterwards.</para>
    ///
    /// <para><b>It is refused when the house is full</b>, which is the other half of what the
    /// number at the top of the screen is for: a steward who is told there are two places left
    /// and lets in four has been let down by the screen, not by their arithmetic.</para>
    /// </remarks>
    [HttpPost("walk-up")]
    public async Task<ActionResult<HostedEventDoorRecord>> WalkUp(
        Guid orgId, Guid eventId, [FromBody] HostedEventWalkUpRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanRunTheDoorAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        var night = ev.Nights.FirstOrDefault(n => n.Id == request.HostedEventNightId);
        if (night is null) return BadRequest("That isn't a night of this event.");

        var people = Math.Clamp(request.People, 1, EventCapacity.MaxPartySize);

        var door = await DoorAsync(db, ev, night, ct);
        if (door.PlacesLeft is { } left && people > left)
            return Conflict(left <= 0
                ? "There is no room left tonight."
                : $"There {(left == 1 ? "is" : "are")} only {left} "
                  + $"{(left == 1 ? "place" : "places")} left tonight.");

        var now = DateTime.UtcNow;
        db.HostedEventWalkUps.Add(new HostedEventWalkUp
        {
            Id = Guid.NewGuid(),
            HostedEventNightId = night.Id,
            People = people,
            Name = Trimmed(request.Name),
            Note = Trimmed(request.Note),
            ArrivedUtc = now,
            RecordedByAppUserId = userId.Value,
            DateCreated = now,
            CreatedByAppUserId = userId.Value,
        });

        await db.SaveChangesAsync(ct);

        return Ok(await DoorAsync(db, ev, night, ct));
    }

    /// <summary>Takes a walk-up back, because a doorway is where the wrong button gets pressed.</summary>
    [HttpDelete("walk-up/{walkUpId:guid}")]
    public async Task<ActionResult<HostedEventDoorRecord>> UndoWalkUp(
        Guid orgId, Guid eventId, Guid walkUpId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanRunTheDoorAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        var walkUp = await db.HostedEventWalkUps
            .FirstOrDefaultAsync(w => w.Id == walkUpId, ct);
        if (walkUp is null) return NotFound();

        var night = ev.Nights.FirstOrDefault(n => n.Id == walkUp.HostedEventNightId);
        if (night is null) return NotFound();

        db.HostedEventWalkUps.Remove(walkUp);
        await db.SaveChangesAsync(ct);

        return Ok(await DoorAsync(db, ev, night, ct));
    }

    private enum Move { Arrive, Leave, Undo }

    private async Task<ActionResult<HostedEventDoorRecord>> MoveAsync(
        Guid orgId, Guid eventId, HostedEventDoorMoveRequest request, Move what, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await _access.CanRunTheDoorAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await db.HostedEvents
            .Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        var night = ev.Nights.FirstOrDefault(n => n.Id == request.HostedEventNightId);
        if (night is null) return BadRequest("That isn't a night of this event.");

        var booking = await db.HostedEventBookings
            .Include(b => b.Nights)
            .FirstOrDefaultAsync(b => b.Id == request.HostedEventBookingId
                                   && b.HostedEventId == eventId, ct);
        if (booking is null) return NotFound();

        if (booking.Status != HostedEventBookingStatus.Confirmed)
            return Conflict("Only a confirmed booking can come in. Confirm it first, or let them "
                          + "in and sort it out afterwards.");

        var now = DateTime.UtcNow;
        var arrival = await db.HostedEventCheckIns.FirstOrDefaultAsync(
            c => c.HostedEventBookingId == booking.Id
              && c.HostedEventNightId == night.Id, ct);

        switch (what)
        {
            case Move.Arrive when arrival is null:
                db.HostedEventCheckIns.Add(new HostedEventCheckIn
                {
                    Id = Guid.NewGuid(),
                    HostedEventBookingId = booking.Id,
                    HostedEventNightId = night.Id,
                    ArrivedUtc = DoorClock.ArrivedAt(request.ArrivedUtc, now, night.Date),
                    People = People(request, booking),
                    Method = HostedEventCheckInMethod.ByName,
                    RecordedByAppUserId = userId.Value,
                    DateCreated = now,
                    CreatedByAppUserId = userId.Value,
                });
                break;

            case Move.Arrive:
                // Already in. Keep the first arrival — it is the true one — and take the chance to
                // correct the head count, which is what a second press usually means. An arrival the
                // phone kept offline can be earlier than the one recorded since; the earlier one wins.
                arrival.People = People(request, booking) ?? arrival.People;
                if (DoorClock.ArrivedAt(request.ArrivedUtc, now, night.Date) is var told && told < arrival.ArrivedUtc)
                    arrival.ArrivedUtc = told;
                arrival.LeftUtc = null;
                arrival.DateUpdated = now;
                arrival.UpdatedByAppUserId = userId.Value;
                break;

            case Move.Leave when arrival is not null:
                arrival.LeftUtc = now;
                arrival.DateUpdated = now;
                arrival.UpdatedByAppUserId = userId.Value;
                break;

            case Move.Undo when arrival is not null:
                db.HostedEventCheckIns.Remove(arrival);
                break;
        }

        await db.SaveChangesAsync(ct);

        return Ok(await DoorAsync(db, ev, night, ct));
    }

    // ── the work ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The night to run: the one asked for, or today at the venue, or the first one left.
    /// </summary>
    /// <remarks>
    /// <b>Today at the VENUE.</b> A door in Nashville opened from a laptop in London must not
    /// offer yesterday's list, and the whole point of defaulting is that a steward should not have
    /// to choose a date before they can let anybody in. Failing that — the day before, or the week
    /// after — the next night that has not happened, and failing that the last one.
    /// </remarks>
    private static HostedEventNight? Tonight(HostedEvent ev, Guid? asked)
    {
        var nights = ev.Nights.OrderBy(n => n.Date).ToList();

        if (asked is { } id) return nights.FirstOrDefault(n => n.Id == id);

        var zone = HostedEventCalendarSync.ZoneOf(ev.TimeZoneId);
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone).Date;

        return nights.FirstOrDefault(n => n.Date.Date == today)
            ?? nights.FirstOrDefault(n => n.Date.Date >= today)
            ?? nights.LastOrDefault();
    }

    private static int? People(HostedEventDoorMoveRequest request, HostedEventBooking booking)
        => request.People is { } many
            ? Math.Clamp(many, 1, EventCapacity.ClampPartySize(booking.PartySize))
            : null;

    /// <summary>Everybody expected tonight, with what the door may know about them.</summary>
    private static async Task<HostedEventDoorRecord> DoorAsync(
        BenDataContext db, HostedEvent ev, HostedEventNight night, CancellationToken ct)
    {
        // CONFIRMED ONLY. A party the venue has not agreed to is not expected at a door, and a
        // steward reading a list that included them would let in somebody nobody has a bed for.
        var bookings = await db.HostedEventBookings.AsNoTracking()
            .Include(b => b.LeadAppUser)
            .Include(b => b.Guests)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
            .Where(b => b.HostedEventId == ev.Id
                     && b.Status == HostedEventBookingStatus.Confirmed)
            .ToListAsync(ct);

        // Here tonight: holding a place on this night, or holding none at all — a pass for the
        // whole event is a pass for every night of it.
        var here = bookings
            .Where(b => b.Nights.All(n => n.ReleasedUtc is not null)
                     || b.Nights.Any(n => n.HostedEventNightId == night.Id && n.ReleasedUtc is null))
            .ToList();

        var arrivals = await db.HostedEventCheckIns.AsNoTracking()
            .Where(c => c.HostedEventNightId == night.Id)
            .ToListAsync(ct);

        // The colours this venue uses, if any. One read for the whole list rather than one per
        // party — a door with two hundred names on it is the case that matters.
        var bands = await db.HostedEventBands.AsNoTracking()
            .Where(b => b.HostedEventId == ev.Id)
            .OrderBy(b => b.SortOrder)
            .ToListAsync(ct);

        var nightsOnTheEvent = ev.Nights.Count;

        var passes = await db.HostedEventPasses.AsNoTracking()
            .Where(p => p.HostedEventBooking.HostedEventId == ev.Id && p.RevokedUtc == null)
            .Select(p => new { p.HostedEventBookingId, p.Token })
            .ToListAsync(ct);

        var parties = here
            .Select(b =>
            {
                var arrival = arrivals.FirstOrDefault(c => c.HostedEventBookingId == b.Id);
                var token = passes.FirstOrDefault(p => p.HostedEventBookingId == b.Id)?.Token;

                return new HostedEventDoorPartyRecord(
                    b.Id,
                    b.LeadAppUser?.DisplayName ?? "Somebody",
                    EventCapacity.ClampPartySize(b.PartySize),
                    b.Kind,
                    Where(b, night.Id),
                    token is { Length: > 6 } ? token[^6..].ToUpperInvariant() : token?.ToUpperInvariant(),
                    arrival?.ArrivedUtc,
                    arrival?.LeftUtc,
                    arrival?.People,
                    // FLAGS AND NOT NOTES: the words a guest wrote about what they cannot eat, with
                    // nobody's name against them. Whose allergy it is belongs to the kitchen's
                    // sheet, behind a different permission.
                    [.. b.Guests
                        .Select(g => g.DietaryNotes?.Trim())
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => n!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)],
                    Band: EventBands.For(b, bands, nightsOnTheEvent) is { } band
                        ? new HostedEventBandRecord(
                            band.Id, band.Colour, band.Meaning, band.Hex, band.Rule, band.SortOrder)
                        : null);
            })
            .OrderBy(p => p.LeadName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var walkUps = await db.HostedEventWalkUps.AsNoTracking()
            .Where(w => w.HostedEventNightId == night.Id)
            .OrderBy(w => w.ArrivedUtc)
            .Select(w => new HostedEventWalkUpRecord(w.Id, w.People, w.Name, w.Note, w.ArrivedUtc))
            .ToListAsync(ct);

        // What is actually in the building: the head count where somebody gave one, the whole
        // party otherwise, nobody who has left, and everybody who simply turned up.
        var inTheBuilding = parties
            .Where(p => p.ArrivedUtc is not null && p.LeftUtc is null)
            .Sum(p => p.PeopleIn ?? p.PartySize)
            + walkUps.Sum(w => w.People);

        var (left, sentence) = await PlacesLeftAsync(db, ev, night, parties, walkUps, ct);

        return new HostedEventDoorRecord(
            ev.Id,
            ev.Name,
            night.Id,
            night.Date,
            [.. ev.Nights.OrderBy(n => n.Date).Select(n => HostedEventController.ToNight(n, ev))],
            parties,
            PeopleExpected: parties.Sum(p => p.PartySize),
            PeopleIn: inTheBuilding,
            WalkUps: walkUps,
            PlacesLeft: left,
            PlacesLeftSentence: sentence);
    }

    /// <summary>
    /// How many more people could be let in tonight, and how to say it.
    /// </summary>
    /// <remarks>
    /// <para><b>Whichever ceiling this event actually has.</b> A theatre's is the seats on its
    /// plan; a hotel weekend's is usually nothing at all, because the rooms are the ceiling and
    /// they are already allocated; an event selling day passes has the number the host set. Where
    /// there is no ceiling the honest answer is that there is none, which is why this is nullable
    /// rather than a large number pretending to be a limit.</para>
    ///
    /// <para><b>It counts places, not bookings</b> — a party of four takes four — and everybody
    /// already through the door counts whether they were booked or not.</para>
    /// </remarks>
    private static async Task<(int? Left, string? Sentence)> PlacesLeftAsync(
        BenDataContext db, HostedEvent ev, HostedEventNight night,
        IReadOnlyList<HostedEventDoorPartyRecord> parties,
        IReadOnlyList<HostedEventWalkUpRecord> walkUps,
        CancellationToken ct)
    {
        var taken = parties.Sum(p => p.PartySize) + walkUps.Sum(w => w.People);

        // A seating plan is the clearest ceiling there is: one seat, one person.
        var seats = await db.HostedEventLayoutUnits.AsNoTracking()
            .Where(u => u.HostedEventId == ev.Id && u.PlaceRoomId == null)
            .CountAsync(ct);

        var ceiling = seats > 0 ? seats : ev.DayPassCapacity;
        if (ceiling is not { } cap) return (null, null);

        var left = Math.Max(0, cap - taken);

        return (left, left switch
        {
            0 => "Full tonight — no room for anybody else.",
            1 => "Room for one more tonight.",
            _ => $"Room for {left} more tonight.",
        });
    }

    private static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;

    /// <summary>Where this party is on this night, in the words the door needs.</summary>
    private static string? Where(HostedEventBooking booking, Guid nightId)
    {
        var tonight = booking.Nights.FirstOrDefault(
            n => n.HostedEventNightId == nightId && n.ReleasedUtc is null);

        return tonight is null
            ? booking.Kind == HostedEventBookingKind.DayPass ? "For the day" : null
            : EventCapacity.NameOf(tonight, booking.Kind);
    }
}
