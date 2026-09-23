using Microsoft.Extensions.DependencyInjection;
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
    private readonly Services.Access.HostedEventAccess _access;
    private readonly EventGuestMailer _guestMail;
    private readonly Ben.Data.Common.Interfaces.IEmailService _email;
    private readonly Ben.Data.Common.SiteIdentity _site;
    private readonly ILogger<HostedEventBookingController> _logger;

    /// <summary>
    /// Where the venue's invitation goes: into the same save as the token it carries (item 239b).
    /// </summary>
    private readonly IOutboxEmailQueue _outbox;

    /// <summary>An invitation is good for a fortnight, the same as every other link here.</summary>
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(14);

    public HostedEventBookingController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security,
        HostedEventCalendarSync sync,
        Services.Access.HostedEventAccess access,
        EventGuestMailer guestMail,
        Ben.Data.Common.Interfaces.IEmailService email,
        Microsoft.Extensions.Options.IOptions<Ben.Data.Common.SiteIdentity> site,
        ILogger<HostedEventBookingController> logger,
        IOutboxEmailQueue outbox)
        : base(dbFactory, mapper, security)
    {
        _outbox = outbox;
        _sync = sync; _access = access; _guestMail = guestMail;
        _email = email; _site = site.Value; _logger = logger;
    }

    // ── reading ──────────────────────────────────────────────────────────────

    /// <summary>The whole weekend: rooms offered, how full each is per night, and every booking.</summary>
    /// <remarks>
    /// Readable by any member, because knowing how full the house is is not a billing question.
    /// <b>Dietary notes are not</b>: they are health information about named guests, so they are
    /// withheld from a member who cannot decide a booking. An ordinary member of a ghost-hunting
    /// group has no reason to read a stranger's allergy list.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<HostedEventBookingBoardRecord>> GetBoard(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        // Membership is not enough. The board carries guests' names, addresses and dietary notes,
        // and every member of a group has no business with any of the three.
        if (!await CanReadBookingsAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await LoadEventAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        return Ok(await BoardAsync(db, ev, await CanDecideAsync(userId.Value, orgId, eventId, db, ct), ct));
    }

    /// <summary>
    /// What the kitchen has to cook differently, for the whole event.
    /// </summary>
    /// <remarks>
    /// <para><b>Confirmed parties by default</b>, because that is who the venue is buying food for.
    /// Undecided parties can be folded in with <c>includeUnconfirmed</c> for a host ordering ahead of a
    /// weekend that has not been decided yet — and the answer says which it is, so a cook cannot
    /// read a provisional number as a settled one.</para>
    ///
    /// <para><b>Takes the deciding permission, not membership.</b> Everything in here is health
    /// information about named individuals.</para>
    ///
    /// <para><b>One night at a time with <c>night</c></b>, because a cook working Saturday is
    /// cooking for the people who are there on Saturday and the weekend's total is the wrong
    /// number to hand them. Omitted, it is the whole event.</para>
    /// </remarks>
    [HttpGet("dietary")]
    public async Task<ActionResult<HostedEventDietaryRecord>> GetDietary(
        Guid orgId, Guid eventId, [FromQuery] bool includeUnconfirmed,
        [FromQuery] Guid? night, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await LoadEventAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        if (night is { } asked && ev.Nights.All(n => n.Id != asked))
            return BadRequest("That isn't a night of this event.");

        return Ok(await DietaryAsync(db, eventId, includeUnconfirmed, night, ct));
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
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

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
        BookingTransitions.Confirm(booking, userId.Value, request.DecisionNote, DateTime.UtcNow);
        // A decision the guest has not seen yet, whatever they had seen before.
        booking.GuestAcknowledgedUtc = null;
        Touch(booking, userId.Value);

        await BookingTransitions.ApplyUmbrellaAsync(
            db, _sync, ev, booking, userId.Value, DateTime.UtcNow, ct);

        // The pass is part of confirming, not a second thing a host has to remember. A guest who
        // was told yes and given nothing to show at the door has to be looked up by name on the
        // night, which is the queue this whole feature exists to remove.
        await EventPasses.EnsureAsync(db, booking, userId.Value, ct);

        // The confirmation and the letter carrying the pass commit together, or neither does (item
        // 239b). It used to be "after the save, and best effort" — right while the letter was a
        // call to a mail system that might not answer, since a confirmed guest whose letter
        // bounced is still a confirmed guest. The letter is now a row in this database, sent later
        // by the outbox and retried there. Failing to write it means the database failed, and
        // then the host is better told "try again" than left believing a guest has their pass.
        await SaveInOneTransactionAndAuditAsync(db, booking, booking.Id, userId.Value,
            () => _guestMail.SendDecisionAsync(db, booking.Id, ct), ct);

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
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await LoadEventAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        var booking = await LoadBookingAsync(db, eventId, bookingId, ct);
        if (booking is null) return NotFound();

        // Read before anything moves: a pass says who and how many and which nights, so a change
        // to any of those makes the code in somebody's pocket say the wrong thing.
        var changesWhatThePassSays =
            (request.PartySize is int wanted
                && EventCapacity.ClampPartySize(wanted) != booking.PartySize)
            || request.Nights is not null;

        if (request.PartySize is int size) booking.PartySize = EventCapacity.ClampPartySize(size);
        if (request.Note is not null) booking.Note = Trimmed(request.Note);

        var nights = request.Nights ?? booking.Nights
            .Select(n => new HostedEventBookingNightChoice(n.HostedEventNightId, n.HostedEventLayoutUnitId))
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
        {
            await BookingTransitions.ApplyUmbrellaAsync(
                db, _sync, ev, booking, userId.Value, DateTime.UtcNow, ct);

            // A pass is never edited, only replaced. The old one is revoked with a reason a door
            // can read aloud, so a guest showing the code they were sent first is told it was
            // superseded rather than that it was never real.
            if (changesWhatThePassSays)
                await EventPasses.ReissueAsync(db, booking, userId.Value,
                    "The booking changed. Ask them for the newer pass.", ct);
            else
                await EventPasses.EnsureAsync(db, booking, userId.Value, ct);
        }

        await db.SaveChangesAsync(ct);
        return Ok(await OneAsync(db, eventId, booking.Id, ct));
    }

    // ── the venue booking somebody in ────────────────────────────────────────

    /// <summary>
    /// Creates a booking for somebody who asked by phone, by email or at the door.
    /// </summary>
    /// <remarks>
    /// Takes an existing account. Somebody with no account goes through <c>on-behalf/invite</c>
    /// instead, because creating an account for a person who never asked for one is the guest
    /// door's job and there is exactly one of those.
    /// </remarks>
    /// <summary>
    /// Gives a party longer to be decided about.
    /// </summary>
    /// <remarks>
    /// <para>What a host reaches for when a hold is about to lapse and they are not ready. Without
    /// it the only choices are to confirm a party they have not decided about or to let the clock
    /// take the decision for them, and neither is a decision.</para>
    ///
    /// <para>Extended from NOW rather than from the old deadline, which is what a host means by
    /// "give me another day": from the moment they press it, not from a moment that has already
    /// passed.</para>
    /// </remarks>
    [HttpPost("{bookingId:guid}/hold/extend")]
    public async Task<ActionResult<HostedEventBookingRecord>> ExtendHold(
        Guid orgId, Guid eventId, Guid bookingId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        var ev = await LoadEventAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        var booking = await LoadBookingAsync(db, eventId, bookingId, ct);
        if (booking is null) return NotFound();

        if (booking.Status != HostedEventBookingStatus.Held)
            return Conflict("Only a party that picked their own places has a hold to extend.");

        var now = DateTime.UtcNow;
        booking.HoldExpiresUtc = now.AddMinutes(ev.HoldMinutes);
        Touch(booking, userId.Value);
        await db.SaveChangesAsync(ct);

        return Ok(await OneAsync(db, eventId, booking.Id, ct));
    }

    /// <summary>
    /// Gives back every hold on this event that has already run out, now.
    /// </summary>
    /// <remarks>
    /// <para>The job does this within five minutes anyway. This exists because five minutes is a
    /// long time with somebody standing at a desk asking whether the Blue Room is free — a host who
    /// can see that three holds lapsed at three o'clock should be able to act on it rather than
    /// wait for a timer they cannot see.</para>
    ///
    /// <para>The same transition the job uses, so a hold released by hand and one released by the
    /// clock are the same thing afterwards. The guest is told either way.</para>
    /// </remarks>
    [HttpPost("holds/release-lapsed")]
    public async Task<ActionResult<HostedEventBookingBoardRecord>> ReleaseLapsedHolds(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();
        var ev = await LoadEventAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        var now = DateTime.UtcNow;
        var lapsed = await db.HostedEventBookings
            .Include(b => b.Nights)
            .Where(b => b.HostedEventId == eventId
                     && b.Status == HostedEventBookingStatus.Held
                     && b.HoldExpiresUtc != null
                     && b.HoldExpiresUtc <= now)
            .ToListAsync(ct);

        foreach (var booking in lapsed)
        {
            BookingTransitions.Expire(booking, now);
            await BookingTransitions.ApplyUmbrellaAsync(
                db, _sync, ev, booking, userId.Value, now, ct);
        }

        if (lapsed.Count > 0) await db.SaveChangesAsync(ct);

        // Told after the save, and never inside it: a letter that cannot be sent must not roll back
        // the release, or the places stay stuck behind a mail server.
        foreach (var booking in lapsed)
        {
            try { await _guestMail.SendHoldLapsedAsync(db, booking, ct); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Booking {BookingId} was released but the guest could not be told.", booking.Id);
            }
        }

        return Ok(await BoardAsync(db, ev, canSeeDietary: true, ct));
    }

    [HttpPost("on-behalf")]
    public async Task<ActionResult<HostedEventBookingRecord>> CreateOnBehalf(
        Guid orgId, Guid eventId,
        [FromBody] CreateHostedEventBookingOnBehalfRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await LoadEventAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        if (request.LeadAppUserId is not { } leadId)
            return BadRequest(
                "Choose who this booking is for. If they have no account here, invite them by email "
              + "instead and the booking appears when they accept.");
        if (!await db.AppUsers.AnyAsync(u => u.Id == leadId, ct))
            return BadRequest("That person no longer has an account here.");

        var booking = new HostedEventBooking
        {
            Id = Guid.NewGuid(),
            HostedEventId = eventId,
            LeadAppUserId = leadId,
            PartySize = EventCapacity.ClampPartySize(request.PartySize),
            Kind = request.Kind,
            // Set through BookingTransitions immediately below, which is the only place a status
            // is ever decided. The initialiser needs a value and this is the one it will keep.
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

            var now = DateTime.UtcNow;
            BookingTransitions.Confirm(booking, userId.Value, note: null, now);
            await BookingTransitions.ApplyUmbrellaAsync(db, _sync, ev, booking, userId.Value, now, ct);
        }

        await db.SaveChangesAsync(ct);
        return Ok(await OneAsync(db, eventId, booking.Id, ct));
    }

    /// <summary>
    /// Asks somebody with no account here to come for the day.
    /// </summary>
    /// <remarks>
    /// <para><b>The same door a walk-up guest uses</b>, pointed at this event's umbrella row: an
    /// address, a single-use link, a fortnight to click it. Building a second invitation for
    /// events would have given the site two answers to "is this really your address", and only
    /// one of them would have gone on being maintained.</para>
    ///
    /// <para><b>Nothing is held until they click.</b> This reserves no room and no day pass, and
    /// the answer says so rather than looking like a booking — a host who thinks they have held a
    /// room for a phone caller will sell it twice. To hold something now, make the booking against
    /// an account.</para>
    ///
    /// <para><b>A day pass, not a room.</b> Sleeping somewhere means choosing rooms night by
    /// night, and a hyperlink is not a booking form. The host can move them into a room when they
    /// confirm, which is where every other room decision is made anyway.</para>
    ///
    /// <para><b>It answers the same way whether or not that address has an account</b>, exactly as
    /// the walk-up invitation does. "Does this person have an account here" is not a question this
    /// endpoint exists to answer.</para>
    /// </remarks>
    [HttpPost("on-behalf/invite")]
    public async Task<ActionResult<HostedEventGuestInviteRecord>> InviteByEmail(
        Guid orgId, Guid eventId,
        [FromBody] InviteHostedEventGuestRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 320)
            return BadRequest("A valid email address is needed.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var ev = await LoadEventAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        // Refused here rather than at the link, because a host who sends thirty invitations to a
        // draft would find out a fortnight later when thirty people were turned away.
        if (HostedEventGuestDoor.WhyTheEmailDoorIsClosed(ev, DateTime.UtcNow, theHostSentThisLink: true)
            is { } shut)
            return BadRequest(shut);

        // The umbrella row is what the invitation points at, and it may not exist yet on an event
        // nobody has booked into.
        var umbrella = await _sync.SyncAsync(db, ev, userId.Value, ct);

        // One pending invitation per address, reissued rather than stacked: a host who is not sure
        // the first one arrived will simply send it again.
        var invite = await db.EventAttendanceInvites
            .FirstOrDefaultAsync(i => i.OrgCalendarEventId == umbrella.Id && i.Email == email, ct);

        var expires = DateTime.UtcNow.Add(InviteLifetime);

        if (invite is { DateConfirmed: not null })
            // Already accepted; their booking is on the board. Saying so distinguishes nothing
            // that the host cannot already see there.
            return Ok(new HostedEventGuestInviteRecord(email, Sent: false, invite.DateExpires));

        var token = Convert.ToHexString(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        if (invite is null)
        {
            invite = new EventAttendanceInvite
            {
                Id = Guid.NewGuid(),
                OrgCalendarEventId = umbrella.Id,
                Email = email,
                DisplayName = Trimmed(request.DisplayName),
                Seats = EventCapacity.ClampPartySize(request.PartySize),
                Token = token,
                DateExpires = expires,
                InvitedByAppUserId = userId.Value,
                DateCreated = DateTime.UtcNow,
                CreatedByAppUserId = userId.Value,
            };
            db.EventAttendanceInvites.Add(invite);
        }
        else
        {
            invite.Token = token;
            invite.DateExpires = expires;
            invite.Seats = EventCapacity.ClampPartySize(request.PartySize);
            // A guest who asked for their own link and is now being invited by the host gets the
            // host's latitude from here on: same person, better standing.
            invite.InvitedByAppUserId = userId.Value;
            if (Trimmed(request.DisplayName) is { } name) invite.DisplayName = name;
            invite.DateUpdated = DateTime.UtcNow;
            invite.UpdatedByAppUserId = userId.Value;
        }

        // Queued BEFORE the save, so the token and the letter carrying it are one write (item
        // 239b). Inviting again rotates the token; saving it first and sending afterwards could
        // leave the guest's earlier link dead and no new one written — and because the outbox
        // swallowed its own failure, "sent" still came back true to the host at the door.
        var sent = await TryQueueInviteAsync(db, email, ev, token, ct);
        await db.SaveChangesAsync(ct);

        return Ok(new HostedEventGuestInviteRecord(email, sent, expires));
    }

    // ── passes and the door (phase 3) ────────────────────────────────────────

    /// <summary>
    /// Gives a confirmed booking a pass, or hands back the one it already has.
    /// </summary>
    /// <remarks>
    /// Confirming already issues one, so this exists for the host who cannot see a pass and wants
    /// one — and it is deliberately the same call, not a second kind of pass. Two live codes for
    /// one party is two codes at a door, one of which is the wrong one.
    /// </remarks>
    [HttpPost("{bookingId:guid}/pass")]
    public async Task<ActionResult<HostedEventPassRecord>> IssuePass(
        Guid orgId, Guid eventId, Guid bookingId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var booking = await LoadBookingForPassAsync(db, orgId, eventId, bookingId, ct);
        if (booking is null) return NotFound();
        if (!EventPasses.MayHaveAPass(booking.Status))
            return BadRequest("Confirm the booking first. A pass for a booking nobody has agreed to "
                            + "is a ticket somebody would turn up holding.");

        var pass = await EventPasses.EnsureAsync(db, booking, userId.Value, ct);
        await db.SaveChangesAsync(ct);

        return Ok(await PassRecordAsync(db, pass.Id, ct));
    }

    /// <summary>Withdraws a pass, in words the door can read out.</summary>
    [HttpPost("{bookingId:guid}/pass/revoke")]
    public async Task<ActionResult<HostedEventPassRecord>> RevokePass(
        Guid orgId, Guid eventId, Guid bookingId,
        [FromBody] RevokeHostedEventPassRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (Trimmed(request.Reason) is not { } reason)
            return BadRequest("Say why. The door reads this out to whoever is holding the pass.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var booking = await LoadBookingForPassAsync(db, orgId, eventId, bookingId, ct);
        if (booking is null) return NotFound();

        var pass = await EventPasses.LiveAsync(db, bookingId, ct);
        if (pass is null) return BadRequest("This booking has no live pass to withdraw.");

        EventPasses.Revoke(pass, userId.Value, reason);
        await SaveAndAuditAsync(db, pass, pass.Id, userId.Value, ct);

        return Ok(await PassRecordAsync(db, pass.Id, ct));
    }

    /// <summary>Withdraws the current pass and issues a fresh one in its place.</summary>
    /// <remarks>
    /// For the guest who lost the letter, and for the booking that changed in a way an edit did
    /// not catch. The new pass remembers the one it replaced, so "what happened to the code I was
    /// sent" always has an answer.
    /// </remarks>
    [HttpPost("{bookingId:guid}/pass/reissue")]
    public async Task<ActionResult<HostedEventPassRecord>> ReissuePass(
        Guid orgId, Guid eventId, Guid bookingId,
        [FromBody] RevokeHostedEventPassRequest? request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var booking = await LoadBookingForPassAsync(db, orgId, eventId, bookingId, ct);
        if (booking is null) return NotFound();
        if (!EventPasses.MayHaveAPass(booking.Status))
            return BadRequest("Confirm the booking first.");

        var pass = await EventPasses.ReissueAsync(db, booking, userId.Value,
            Trimmed(request?.Reason) ?? "The venue issued a replacement pass.", ct);
        await db.SaveChangesAsync(ct);
        // The old pass's withdrawal and the new one's issue, as one entry against the booking they belong to.
        if (HttpContext?.RequestServices?.GetService<Ben.Service.RepositoryService.GenericInterfaces.IAuditLogService>() is { } audit)
            await TryAuditAsync(audit.LogCreateAsync(nameof(HostedEventPass), pass.Id, pass, userId.Value, Ben.Data.Common.Constants.AppSources.WebApi));

        return Ok(await PassRecordAsync(db, pass.Id, ct));
    }

    /// <summary>
    /// Sends the confirmation letter, pass and diary file included, again.
    /// </summary>
    /// <remarks>
    /// <para>For the guest who says the email never came. It is the whole confirmation rather than
    /// a bare picture of the code, because "send my pass again" from a guest means "I have
    /// nothing", not "I have everything but the square". <see cref="EventGuestMailer"/> stamps
    /// <c>EmailedUtc</c> on the pass as it goes, so the board's "not sent yet" mark clears.</para>
    ///
    /// <para><b>The truth is told to the caller.</b> The mailer itself is best effort and answers
    /// false rather than throwing, which is right after a confirmation — the decision stands
    /// whatever the mail did. It is wrong here: a host who pressed <i>Send</i> and was told 200
    /// while nothing was posted would stand at a door wondering why the guest has no code. So no
    /// mail service, no address and a send that failed are each a refusal in words that says
    /// which, and what to do instead (item 235 phase 1).</para>
    /// </remarks>
    [HttpPost("{bookingId:guid}/pass/email")]
    public async Task<ActionResult<HostedEventPassRecord>> EmailPass(
        Guid orgId, Guid eventId, Guid bookingId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var booking = await LoadBookingForPassAsync(db, orgId, eventId, bookingId, ct);
        if (booking is null) return NotFound();
        if (!EventPasses.MayHaveAPass(booking.Status))
            return BadRequest("Confirm the booking first. There is no pass to send until then.");

        var pass = await EventPasses.LiveAsync(db, bookingId, ct);
        if (pass is null)
            return BadRequest("This booking has no live pass to send. Issue one first.");

        if (!_guestMail.IsConfigured)
            return Conflict("This site has no outgoing mail set up, so the letter cannot be sent. "
                          + "Show them the pass from this screen instead.");

        var to = await db.AppUsers.AsNoTracking()
            .Where(u => u.Id == booking.LeadAppUserId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(to))
            return Conflict("This guest's account has no email address, so there is nobody to "
                          + "post the letter to.");

        // One save for the letter and the pass's EmailedUtc (item 239b), so the board's "not sent
        // yet" mark clears only when a letter really was queued. It used to clear regardless: the
        // send swallowed its own failures and returned, the stamp was saved, and the host was told
        // 200 for a letter that did not exist — the exact thing the paragraph above forbids.
        try
        {
            if (!await _guestMail.SendDecisionAsync(db, booking.Id, ct))
                return Conflict("The letter could not be sent just now and nothing was posted. Try "
                              + "again in a minute, or show them the pass from this screen.");
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not queue the pass letter for booking {BookingId}.", booking.Id);
            return Conflict("The letter could not be sent just now and nothing was posted. Try "
                          + "again in a minute, or show them the pass from this screen.");
        }

        return Ok(await PassRecordAsync(db, pass.Id, ct));
    }

    /// <summary>
    /// Scans a code at the door and says who has just walked in.
    /// </summary>
    /// <remarks>
    /// <para><b>Every refusal is a sentence somebody can say aloud</b> to the person in front of
    /// them. "Invalid" does not say whether to send them to the desk, wait, or turn them away —
    /// and the person on the door is usually not the person who took the booking.</para>
    ///
    /// <para><b>A second scan is not a refusal.</b> A door that turned away the same party walking
    /// back in from the car park would be worse than one that says when they first arrived and
    /// lets a human decide. The first arrival is the one kept.</para>
    ///
    /// <para>Send <c>checkIn: false</c> to look without admitting anybody, which is what a host
    /// testing a code before the doors open wants.</para>
    /// </remarks>
    [HttpPost("door/scan")]
    public async Task<ActionResult<HostedEventScanResult>> Scan(
        Guid orgId, Guid eventId, [FromBody] ScanHostedEventPassRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        // THE DOOR'S OWN PERMISSION, not the deciding one (item 235 phase 7). Scanning a pass used
        // to require whatever confirming a booking requires, which meant a steward with a phone
        // had to be trusted with the whole board — guests' addresses, dietary notes and all.
        if (!await CanRunTheDoorAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct))
            return NotFound();

        var token = request.Token?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            return Ok(Refused("Nothing was scanned. Try again, or look them up by name."));

        var pass = await db.HostedEventPasses
            .Include(p => p.HostedEventBooking).ThenInclude(b => b.LeadAppUser)
            .Include(p => p.HostedEventBooking).ThenInclude(b => b.Guests)
            .Include(p => p.HostedEventBooking).ThenInclude(b => b.Nights)
                .ThenInclude(n => n.HostedEventNight)
            .Include(p => p.HostedEventBooking).ThenInclude(b => b.Nights).ThenInclude(n => n.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
            .Include(p => p.HostedEventBooking).ThenInclude(b => b.HostedEvent)
            .FirstOrDefaultAsync(p => p.Token == token, ct);

        // Named, so the commonest real case — somebody showing last month's code — gets an answer
        // that ends the conversation instead of starting an argument.
        var otherEventName = pass is not null
                          && pass.HostedEventBooking.HostedEventId != eventId
            ? pass.HostedEventBooking.HostedEvent?.Name
            : null;

        if (EventPasses.WhyThisScanIsRefused(pass, eventId, otherEventName) is { } refusal)
            return Ok(Refused(refusal));

        var booking = pass!.HostedEventBooking;
        var alreadyIn = pass.CheckedInUtc;

        var now = DateTime.UtcNow;

        if (request.CheckIn && pass.CheckedInUtc is null)
        {
            // A scan the phone kept while it had no signal carries when it happened (phase 14c).
            pass.CheckedInUtc = await db.HostedEventNights.AsNoTracking()
                .Where(n => n.HostedEventId == eventId)
                .OrderBy(n => n.Date).Select(n => (DateTime?)n.Date).FirstOrDefaultAsync(ct) is { } firstNight
                ? DoorClock.ArrivedAt(request.ArrivedUtc, now, firstNight)
                : now;
            pass.CheckedInByAppUserId = userId.Value;
            pass.DateUpdated = now;
            pass.UpdatedByAppUserId = userId.Value;
        }

        // AND THE ARRIVAL ITSELF, FOR THIS NIGHT (item 235 phase 7, defect 19).
        //
        // The stamp above is one per pass and always was: it records that a party turned up, once,
        // over a three-night weekend. The row below is what lets the Saturday door know whether
        // the people in front of it were in on the Friday, and what answers "who was actually here
        // on the Saturday" afterwards. Both are kept — the stamp is history and rewriting it would
        // invent nights nobody came to.
        if (request.CheckIn && request.HostedEventNightId is { } nightId)
        {
            var here = booking.Nights.Any(n => n.HostedEventNightId == nightId && n.ReleasedUtc is null)
                    || booking.Nights.All(n => n.ReleasedUtc is not null);

            if (here && await db.HostedEventNights.AnyAsync(
                    n => n.Id == nightId && n.HostedEventId == eventId, ct))
            {
                var arrival = await db.HostedEventCheckIns.FirstOrDefaultAsync(
                    c => c.HostedEventBookingId == booking.Id
                      && c.HostedEventNightId == nightId, ct);

                var nightDate = await db.HostedEventNights.AsNoTracking()
                    .Where(n => n.Id == nightId).Select(n => n.Date).FirstAsync(ct);
                var arrivedAt = DoorClock.ArrivedAt(request.ArrivedUtc, now, nightDate);

                if (arrival is null)
                {
                    db.HostedEventCheckIns.Add(new HostedEventCheckIn
                    {
                        Id = Guid.NewGuid(),
                        HostedEventBookingId = booking.Id,
                        HostedEventNightId = nightId,
                        ArrivedUtc = arrivedAt,
                        Method = HostedEventCheckInMethod.Scanned,
                        RecordedByAppUserId = userId.Value,
                        DateCreated = now,
                        CreatedByAppUserId = userId.Value,
                    });
                }
                else
                {
                    // Walked back in from the car park. Keep the first arrival, clear the leaving — and
                    // when a scan kept offline is earlier than what was recorded since, the earlier wins.
                    if (arrivedAt < arrival.ArrivedUtc) arrival.ArrivedUtc = arrivedAt;
                    arrival.LeftUtc = null;
                    arrival.DateUpdated = now;
                    arrival.UpdatedByAppUserId = userId.Value;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        return Ok(new HostedEventScanResult(
            Admitted: true,
            Refusal: null,
            HostedEventBookingId: booking.Id,
            LeadName: booking.LeadAppUser?.DisplayName ?? "Somebody",
            PartySize: EventCapacity.ClampPartySize(booking.PartySize),
            Kind: booking.Kind,
            Nights: booking.Nights
                .OrderBy(n => n.HostedEventNight.Date)
                .Select(n => new HostedEventBookingNightRecord(
                    n.HostedEventNightId, n.HostedEventNight.Date, n.HostedEventLayoutUnitId, EventCapacity.NameOf(n, booking.Kind)))
                .ToList(),
            GuestNames: booking.Guests.OrderBy(g => g.SortOrder).Select(g => g.DisplayName).ToList(),
            AlreadyCheckedInUtc: alreadyIn));

        static HostedEventScanResult Refused(string why)
            => new(false, why, null, null, null, null, null, null, null);
    }

    // ── the work ─────────────────────────────────────────────────────────────

    private static Task<HostedEventBooking?> LoadBookingForPassAsync(
        BenDataContext db, Guid orgId, Guid eventId, Guid bookingId, CancellationToken ct)
        => db.HostedEventBookings
            .FirstOrDefaultAsync(b => b.Id == bookingId
                                   && b.HostedEventId == eventId
                                   && b.HostedEvent.OrganizationId == orgId, ct);

    /// <summary>One pass, as every screen reads it.</summary>
    internal static async Task<HostedEventPassRecord> PassRecordAsync(
        BenDataContext db, Guid passId, CancellationToken ct)
    {
        var pass = await db.HostedEventPasses.AsNoTracking()
            .Include(p => p.CheckedInByAppUser)
            .FirstAsync(p => p.Id == passId, ct);

        return ToRecord(pass);
    }

    /// <summary>
    /// The picture's address, built the same way everywhere.
    /// </summary>
    /// <remarks>
    /// Keyed by the TOKEN rather than the pass id, so the image URL a guest's browser caches stops
    /// working the moment the pass is replaced. A URL keyed by the pass id would go on serving the
    /// old code from a cache after a reissue, which is the one failure a reissue exists to prevent.
    /// </remarks>
    internal static string PassImageUrl(string token) => $"/api/public/event-passes/{token}.png";

    internal static HostedEventPassRecord ToRecord(HostedEventPass pass)
        => new(
            pass.Id,
            pass.HostedEventBookingId,
            pass.Token,
            PassImageUrl(pass.Token),
            pass.IssuedUtc,
            pass.RevokedUtc,
            pass.RevokedReason,
            pass.EmailedUtc,
            pass.CheckedInUtc,
            pass.CheckedInByAppUser?.DisplayName,
            pass.ReissuedFromHostedEventPassId is not null);

    /// <summary>
    /// Queues the invitation into the caller's context, and reports honestly whether it did.
    /// </summary>
    /// <remarks>
    /// <para>Unlike the public flow, the truth is told to the caller here. A host is not a
    /// stranger who might be probing for accounts; they are the person who will stand at a door
    /// wondering why nobody came, and "we could not send it" is exactly what they need to know.
    /// The invitation is saved either way, and the link is in the log.</para>
    ///
    /// <para>Not saved here: the caller's save writes the letter with the token it carries, so
    /// true means the letter is in that same write (item 239b).</para>
    ///
    /// <para>It deliberately says nothing about the room, the price or the programme. Those are
    /// the confirmation's to say, and this letter goes to an address nobody has proved yet.</para>
    /// </remarks>
    private async Task<bool> TryQueueInviteAsync(
        BenDataContext db, string email, HostedEvent ev, string token, CancellationToken ct)
    {
        var link = _site.AbsoluteUrl($"/attending/{token}");

        if (!_email.IsConfigured)
        {
            _logger.LogInformation(
                "Email is not configured; the invitation to hosted event {EventId} was not sent. "
              + "Link: {Link}", ev.Id, link);
            return false;
        }

        var safeName = NotificationText.Safe(ev.Name);
        try
        {
            await _outbox.EnqueueAsync(db, new Ben.Data.Common.Interfaces.EmailMessage(email,
                $"You're invited to {ev.Name}",
                $"<p>The venue has invited you to <strong>{safeName}</strong>, starting "
              + $"{ev.StartsOn:dddd, MMMM d}.</p>"
              + $"<p><a href=\"{link}\">Accept the invitation</a></p>"
              + "<p>Accepting puts your name in front of the venue, who will confirm your place "
              + "and tell you what happens next. That link is good for two weeks and only works "
              + "once.</p>"), ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Error, which the database log keeps: a Warning is how a letter that never went
            // stayed invisible before (item 239).
            _logger.LogError(ex,
                "Could not queue an invitation to hosted event {EventId}.", ev.Id);
            return false;
        }
    }

    private async Task<ActionResult<HostedEventBookingRecord>> DecideAsync(
        Guid orgId, Guid eventId, Guid bookingId,
        HostedEventBookingStatus status, string? note, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await CanDecideAsync(userId.Value, orgId, eventId, db, ct)) return Forbid();

        var booking = await LoadBookingAsync(db, eventId, bookingId, ct);
        if (booking is null) return NotFound();

        var now = DateTime.UtcNow;
        if (status == HostedEventBookingStatus.TurnedDown)
            BookingTransitions.TurnDown(booking, userId.Value, note, now);
        else
            BookingTransitions.Cancel(booking, userId.Value, note, now);

        booking.GuestAcknowledgedUtc = null;
        Touch(booking, userId.Value);

        // Whatever they held, they hold no longer. The umbrella row going is what makes every
        // existing count stop including them, which is the whole reason that row exists — and it
        // is written by the same call, so the two can never disagree.
        var releasedFrom = await db.HostedEvents
            .FirstOrDefaultAsync(e => e.Id == booking.HostedEventId, ct);
        if (releasedFrom is not null)
            await BookingTransitions.ApplyUmbrellaAsync(
                db, _sync, releasedFrom, booking, userId.Value, now, ct);

        // And the pass goes with it, in the same save. A guest holding a live code for a booking
        // that was cancelled is a guest a door waves through.
        await EventPasses.RevokeAllAsync(db, booking.Id, userId.Value,
            status == HostedEventBookingStatus.TurnedDown
                ? "The venue could not take this booking."
                : "The venue released this booking.", ct);

        // A guest who is not coming must be told exactly as reliably as one who is, which is why
        // this is the same call the confirmation makes rather than a second path that could quietly
        // stop being used — and why it commits with the decision in the same way (item 239b).
        await SaveInOneTransactionAndAuditAsync(db, booking, booking.Id, userId.Value,
            () => _guestMail.SendDecisionAsync(db, booking.Id, ct), ct);

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

        var offered = await db.HostedEventLayoutUnits
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

            // A night with no unit is somebody here for the day and going home again, which holds
            // nothing and so cannot be over capacity. It is how a three-night event sells its
            // Saturday on its own, and refusing it here would make that impossible to say.
            if (choice.HostedEventLayoutUnitId is not { } unitId) continue;

            var unit = offered.FirstOrDefault(u => u.Id == unitId);
            if (unit is null)
                return ev.LayoutKind == HostedEventLayoutKind.Seats
                    ? "That seat is not one on this event's plan. Add it to the plan first."
                    : "That room is not one this event is offering. Add it to the plan first.";

            var taken = EventCapacity.PeopleIn(all, choice.HostedEventNightId, unitId,
                                               excludingBookingId: booking.Id);
            if (EventCapacity.WhyThisRoomCannotTakeThem(
                    EventCapacity.NameOf(unit), night.Date, EventCapacity.CapacityOf(unit),
                    taken, party, ev.LayoutKind)
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
    /// <summary>
    /// Makes the booking's nights match what was chosen, keeping the rows that already agree.
    /// </summary>
    /// <remarks>
    /// <para><b>Reconciled, not replaced.</b> It used to delete every night row and write fresh
    /// ones. That threw away each row's id and its release history for no reason — and worse, the
    /// deleted rows were still tracked, so the transition that stamps "this row is holding" flipped
    /// one of them from Deleted to Modified and the save asked SQL Server to update a row it was
    /// deleting in the same batch. A 500, on the ordinary act of confirming a party who had picked
    /// their own seat.</para>
    ///
    /// <para>Matching on night AND unit, because a party may hold two rooms on one night since
    /// phase 4. A row that is still wanted keeps its identity; one that is not is removed; one that
    /// is new is added.</para>
    /// </remarks>
    private static void ReplaceNights(
        BenDataContext db, HostedEventBooking booking,
        IReadOnlyList<HostedEventBookingNightChoice> nights)
    {
        var wanted = nights
            .GroupBy(n => (n.HostedEventNightId, n.HostedEventLayoutUnitId))
            .Select(g => g.Last())
            .ToList();

        var keep = new HashSet<(Guid, Guid?)>(
            wanted.Select(w => (w.HostedEventNightId, w.HostedEventLayoutUnitId)));

        // Gone: rows nobody asked for any more.
        foreach (var row in booking.Nights.ToList())
        {
            if (keep.Contains((row.HostedEventNightId, row.HostedEventLayoutUnitId))) continue;
            booking.Nights.Remove(row);
            db.HostedEventBookingNights.Remove(row);
        }

        // New: the ones that are not already there.
        var already = booking.Nights
            .Select(n => (n.HostedEventNightId, n.HostedEventLayoutUnitId))
            .ToHashSet();

        foreach (var choice in wanted)
        {
            if (already.Contains((choice.HostedEventNightId, choice.HostedEventLayoutUnitId)))
                continue;

            var row = new HostedEventBookingNight
            {
                Id = Guid.NewGuid(),
                HostedEventBookingId = booking.Id,
                HostedEventNightId = choice.HostedEventNightId,
                HostedEventLayoutUnitId = choice.HostedEventLayoutUnitId,
                People = choice.People,
                DateCreated = DateTime.UtcNow,
            };
            // Added through the set, not only the collection: on a booking already in the database, a row found
            // through the navigation with its key already set is taken for an existing row, saved as an UPDATE of
            // nothing, and the whole confirmation fails.
            db.HostedEventBookingNights.Add(row);
            booking.Nights.Add(row);
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
            db.HostedEventBookingGuests.Add(row);   // through the set, for the reason ReplaceNights gives
            booking.Guests.Add(row);
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

    /// <summary>
    /// One booking, reloaded after a change. Always with the dietary notes, because every caller
    /// of this is somebody who has just decided a booking.
    /// </summary>
    private async Task<HostedEventBookingRecord> OneAsync(
        BenDataContext db, Guid eventId, Guid bookingId, CancellationToken ct)
    {
        var board = await BookingsAsync(db, eventId, canSeeDietary: true, ct);
        return board.First(b => b.Id == bookingId);
    }

    private static async Task<IReadOnlyList<HostedEventBookingRecord>> BookingsAsync(
        BenDataContext db, Guid eventId, bool canSeeDietary, CancellationToken ct)
    {
        var bookings = await db.HostedEventBookings
            .AsNoTracking()
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventLayoutUnit).ThenInclude(u => u!.PlaceRoom)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventNight)
            .Include(b => b.Guests)
            .Include(b => b.LeadAppUser)
            .Include(b => b.DecidedByAppUser)
            .Where(b => b.HostedEventId == eventId)
            .OrderBy(b => b.DateCreated)
            .ToListAsync(ct);

        // The event's bands, once, for the whole board — and how many nights it runs, which is
        // what "here every night" is measured against.
        var bands = await db.HostedEventBands.AsNoTracking()
            .Where(b => b.HostedEventId == eventId)
            .OrderBy(b => b.SortOrder)
            .ToListAsync(ct);
        var nightsOnTheEvent = await db.HostedEventNights.AsNoTracking()
            .CountAsync(n => n.HostedEventId == eventId, ct);

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
                    n.HostedEventLayoutUnitId, EventCapacity.NameOf(n, b.Kind)))
                .ToList(),
            b.Guests
                .OrderBy(g => g.SortOrder)
                .Select(g => new HostedEventBookingGuestRecord(
                    g.Id, g.DisplayName, g.AppUserId,
                    canSeeDietary ? g.DietaryNotes : null, g.SortOrder))
                .ToList(),
            b.HoldExpiresUtc,
            Band: EventBands.For(b, bands, nightsOnTheEvent) is { } band
                ? new HostedEventBandRecord(
                    band.Id, band.Colour, band.Meaning, band.Hex, band.Rule, band.SortOrder)
                : null,
            HandPickedBandId: b.HostedEventBandId,
            ContactPhone: b.ContactPhone))
            .ToList();
    }

    private async Task<HostedEventBookingBoardRecord> BoardAsync(
        BenDataContext db, HostedEvent ev, bool canSeeDietary, CancellationToken ct)
    {
        var offered = await db.HostedEventLayoutUnits
            .AsNoTracking()
            .Include(r => r.PlaceRoom)
            .Where(r => r.HostedEventId == ev.Id)
            // Sort order alone: it is what the designer wrote, and a seat has no room to fall back
            // on — ordering by the room's name would throw on the first Seats layout.
            .OrderBy(r => r.SortOrder)
            .ToListAsync(ct);

        var all = await AllBookingsAsync(db, ev.Id, ct);
        var nights = ev.Nights.OrderBy(n => n.Date).ToList();

        // THREE QUERIES, NOT HALF A MILLION LIST WALKS.
        //
        // This used to ask PeopleIn once per unit per night, and each call walked every booking on
        // the event and every night of each. Thirteen rows over three nights with two hundred
        // parties is roughly half a million traversals to paint one screen, repeated on every
        // render, all of it recomputing the same handful of numbers.
        var occupancy = await PlanOccupancy.ReadAsync(db, ev.Id, ct);
        var blocks = await PlanOccupancy.BlocksAsync(db, ev.Id, ct);

        var unitNights = new List<HostedEventUnitNightRecord>();
        foreach (var night in nights)
        {
            foreach (var unit in offered)
            {
                occupancy.TryGetValue((night.Id, unit.Id), out var cell);
                unitNights.Add(new HostedEventUnitNightRecord(
                    night.Id, night.Date, unit.Id, EventCapacity.NameOf(unit),
                    EventCapacity.CapacityOf(unit),
                    cell.People,
                    cell.Asked,
                    Pending: cell.HeldAs == HostedEventBookingStatus.Held,
                    NotOffered: EventCapacity.WhyItIsNotOffered(
                        blocks, unit.Id, night.Id, EventCapacity.NameOf(unit))));
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
            offered.Select(HostedEventController.ToUnitRecord).ToList(),
            unitNights,
            await BookingsAsync(db, ev.Id, canSeeDietary, ct),
            DayPassNights: DayPassesByNight(all, nights),
            BookingMode: ev.BookingMode,
            LapsedHolds: all.Count(b => b.Status == HostedEventBookingStatus.Held
                                     && b.HoldExpiresUtc <= DateTime.UtcNow));
    }

    /// <summary>Loads what the kitchen needs and hands it to <see cref="EventDietary"/> to read.</summary>
    /// <param name="night">
    /// One night of the event, or null for all of it. The rule for which parties are there on a
    /// night lives in <see cref="EventDietary.OnNight"/>, with the reasoning.
    /// </param>
    private static async Task<HostedEventDietaryRecord> DietaryAsync(
        BenDataContext db, Guid eventId, bool includeUnconfirmed, Guid? night, CancellationToken ct)
    {
        IReadOnlyList<HostedEventBooking> bookings = await db.HostedEventBookings
            .AsNoTracking()
            .Include(b => b.Guests)
            .Include(b => b.Nights).ThenInclude(n => n.HostedEventNight)
            .Include(b => b.LeadAppUser)
            .Where(b => b.HostedEventId == eventId)
            .ToListAsync(ct);

        if (night is { } only) bookings = EventDietary.OnNight(bookings, only);

        return EventDietary.Summarise(eventId, bookings, includeUnconfirmed);
    }

    /// <summary>
    /// Day passes per night, for a run where one night is the busy one.
    /// </summary>
    /// <remarks>
    /// One number for the whole event is right for a weekend somebody comes to once and wrong for a
    /// three-night run: a host catering Saturday needs Saturday's number, not the sum. The
    /// event-wide number stays as well, because it is what the capacity is set against.
    /// </remarks>
    private static List<HostedEventDayPassNightRecord> DayPassesByNight(
        IReadOnlyList<HostedEventBooking> bookings, IReadOnlyList<HostedEventNight> nights)
        => [.. nights.Select(night =>
        {
            var here = bookings
                .Where(b => b.Kind == HostedEventBookingKind.DayPass)
                .Where(b => b.Nights.Any(n => n.HostedEventNightId == night.Id
                                           && n.ReleasedUtc == null))
                .ToList();

            return new HostedEventDayPassNightRecord(
                night.Id, night.Date,
                here.Where(b => EventCapacity.Holds(b.Status)).Sum(b => Math.Max(1, b.PartySize)),
                here.Where(b => b.Status == HostedEventBookingStatus.Requested)
                    .Sum(b => Math.Max(1, b.PartySize)));
        })];

    // ── plumbing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// May this person decide who comes: confirm, turn down, release, edit, invite, issue passes.
    /// </summary>
    /// <remarks>
    /// It used to ask whether they could change the group's SETTINGS, because that was the only key
    /// wired up when the feature began — so the person who arranges the rooms had to be somebody
    /// who could also change the billing. Deciding who comes is its own job and now has its own key.
    /// </remarks>
    /// <param name="eventId">
    /// Which event, because from phase 7 an answer can come from the event's own staff list as
    /// well as from the group's roles — a weekend helper is nobody in the group and somebody at
    /// the door.
    /// </param>
    private Task<bool> CanDecideAsync(
        Guid userId, Guid orgId, Guid eventId, BenDataContext db, CancellationToken ct)
        => _access.CanDecideBookingsAsync(userId, orgId, eventId, db, ct);

    /// <summary>Run the door: scan, admit, mark somebody away again.</summary>
    /// <remarks>
    /// <b>Not the deciding permission.</b> The scan used to ask whether somebody could confirm a
    /// booking, which meant a steward with a phone had to be trusted with the whole board — the
    /// exact coupling the staff table exists to break.
    /// </remarks>
    private Task<bool> CanRunTheDoorAsync(
        Guid userId, Guid orgId, Guid eventId, BenDataContext db, CancellationToken ct)
        => _access.CanRunTheDoorAsync(userId, orgId, eventId, db, ct);

    /// <summary>
    /// May this person see guests' names, addresses and dietary notes.
    /// </summary>
    /// <remarks>
    /// Separate from deciding, and it is the check that was missing. Any member could read the
    /// board: a dietary note is a health disclosure somebody made to a venue so they would not be
    /// poisoned, and an address is theirs.
    /// </remarks>
    private Task<bool> CanReadBookingsAsync(
        Guid userId, Guid orgId, Guid eventId, BenDataContext db, CancellationToken ct)
        => _access.CanReadBookingsAsync(userId, orgId, eventId, db, ct);

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
