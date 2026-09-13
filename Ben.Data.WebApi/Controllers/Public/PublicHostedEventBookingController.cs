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

    private readonly HostedEventCalendarSync _sync;

    public PublicHostedEventBookingController(
        IDbContextFactory<BenDataContext> db, HostedEventCalendarSync sync)
    { _db = db; _sync = sync; }

    // ── what I have asked for ────────────────────────────────────────────────

    /// <summary>
    /// Every hosted event this person has a booking at, coming up or recently past.
    /// </summary>
    /// <remarks>
    /// <b>A released booking for an event that has not happened yet is still shown here</b>, and
    /// only here. Everywhere else on this door a cancelled booking is gone, because "have I got a
    /// place" is answered no; but a list of what somebody has coming up that silently drops the
    /// weekend they were released from is a list that answers a question nobody asked. They kept
    /// the date free. Once the event is over it goes, like everything else.
    /// </remarks>
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<MyHostedEventBookingRecord>>> GetMine(
        CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var today = DateTime.UtcNow.Date;
        var bookings = (await MineQuery(db, userId, includeReleased: true)
            .Where(b => b.Status != HostedEventBookingStatus.Cancelled
                     || b.HostedEvent.EndsOn >= today)
            .OrderBy(b => b.HostedEvent.StartsOn)
            .ToListAsync(ct))
            // ONE ROW PER EVENT (phase 14): the booking that matters — live first, then the newest. Somebody who
            // asked, was released and asked again has one weekend, not three; the phone app's first capture of this
            // list for a much-tested guest came back as 860 rows for a single event.
            .GroupBy(b => b.HostedEventId)
            .Select(g => g
                .OrderByDescending(b => b.Status is HostedEventBookingStatus.Confirmed
                                               or HostedEventBookingStatus.Held
                                               or HostedEventBookingStatus.Requested)
                .ThenByDescending(b => b.DateCreated)
                .First())
            .OrderBy(b => b.HostedEvent.StartsOn)
            .ToList();

        // The programme places and whether a review is open, for the list only (phase 12): "what am I
        // going to" includes the ghost hunt at eleven, and "how was it" belongs beside the weekend it
        // is about.
        var eventIds = bookings.Select(b => b.HostedEventId).Distinct().ToList();
        var signUps = await db.HostedEventSessionSignUps.AsNoTracking()
            .Where(s => s.AppUserId == userId && eventIds.Contains(s.HostedEventSession.HostedEventId))
            .Select(s => new
            {
                s.HostedEventSession.HostedEventId,
                Line = new MySessionLineRecord(
                    s.HostedEventSessionId, s.HostedEventSession.Title, s.HostedEventSession.StartsAtUtc,
                    s.HostedEventSession.EndsAtUtc,
                    s.HostedEventSession.PlaceRoom != null ? s.HostedEventSession.PlaceRoom.Name : s.HostedEventSession.LocationText,
                    s.WaitlistedUtc != null && s.PromotedUtc == null,
                    s.HostedEventSession.CalledOffUtc != null),
            })
            .ToListAsync(ct);
        var reviewed = await db.HostedEventReviews.AsNoTracking()
            .Where(r => r.AppUserId == userId && eventIds.Contains(r.HostedEventId))
            .Select(r => r.HostedEventId)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var list = new List<MyHostedEventBookingRecord>();
        foreach (var booking in bookings)
        {
            var mayReview = booking.Status == HostedEventBookingStatus.Confirmed
                         && await HostedEventReviews.WhyNotAsync(db, booking.HostedEvent, userId, now, ct) is null;

            list.Add(ToMine(booking) with
            {
                Sessions = [.. signUps.Where(s => s.HostedEventId == booking.HostedEventId)
                                      .Select(s => s.Line).OrderBy(l => l.StartsAtUtc)],
                MayReview = mayReview,
                HasReviewed = reviewed.Contains(booking.HostedEventId),
            });
        }

        return Ok(list);
    }

    /// <summary>
    /// What the booking form can fill in for this guest: the name and phone their account already
    /// has (slice 11d).
    /// </summary>
    /// <remarks>
    /// Read, never written: the phone typed into a booking stays with that booking, so the form asks
    /// again next time for anybody whose account has none.
    /// </remarks>
    [HttpGet("my-contact")]
    public async Task<ActionResult<BookingContactRecord>> GetMyContact(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var me = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new
            {
                u.FirstName, u.LastName, u.Email, u.PhoneNumber,
                Listed = u.UserPhones.OrderByDescending(p => p.IsPrimary).Select(p => p.PhoneNumber).FirstOrDefault(),
            })
            .FirstOrDefaultAsync(ct);
        if (me is null) return NotFound();

        return Ok(new BookingContactRecord(me.FirstName, me.LastName, me.Listed ?? me.PhoneNumber, me.Email));
    }

    /// <summary>This person's booking at one event, or 404 when they have none.</summary>
    [HttpGet("{eventId:guid}/my-booking")]
    public async Task<ActionResult<MyHostedEventBookingRecord>> GetMyBooking(
        Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var booking = await Newest(MineQuery(db, userId).Where(b => b.HostedEventId == eventId))
            .FirstOrDefaultAsync(ct);

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

    /// <summary>
    /// The guest's own pass, with enough words on it to get in without a scanner.
    /// </summary>
    /// <remarks>
    /// <para>The human summary travels with the code deliberately. A door with a flat battery, or
    /// a camera that will not focus in the dark, still has to be able to see who this is and how
    /// many they are — a pass only a machine can read is a pass that fails on the one evening it
    /// matters.</para>
    ///
    /// <para><b>A withdrawn pass is still shown</b>, with the reason on it. The first version
    /// answered "the venue hasn't issued your pass yet" the moment the only pass was revoked, which
    /// told a guest whose booking had just been turned down that they were waiting for something.
    /// The latest pass comes back whatever its state: the live one when there is one, otherwise the
    /// most recently withdrawn, so a guest can show it to somebody who can tell them why. A revoked
    /// pass that was replaced is simply history — the replacement is the pass.</para>
    ///
    /// <para>Only a booking that has never had a pass at all is told what to wait for, rather than
    /// being shown an empty box.</para>
    /// </remarks>
    [HttpGet("{eventId:guid}/my-booking/pass")]
    public async Task<ActionResult<MyHostedEventPassRecord>> GetMyPass(
        Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var booking = await Newest(db.HostedEventBookings.AsNoTracking()
            .Include(b => b.HostedEvent).ThenInclude(e => e.Place)
            .Include(b => b.LeadAppUser)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventNight)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
            // A CANCELLED booking is included here, unlike everywhere else on this door. The
            // venue released it and revoked the pass with a reason; the guest is the one person
            // who needs to be able to show that pass to somebody and be told why it will not work.
            .Where(b => b.HostedEventId == eventId && b.LeadAppUserId == userId))
            .FirstOrDefaultAsync(ct);
        if (booking is null) return NotFound();

        var passes = await db.HostedEventPasses.AsNoTracking()
            .Include(p => p.CheckedInByAppUser)
            .Where(p => p.HostedEventBookingId == booking.Id)
            .OrderByDescending(p => p.IssuedUtc)
            .ToListAsync(ct);

        // The live one when there is one; failing that the most recently WITHDRAWN — ordered by
        // when it was withdrawn rather than when it was issued, because a reissue-then-revoke
        // leaves the newer pass revoked first and its reason is the current one.
        var pass = passes.FirstOrDefault(p => p.RevokedUtc == null)
                ?? passes.OrderByDescending(p => p.RevokedUtc).FirstOrDefault();

        if (pass is null)
            // Three states, three sentences, because the waiting one said to somebody the venue
            // has already refused is the wrong sentence at the worst moment.
            return StatusCode(StatusCodes.Status403Forbidden, booking.Status switch
            {
                HostedEventBookingStatus.Confirmed =>
                    "The venue hasn't issued your pass yet. Ask them for one.",
                HostedEventBookingStatus.TurnedDown =>
                    "The venue couldn't take this booking, so there is no pass.",
                HostedEventBookingStatus.Cancelled =>
                    "This booking was released, so there is no pass.",
                _ => "Your pass is issued when the venue confirms your place.",
            });

        // The colour to collect at the desk, when the venue uses bands. Read here rather than
        // guessed by the page, so the pass and the door can never disagree about it.
        var bands = await db.HostedEventBands.AsNoTracking()
            .Where(b => b.HostedEventId == eventId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync(ct);

        var nights = await db.HostedEventNights.AsNoTracking()
            .CountAsync(n => n.HostedEventId == eventId, ct);

        // Where they sit, when the venue has seated them (phase 13) — a party split across two tables reads both.
        var seating = booking.Status != HostedEventBookingStatus.Confirmed ? [] : (await db.HostedEventDiningSeats.AsNoTracking()
                .Where(s => s.HostedEventBookingId == booking.Id)
                .Select(s => new
                {
                    s.HostedEventMenu.HostedEventNight.Date, s.HostedEventMenu.SortOrder, Sitting = s.HostedEventMenu.Title,
                    Table = s.HostedEventDiningTable.Name, s.People,
                })
                .ToListAsync(ct))
            .OrderBy(s => s.Date).ThenBy(s => s.SortOrder).ThenBy(s => s.Table)
            .Select(s => $"{s.Date:ddd MM/dd} · {s.Sitting} · {s.Table}{(s.People < booking.PartySize ? $" ({s.People})" : "")}")
            .ToList();

        return Ok(new MyHostedEventPassRecord(
            Entities.HostedEventBookingController.ToRecord(pass),
            booking.HostedEvent?.Name ?? "An event",
            booking.HostedEvent?.Place?.Name,
            booking.LeadAppUser?.DisplayName ?? "You",
            booking.PartySize,
            booking.Kind,
            booking.Nights
                .OrderBy(n => n.HostedEventNight.Date)
                .Select(n => new HostedEventBookingNightRecord(
                    n.HostedEventNightId, n.HostedEventNight.Date, n.HostedEventLayoutUnitId, EventCapacity.NameOf(n)))
                .ToList(),
            Band: EventBands.For(booking, bands, nights) is { } band
                ? new HostedEventBandRecord(
                    band.Id, band.Colour, band.Meaning, band.Hex, band.Rule, band.SortOrder)
                : null,
            Seating: seating));
    }

    /// <summary>
    /// Posts the guest their own pass again (item 235 phase 6).
    /// </summary>
    /// <remarks>
    /// <para><b>Because the commonest thing that goes wrong with a pass is a lost letter</b>, and
    /// the guest is standing there with a phone. Every other route to a fresh copy goes through
    /// the venue — a message, somebody at a desk, a reissue — which is a lot of machinery for
    /// "send it to me again".</para>
    ///
    /// <para>It re-posts the decision letter, which is the letter the pass travels in, so the
    /// guest gets exactly what the venue sent rather than a second kind of mail that could say
    /// something different. Nothing is issued or replaced: the pass in their pocket is the pass.
    /// </para>
    /// </remarks>
    [HttpPost("{eventId:guid}/my-booking/pass/email")]
    public async Task<ActionResult<MyHostedEventBookingRecord>> EmailMyPass(
        Guid eventId, [FromServices] EventGuestMailer mail, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var booking = await Newest(db.HostedEventBookings
            .Where(b => b.HostedEventId == eventId && b.LeadAppUserId == userId))
            .FirstOrDefaultAsync(ct);
        if (booking is null) return NotFound();

        if (booking.Status != HostedEventBookingStatus.Confirmed)
            return Conflict("Your pass is sent when the venue confirms your place.");

        if (await EventPasses.LiveAsync(db, booking.Id, ct) is null)
            return Conflict("There is no pass on this booking at the moment. The venue can issue "
                          + "you one.");

        if (!mail.IsConfigured)
            return Conflict("This site has no outgoing mail set up, so nothing can be posted. The "
                          + "pass on this screen is the same one.");

        if (!await mail.SendDecisionAsync(db, booking.Id, ct))
            return Conflict("The letter could not be sent just now and nothing was posted. Try "
                          + "again in a minute — the pass on this screen works either way.");

        return Ok(await ReloadAsync(db, userId, booking.Id, ct));
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
    /// <summary>
    /// Takes the places a guest picked on the plan and holds them until the venue answers.
    /// </summary>
    /// <remarks>
    /// <para><b>Its own endpoint, not a flag on asking.</b> Asking holds nothing and joins a queue;
    /// picking takes squares out of everybody else's reach. They are different acts with different
    /// consequences, and one endpoint serving both would have had to guess which the caller meant.
    /// </para>
    ///
    /// <para><b>The race is settled by the database, not by this method.</b> Two guests pressing
    /// the button a millisecond apart both see the seat free before either writes. So the insert is
    /// attempted and the unique index decides; the loser catches the violation, re-reads what
    /// actually happened, and is told which square went by name with the rest of their choice
    /// intact. Checking first and writing second would simply lose the race more slowly.</para>
    ///
    /// <para><b>A cap per account, because rate limiting cannot see the real abuse.</b> Holding
    /// every seat in a house for two days empties a venue's weekend without booking anything, and
    /// an attacker has more than one address. Five live holds is more than any real guest needs.
    /// </para>
    /// </remarks>
    [HttpPost("{eventId:guid}/holds")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Services.RateLimiting.HostedBookingPolicy)]
    public async Task<ActionResult<MyHostedEventBookingRecord>> HoldPlaces(
        Guid eventId, [FromBody] HoldHostedEventPlacesRequest request,
        [FromServices] EventGuestMailer mail, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var ev = await BookableEventAsync(db, eventId, ct);
        if (ev is null) return NotFound();

        if (ev.BookingMode != HostedEventBookingMode.Pick)
            return Conflict("Places at this event are asked for and the venue puts you somewhere, "
                          + "rather than picked on the plan.");

        if (!EventCapacity.IsOpenForRequests(ev, DateTime.UtcNow))
            return Conflict("This event has stopped taking bookings.");

        var chosen = request.Nights ?? [];
        if (chosen.Count == 0)
            return BadRequest("Pick at least one place before holding anything.");

        if (await WhyTheseNightsAreNotRealAsync(db, eventId, chosen, ct) is { } bad)
            return BadRequest(bad);

        if (await db.HostedEventBookings.AnyAsync(
                b => b.HostedEventId == eventId && b.LeadAppUserId == userId
                  && BookingTransitions.Waiting.Contains(b.Status), ct))
            return Conflict("You already have places at this event waiting on the venue. "
                          + "Let those go first if you want to choose differently.");

        if (await CountMyLiveHoldsAsync(db, userId, ct) >= MaximumLiveHolds)
            return Conflict($"You are holding places at {MaximumLiveHolds} events already. "
                          + "Wait for a venue to answer, or let one go, before choosing more.");

        if (await WhatIsNotOnOfferAsync(db, eventId, chosen, ct) is { } withheld)
            return Conflict(withheld);

        var (contact, missing) = await BookingContact.ForAccountAsync(
            db, userId, request.FirstName, request.LastName, request.Phone, ct);
        if (contact is null) return BadRequest(missing);

        var now = DateTime.UtcNow;

        // Somebody not signed in may be confirming one of these by email (slice 11d). Their pick is
        // not a booking and the database's arbiter cannot see it, so it is checked here, after the
        // lapsed ones have gone back.
        await EmailPicks.RetireLapsedAsync(db, now, eventId, ct);
        if (await EmailPicks.PendingAmongAsync(db, eventId, chosen, exceptPickId: null, ct) is { } pending)
            return Conflict(await WhoGotThereFirstAsync(_db, eventId,
                [.. chosen.Where(c => c.HostedEventLayoutUnitId is { } u && pending.Contains(u))], ct));

        await BookingContact.FillEmptyNamesAsync(db, userId, contact, ct);

        var booking = new HostedEventBooking
        {
            ContactPhone = contact.Phone,
            Id = Guid.NewGuid(),
            HostedEventId = eventId,
            LeadAppUserId = userId,
            PartySize = PartyPicking(ev, chosen, request.PartySize),
            Kind = HostedEventBookingKind.Overnight,
            Status = HostedEventBookingStatus.Requested,
            Note = Trimmed(request.Note),
            DateCreated = now,
            CreatedByAppUserId = userId,
        };
        db.HostedEventBookings.Add(booking);

        WriteNights(db, booking, chosen);
        WriteGuests(db, booking, request.Guests ?? []);

        BookingTransitions.Hold(booking, ev, now);
        await BookingTransitions.ApplyUmbrellaAsync(db, _sync, ev, booking, userId, now, ct);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsSomebodyGotThereFirst(ex))
        {
            // Somebody else's insert won. Re-read what is actually held now and say which squares
            // went, so the picker can repaint and the guest keeps the rest of their choice.
            return Conflict(await WhoGotThereFirstAsync(_db, eventId, chosen, ct));
        }

        // After the save, and best effort. A guest whose places are held but whose letter bounced
        // still holds the places; a letter about a hold that then failed to save is a lie.
        await mail.SendHoldPlacedAsync(db, booking.Id, ct);

        return Ok(await ReloadAsync(db, userId, booking.Id, ct));
    }

    /// <summary>
    /// How many people this picking is for: on a seating plan, the number of seats.
    /// </summary>
    /// <remarks>
    /// <para><b>A seat holds one person, so four people is four seats.</b> Taking the number the
    /// caller sent would let somebody hold ONE seat for a party of four — and the venue could then
    /// never confirm it, because confirming checks capacity and a seat seats one. The guest would
    /// sit in a queue that has no answer, which is worse than being refused at the moment they
    /// picked. Found by a board walk whose confirmation was refused for exactly this.</para>
    ///
    /// <para>Rooms are different and the caller's number stands: a family of five picking one room
    /// that sleeps five is right, and it is the room's capacity that judges it.</para>
    ///
    /// <para>The busiest night decides, since somebody taking two seats on Friday and one on
    /// Saturday is a party of two who are not all staying.</para>
    /// </remarks>
    internal static int PartyPicking(
        HostedEvent hosted, IReadOnlyList<HostedEventBookingNightChoice> chosen, int asked)
    {
        if (hosted.LayoutKind != HostedEventLayoutKind.Seats)
            return EventCapacity.ClampPartySize(asked);

        var busiest = chosen
            .Where(c => c.HostedEventLayoutUnitId is not null)
            .GroupBy(c => c.HostedEventNightId)
            .Select(g => g.Select(c => c.HostedEventLayoutUnitId).Distinct().Count())
            .DefaultIfEmpty(0)
            .Max();

        return EventCapacity.ClampPartySize(busiest);
    }

    /// <summary>The most events one account may be holding places at, at once.</summary>
    /// <remarks>
    /// Five is more than any real guest needs and far fewer than a script wants. It bounds the
    /// damage a determined person can do with one account; the hold expiry bounds how long they can
    /// do it for.
    /// </remarks>
    internal const int MaximumLiveHolds = 5;

    internal static Task<int> CountMyLiveHoldsAsync(
        BenDataContext db, Guid userId, CancellationToken ct)
        => db.HostedEventBookings.CountAsync(
               b => b.LeadAppUserId == userId
                 && b.Status == HostedEventBookingStatus.Held, ct);

    /// <summary>Whether this failure is the arbiter index refusing a second party.</summary>
    /// <remarks>
    /// 2601 and 2627 are SQL Server's two unique-violation numbers; 19 is SQLite's, which the tests
    /// run on. Anything else is a real fault and must not be dressed up as a lost race.
    /// </remarks>
    internal static bool IsSomebodyGotThereFirst(DbUpdateException ex)
        => ex.InnerException?.GetType().GetProperty("SqliteErrorCode")?.GetValue(ex.InnerException)
               is int sqlite && sqlite == 19
        || ex.InnerException?.GetType().GetProperty("Number")?.GetValue(ex.InnerException)
               is int number && number is 2601 or 2627;

    /// <summary>Which of the squares this guest picked are now somebody else's, in words.</summary>
    /// <param name="countEmailPicks">
    /// Whether a square somebody is confirming by email counts as gone (slice 11d). False when it is
    /// that very pick being turned into a booking, whose own squares would otherwise be named as
    /// taken from itself.
    /// </param>
    internal static async Task<HoldRefusedRecord> WhoGotThereFirstAsync(
        IDbContextFactory<BenDataContext> factory, Guid eventId,
        IReadOnlyList<HostedEventBookingNightChoice> chosen, CancellationToken ct,
        bool countEmailPicks = true)
    {
        // A fresh context: the failed save left the old one holding a booking that does not exist.
        await using var fresh = await factory.CreateDbContextAsync(ct);

        var occupancy = await PlanOccupancy.ReadAsync(fresh, eventId, ct);

        var taken = chosen
            .Where(c => c.HostedEventLayoutUnitId is { } unit
                     && occupancy.TryGetValue((c.HostedEventNightId, unit), out var cell)
                     && (cell.BookingId is not null || (countEmailPicks && cell.AwaitingEmail)))
            .Select(c => c.HostedEventLayoutUnitId!.Value)
            .Distinct()
            .ToList();

        var names = await fresh.HostedEventLayoutUnits.AsNoTracking()
            .Where(u => taken.Contains(u.Id))
            .Include(u => u.PlaceRoom)
            .ToListAsync(ct);

        var said = names.Count switch
        {
            0 => "Somebody took one of those places a moment ago. Have another look at the plan.",
            1 => $"{EventCapacity.NameOf(names[0])} was taken a moment ago. Pick another.",
            _ => $"{string.Join(" and ", names.Select(EventCapacity.NameOf))} were taken a moment "
               + "ago. Pick again for those; the rest of your choice is still free.",
        };

        return new HoldRefusedRecord(said, taken);
    }

    /// <summary>Why the venue is not offering one of these squares, or null when it is.</summary>
    internal static async Task<string?> WhatIsNotOnOfferAsync(
        BenDataContext db, Guid eventId,
        IReadOnlyList<HostedEventBookingNightChoice> chosen, CancellationToken ct)
    {
        var blocks = await PlanOccupancy.BlocksAsync(db, eventId, ct);
        if (blocks.Count == 0) return null;

        foreach (var choice in chosen)
        {
            if (choice.HostedEventLayoutUnitId is not { } unitId) continue;

            var unit = await db.HostedEventLayoutUnits.AsNoTracking()
                .Include(u => u.PlaceRoom)
                .FirstOrDefaultAsync(u => u.Id == unitId, ct);
            if (unit is null) continue;

            if (EventCapacity.WhyItIsNotOffered(
                    blocks, unitId, choice.HostedEventNightId, EventCapacity.NameOf(unit)) is { } why)
                return why;
        }

        return null;
    }

    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Services.RateLimiting.HostedBookingPolicy)]
    [HttpPost("{eventId:guid}/bookings")]
    public async Task<ActionResult<MyHostedEventBookingRecord>> RequestAPlace(
        Guid eventId, [FromBody] RequestHostedEventBookingRequest request,
        [FromServices] EventGuestMailer mail, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var ev = await BookableEventAsync(db, eventId, ct);
        if (ev is null) return NotFound();
        // Defensive: BookableEventAsync already excludes anything that is not taking bookings, so
        // this cannot fire today. It reads the state rather than the stamp anyway, because a second
        // reader on a different source of truth is how the two came to disagree in the first place.
        if (HostedEventStates.CalledOff.Contains(ev.LifecycleState))
            return Conflict("This event has been called off.");
        if (!EventCapacity.IsOpenForRequests(ev, DateTime.UtcNow))
            return Conflict("This event has stopped taking bookings.");
        if (request.Kind == HostedEventBookingKind.DayPass && ev.DayPassCapacity is 0)
            return Conflict("This event isn't selling day passes.");

        var (contact, missing) = await BookingContact.ForAccountAsync(
            db, userId, request.FirstName, request.LastName, request.Phone, ct);
        if (contact is null) return BadRequest(missing);

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
            ContactPhone = contact.Phone,
            Note = Trimmed(request.Note),
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.HostedEventBookings.Add(booking);

        if (await WhyTheseNightsAreNotRealAsync(db, eventId, request.Nights ?? [], ct) is { } bad)
            return BadRequest(bad);

        WriteNights(db, booking, request.Nights ?? []);
        WriteGuests(db, booking, request.Guests ?? []);
        await BookingContact.FillEmptyNamesAsync(db, userId, contact, ct);

        await db.SaveChangesAsync(ct);

        // Silence reads as a booking: somebody who filled in a form and heard nothing assumes it
        // worked, and turns up with a suitcase. The letter says the opposite in as many words.
        await mail.SendAskedAsync(db, booking.Id, ct);

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
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Services.RateLimiting.HostedBookingPolicy)]
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
            WriteNights(db, booking, request.Nights);
        }

        if (request.Guests is not null)
        {
            db.HostedEventBookingGuests.RemoveRange(booking.Guests);
            booking.Guests.Clear();
            WriteGuests(db, booking, request.Guests);
        }

        // Back to the queue, and the room-nights it held are released with the umbrella row: what
        // the venue agreed to is not what is being asked for any more.
        if (wasConfirmed && changesWhatWasAgreed)
        {
            BookingTransitions.Request(booking, DateTime.UtcNow);
            booking.DecidedByAppUserId = null;
            booking.GuestAcknowledgedUtc = null;

            var hosted = await db.HostedEvents
                .Include(e => e.Nights)
                .FirstOrDefaultAsync(e => e.Id == booking.HostedEventId, ct);
            if (hosted is not null)
                await BookingTransitions.ApplyUmbrellaAsync(
                    db, _sync, hosted, booking, booking.LeadAppUserId, DateTime.UtcNow, ct);
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

        var booking = await Newest(db.HostedEventBookings
            .Where(b => b.HostedEventId == eventId && b.LeadAppUserId == userId))
            .FirstOrDefaultAsync(ct);
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
    ///
    /// <para><b>A turned-down booking has nothing to withdraw</b>, and is told so. The first
    /// version fell through to the confirmed branch and recorded a cancellation request against a
    /// booking the venue had already said no to — a host would have seen "asked to cancel" on a
    /// party that was never coming (item 235 phase 1).</para>
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
            // A request holds no table, but one that was confirmed, seated and then changed back into a request
            // can still have a seat row; it goes with the booking (phase 13).
            await db.HostedEventDiningSeats.Where(x => x.HostedEventBookingId == booking.Id).ExecuteDeleteAsync(ct);
            db.HostedEventBookings.Remove(booking);
            await db.SaveChangesAsync(ct);
            return NoContent();
        }

        if (booking.Status == HostedEventBookingStatus.TurnedDown)
            return Conflict("This booking was already turned down; there is nothing to withdraw.");

        // HELD: let go at once, and the places go straight back to the house.
        //
        // There was no branch for this, so a guest who picked seats and changed their mind fell
        // through to the confirmed path below and merely REQUESTED a cancellation — the seats
        // stayed theirs until the hold lapsed, out of everybody's reach, for a booking they had
        // already abandoned. Nothing has been decided and nothing catered against, so there is
        // nobody to ask.
        //
        // Cancelled rather than deleted: the row is the record of what they held and when they let
        // it go, which is the whole of the conversation if they ring up later. The nights are
        // released, so the arbiter index frees the seat immediately.
        if (booking.Status == HostedEventBookingStatus.Held)
        {
            var now = DateTime.UtcNow;
            BookingTransitions.Cancel(booking, userId, Trimmed(reason) ?? "Let go by the guest.", now);

            var hosted = await db.HostedEvents
                .Include(e => e.Nights)
                .FirstOrDefaultAsync(e => e.Id == booking.HostedEventId, ct);
            if (hosted is not null)
                await BookingTransitions.ApplyUmbrellaAsync(db, _sync, hosted, booking, userId, now, ct);

            await db.SaveChangesAsync(ct);
            return NoContent();
        }

        // Confirmed: record the ask rather than acting on it.
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
    internal static Task<HostedEvent?> BookableEventAsync(
        BenDataContext db, Guid eventId, CancellationToken ct)
        => db.HostedEvents
            .FirstOrDefaultAsync(
                e => e.Id == eventId && HostedEventStates.TakingBookings.Contains(e.LifecycleState), ct);

    /// <summary>
    /// Why these nights are not nights of this event, or null when they are.
    /// </summary>
    /// <remarks>
    /// A room is NOT checked for being offered: a guest naming a room the event is not using is
    /// expressing a preference the venue will override anyway, and refusing it would be a form
    /// error about somebody else's booking system.
    /// </remarks>
    internal static async Task<string?> WhyTheseNightsAreNotRealAsync(
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

    /// <summary>
    /// Writes one row per place per night, and keeps every one of them.
    /// </summary>
    /// <remarks>
    /// <b>Grouped by night AND unit.</b> It used to group by night alone and keep the last, which
    /// silently threw away every place but one: a party picking three seats got one seat, and a
    /// family taking a double and a twin got the twin. That was the same wrong assumption the night
    /// key carried until phase 4 — a party holds one thing per night — and it survived in the
    /// writer after the key was widened. Found by a hold test whose second guest was allowed a seat
    /// somebody already had, because the seat that clashed had been discarded before the insert.
    /// </remarks>
    internal static void WriteNights(
        BenDataContext db, HostedEventBooking booking, IReadOnlyList<HostedEventBookingNightChoice> nights)
    {
        foreach (var choice in nights
                     .GroupBy(n => (n.HostedEventNightId, n.HostedEventLayoutUnitId))
                     .Select(g => g.Last()))
        {
            var row = new HostedEventBookingNight
            {
                Id = Guid.NewGuid(),
                HostedEventBookingId = booking.Id,
                HostedEventNightId = choice.HostedEventNightId,
                HostedEventLayoutUnitId = choice.HostedEventLayoutUnitId,
                People = choice.People,
                DateCreated = DateTime.UtcNow,
            };
            // Added through the set, not only the collection. On a booking already in the database (a guest
            // changing their nights), a row found through the navigation with its key set is taken for an existing
            // row and saved as an UPDATE of nothing.
            db.HostedEventBookingNights.Add(row);
            booking.Nights.Add(row);
        }
    }

    internal static void WriteGuests(
        BenDataContext db, HostedEventBooking booking, IReadOnlyList<HostedEventBookingGuestInput> guests)
    {
        var order = 0;
        foreach (var guest in guests)
        {
            var name = Trimmed(guest.DisplayName);
            if (name is null) continue;

            var row = new HostedEventBookingGuest
            {
                Id = Guid.NewGuid(),
                HostedEventBookingId = booking.Id,
                DisplayName = name,
                AppUserId = guest.AppUserId,
                DietaryNotes = Trimmed(guest.DietaryNotes),
                SortOrder = order++,
                DateCreated = DateTime.UtcNow,
            };
            db.HostedEventBookingGuests.Add(row);   // through the set, for the reason WriteNights gives
            booking.Guests.Add(row);
        }
    }

    /// <param name="includeReleased">
    /// Whether a cancelled booking counts as one of this person's. False everywhere that asks
    /// "have I got a place here" — a released booking is not a place, and treating it as one would
    /// stop the guest ever asking again. True only for the list of what they have coming up, where
    /// leaving it out silently drops a weekend they are still keeping free.
    /// </param>
    internal static IQueryable<HostedEventBooking> MineQuery(
        BenDataContext db, Guid userId, bool includeReleased = false)
        => db.HostedEventBookings
            .AsNoTracking()
            .Include(b => b.HostedEvent).ThenInclude(e => e.Organization)
            .Include(b => b.HostedEvent).ThenInclude(e => e.Place)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventNight)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
            .Include(b => b.Guests)
            .Where(b => b.LeadAppUserId == userId
                     && (includeReleased || b.Status != HostedEventBookingStatus.Cancelled));

    /// <summary>
    /// One guest's bookings at one event, the one that matters first.
    /// </summary>
    /// <remarks>
    /// <para><b>A guest may have several at one event over time</b> — asked and turned down, held
    /// and lapsed, then confirmed — and only one of them can be live, because the database says
    /// so. Without an order, <c>First</c> takes whichever the database hands back, which in
    /// practice is the OLDEST: the pass page showed a guest the words "this booking was released"
    /// while their confirmed pass sat one row further down. Found by a browser test doing what a
    /// guest does, which is to try more than once.</para>
    ///
    /// <para>Live first, then the most recent, so a history of refusals never hides the booking
    /// somebody actually holds.</para>
    /// </remarks>
    private static IQueryable<HostedEventBooking> Newest(IQueryable<HostedEventBooking> bookings)
        => bookings
            .OrderByDescending(b => b.Status == HostedEventBookingStatus.Confirmed
                                 || b.Status == HostedEventBookingStatus.Held
                                 || b.Status == HostedEventBookingStatus.Requested)
            .ThenByDescending(b => b.DateCreated);

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
    internal static MyHostedEventBookingRecord ToMine(HostedEventBooking b)
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
                    n.HostedEventLayoutUnitId, EventCapacity.NameOf(n)))
                .ToList(),
            b.Guests
                .OrderBy(g => g.SortOrder)
                .Select(g => new HostedEventBookingGuestRecord(
                    g.Id, g.DisplayName, g.AppUserId, g.DietaryNotes, g.SortOrder))
                .ToList(),
            b.HoldExpiresUtc);

    internal static string? Trimmed(string? value)
        => value?.Trim() is { Length: > 0 } v ? v : null;
}
