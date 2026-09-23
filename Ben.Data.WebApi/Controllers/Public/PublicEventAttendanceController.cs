using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Tours;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Coming along to a public event without already having an account.
/// </summary>
/// <remarks>
/// <para>Ben: <i>"Someone may give some information, but we need enough to be able to show them
/// they have elected to attend if not already users of our site."</i></para>
///
/// <para><b>Why a link rather than just taking the address.</b> An email typed into a box proves
/// nothing, and events that hide their location until somebody is coming would be protecting
/// nothing at all if anyone could type an address and be shown where a group is meeting. Sending a
/// link and requiring the click is the cheapest gate that verifies anything.</para>
///
/// <para><b>Why it creates a real account.</b> A guest record would leave nobody behind, and the
/// stated purpose of public events is that they introduce a group to new people. Confirming makes a
/// passwordless account: they <i>are</i> a site user, they simply never had to invent a password,
/// and setting one later is an upgrade rather than a requirement.</para>
///
/// <para><b>Enumeration.</b> Asking to come always answers the same way, whether or not the address
/// already belongs to somebody. Otherwise this endpoint would be a way of testing which email
/// addresses have accounts here.</para>
/// </remarks>
[ApiController]
[Route("api/public/event-attendance")]
[Ben.Data.WebApi.Services.FeatureGated(Ben.Data.WebApi.Services.SiteSettingKeys.FeatureEvents)]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Ben.Data.WebApi.Services.RateLimiting.EventAttendancePolicy)]
public sealed class PublicEventAttendanceController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IEmailService _email;

    /// <summary>
    /// Where the "confirm you're coming" letter goes: into the same save as the token it carries
    /// (item 239b).
    /// </summary>
    private readonly Ben.Data.WebApi.Services.IOutboxEmailQueue _outbox;
    private readonly UserManager<AppUser> _users;
    private readonly Ben.Data.WebApi.Services.UserHandleService _handles;
    private readonly Ben.Data.Common.SiteIdentity _site;
    private readonly Ben.Data.WebApi.Services.Tours.TourGuestMailer _tourMail;
    private readonly ILogger<PublicEventAttendanceController> _logger;

    /// <summary>A link is good for a fortnight — long enough to act on, short enough to expire.</summary>
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(14);

    /// <summary>
    /// How many invitations one event may issue per seat before it is refusing rather than filling.
    /// </summary>
    /// <remarks>
    /// Well above one, because not everybody who asks turns up and an organiser would rightly be
    /// furious at a cap that stopped a tour selling out. Three per seat means a thirty-guest walk
    /// can hand out ninety links before anything is questioned, which no real evening reaches and
    /// no mailer is satisfied by.
    /// </remarks>
    internal const int InviteCeilingMultiple = 3;

    /// <summary>
    /// The bound for an event that never stated a capacity, and the floor under the multiple.
    /// </summary>
    /// <remarks>
    /// Most events set no capacity, so without a floor the multiple would have nothing to multiply
    /// and the guard would be unreachable exactly where it is most needed. Five hundred is chosen
    /// to be beyond any single tour, talk or public hunt this site hosts while still being a number.
    /// </remarks>
    internal const int InviteCeilingFloor = 500;

    public PublicEventAttendanceController(
        IDbContextFactory<BenDataContext> db, IEmailService email, UserManager<AppUser> users,
        IOptions<Ben.Data.Common.SiteIdentity> site, ILogger<PublicEventAttendanceController> logger,
        Ben.Data.WebApi.Services.UserHandleService handles,
        Ben.Data.WebApi.Services.Tours.TourGuestMailer tourMail,
        Ben.Data.WebApi.Services.IOutboxEmailQueue outbox)
    {
        _outbox = outbox;
        _tourMail = tourMail;
        _handles = handles;
        _db     = db;
        _email  = email;
        _users  = users;
        _site   = site.Value;
        _logger = logger;
    }

    /// <summary>
    /// Asks to come to a public event, giving an email address rather than signing in.
    /// </summary>
    [HttpPost("{eventId:guid}/request")]
    [AllowAnonymous]
    public async Task<IActionResult> RequestAttendance(
        Guid eventId, [FromBody] RequestEventAttendanceRequest request, CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 320)
            return BadRequest("A valid email address is needed.");

        await using var db = await _db.CreateDbContextAsync(ct);

        var ev = await VisiblePublicEvent(db).FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return NotFound();

        // One rule, not two: an explicit RsvpClosesAt is the organiser's decision, and otherwise
        // sign-ups run to the start plus the late-arrival grace. Refusing on StartDateTime as well
        // made the grace unreachable — a late guest who is standing at the meeting point could not
        // sign up, and so could not submit what they photographed on the walk.
        if (DateTime.UtcNow > ev.RsvpClosingTime)
            return Conflict("Sign-ups for this event have closed.");

        // A hosted event (item 235) wears an umbrella calendar row, and its own rules are the ones
        // that answer here: called off, unpublished, past its deadline, or not selling day passes.
        // Each refusal says what to do instead rather than leaving somebody hunting for a phone
        // number the page may not carry.
        var hosted = await HostedEventGuestDoor.BehindAsync(db, ev, ct);
        if (hosted is not null
            && HostedEventGuestDoor.WhyTheEmailDoorIsClosed(hosted, DateTime.UtcNow) is { } shut)
            return Conflict(shut);

        // Item 235 slice 11d: a hosted event's organizer has to be able to reach whoever asks, so
        // the name and a phone are required here and nowhere else on this door.
        if (hosted is not null
            && BookingContact.WhyNotEnough(request.FirstName, request.LastName, request.Phone) is { } missing)
            return BadRequest(missing);

        // Counted in PLACES (item 234), and a TOUR date is not refused for fullness at all: a
        // request holds nothing, so the overflow is a waiting list the business works through.
        // A hosted event is the same and for the same reason — the venue decides, and refusing a
        // request because the house is full would close the very waiting list the host wants.
        var held = TourSeats.PlacesTaken(await db.OrgCalendarEventAttendees.AsNoTracking()
            .Where(a => a.OrgCalendarEventId == eventId).ToListAsync(ct));
        if (!TourSeats.IsTourDate(ev) && hosted is null
            && ev.AttendeeCapacity is int cap && held >= cap)
            return Conflict("This event is full.");

        // Reuse the pending row for a repeat request rather than accumulating one per attempt —
        // somebody who did not receive the first email will simply ask again.
        var invite = await db.EventAttendanceInvites
            .FirstOrDefaultAsync(i => i.OrgCalendarEventId == eventId && i.Email == email, ct);

        if (invite is { DateConfirmed: not null })
            return Ok();   // already coming; say nothing that distinguishes the case

        // ── The mailer guard (item 199) ──────────────────────────────────────
        // This endpoint sends an email to any address typed into it, so the per-caller rate limit
        // is the wrong instrument: a crowd of thirty guests at a meeting point and one attacker
        // with a list arrive from the same NAT'd address and are identical to the limiter. That is
        // why the per-caller limit here is deliberately generous, and why the real ceiling is this
        // one — an event that has issued far more invitations than it could ever seat is not
        // hosting a rush, it is being used as a mailer.
        //
        // Only NEW addresses count. Somebody re-requesting their own link takes the branch above
        // and never reaches here, so a guest whose first email went to spam is never the person
        // this refuses. Capacity is the honest measure when it is set, and an event with no stated
        // capacity still gets a bound rather than none.
        if (invite is null)
        {
            var issued = await db.EventAttendanceInvites
                .CountAsync(i => i.OrgCalendarEventId == eventId, ct);
            var ceiling = ev.AttendeeCapacity is int seats
                ? Math.Max(seats * InviteCeilingMultiple, InviteCeilingFloor)
                : InviteCeilingFloor;

            if (issued >= ceiling)
            {
                _logger.LogWarning(
                    "Event {EventId} has issued {Issued} attendance invitations against a ceiling of "
                    + "{Ceiling}; refusing further requests. Raise the event's capacity if this is a "
                    + "genuinely large event.", eventId, issued, ceiling);
                return Conflict("Sign-ups for this event are temporarily unavailable.");
            }
        }

        var token = NewToken();

        if (invite is null)
        {
            invite = new EventAttendanceInvite
            {
                Id                 = Guid.NewGuid(),
                OrgCalendarEventId = eventId,
                Email              = email,
                DisplayName        = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
                FirstName          = hosted is null ? null : BookingContact.Trimmed(request.FirstName),
                LastName           = hosted is null ? null : BookingContact.Trimmed(request.LastName),
                Phone              = hosted is null ? null : BookingContact.Trimmed(request.Phone),
                // On a tour date and on a hosted event, where the number asked for is part of the
                // ask. Every other event seats one person per sign-up (item 234).
                Seats              = TourSeats.IsTourDate(ev) || hosted is not null
                                         ? TourSeats.Clamp(request.Seats) : null,
                Token              = token,
                DateExpires        = DateTime.UtcNow.Add(LinkLifetime),
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = Guid.Empty,
            };
            db.EventAttendanceInvites.Add(invite);
        }
        else
        {
            invite.Token       = token;
            invite.DateExpires = DateTime.UtcNow.Add(LinkLifetime);
            invite.DateUpdated = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(request.DisplayName)) invite.DisplayName = request.DisplayName.Trim();
            if (hosted is not null)
            {
                invite.FirstName = BookingContact.Trimmed(request.FirstName);
                invite.LastName  = BookingContact.Trimmed(request.LastName);
                invite.Phone     = BookingContact.Trimmed(request.Phone);
            }
            // Asking again with a different party size is the newer answer, not a second guest.
            if (TourSeats.IsTourDate(ev) || hosted is not null)
                invite.Seats = TourSeats.Clamp(request.Seats);
        }

        // The letter joins THIS save (item 239b). Asking again rotates the token, which kills the
        // link in any letter already sent; saving the new token and writing its letter afterwards
        // meant a letter that failed to write left somebody with a dead link and nothing to replace
        // it. Now the token and the letter carrying it are one write, or neither is.
        await TryQueueAsync(db, email, ev, token, ct);
        await db.SaveChangesAsync(ct);

        // Always 200, whether or not that address already has an account and whether or not the mail
        // actually went. Anything else turns this into an account-existence oracle.
        return Ok();
    }

    /// <summary>What a confirmation link points at, before it is used.</summary>
    [HttpGet("{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<EventAttendanceInviteInfo>> GetInvite(string token, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var invite = await db.EventAttendanceInvites.AsNoTracking()
            .Include(i => i.OrgCalendarEvent).ThenInclude(e => e.Organization)
            .FirstOrDefaultAsync(i => i.Token == token, ct);

        if (invite is null || invite.DateExpires < DateTime.UtcNow) return NotFound();

        return Ok(new EventAttendanceInviteInfo(
            invite.OrgCalendarEventId,
            invite.OrgCalendarEvent.Title,
            invite.OrgCalendarEvent.Organization.Name,
            invite.OrgCalendarEvent.Organization.UrlName,
            invite.OrgCalendarEvent.UrlName,
            invite.OrgCalendarEvent.StartDateTime,
            invite.Email));
    }

    /// <summary>
    /// Uses the link: confirms the address, makes an account if there is not one, and records that
    /// they are coming.
    /// </summary>
    /// <remarks>
    /// The token is cleared in the same save, so a forwarded email cannot hand the address to a
    /// mailing list. Everything here is one transaction — an account created without the attendance
    /// it was created for would be the worst outcome, because nothing would tell anybody it had
    /// happened.
    /// </remarks>
    [HttpPost("{token}/confirm")]
    [AllowAnonymous]
    public async Task<ActionResult<EventAttendanceConfirmation>> Confirm(
        string token,
        [FromServices] Services.Events.EventGuestMailer hostedMail,
        [FromServices] Services.EmailLinkAccounts accounts,
        CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var invite = await db.EventAttendanceInvites
            .Include(i => i.OrgCalendarEvent).ThenInclude(e => e.Organization)
            // The tour, so the paused/retired check below has something to read.
            .Include(i => i.OrgCalendarEvent).ThenInclude(e => e.Tour)
            .FirstOrDefaultAsync(i => i.Token == token, ct);

        if (invite is null || invite.DateExpires < DateTime.UtcNow)
            return NotFound();

        var ev = invite.OrgCalendarEvent;

        // Re-checked at the moment of use, not only when the link was sent. A fortnight is long
        // enough for an event to fill up or close.
        //
        // Unless an organiser sent it. A guide signing up a group who turned up late is making
        // that call in person, and refusing their link at the moment it is used would recreate
        // exactly the failure the late-arrival grace exists to prevent — the guest is standing
        // there, has paid, and would still lose the photograph they took on the walk. A link the
        // guest asked for themselves gets no such latitude, because nobody vouched for it.
        if (invite.InvitedByAppUserId is null && DateTime.UtcNow > ev.RsvpClosingTime)
            return Conflict("Sign-ups for this event have closed.");

        // Item 233: a tour that has been paused or retired since the link was sent takes nobody,
        // whoever sent it. An organiser's latitude is about somebody standing in front of them on
        // a night the walk is running; it is not a way to join a walk that is not.
        if (PublicEventController.WhyTourIsNotTakingSignUps(ev) is { } tourClosed)
            return Conflict(tourClosed);

        // Item 235: the same re-check for a hosted event, whose own state is the one that answers.
        // A host's own link keeps its latitude over the deadline and over nothing else — an event
        // called off since the letter went out takes nobody.
        var hosted = await HostedEventGuestDoor.BehindAsync(db, ev, ct);
        if (hosted is not null
            && HostedEventGuestDoor.WhyTheEmailDoorIsClosed(
                   hosted, DateTime.UtcNow, invite.InvitedByAppUserId is not null) is { } shut)
            return Conflict(shut);

        var attendees = await db.OrgCalendarEventAttendees
            .Where(a => a.OrgCalendarEventId == ev.Id)
            .ToListAsync(ct);

        var user = await accounts.FindOrCreateAsync(
            invite.Email, invite.DisplayName, invite.FirstName, invite.LastName, ct);
        if (user is null) return BadRequest("That account could not be created.");

        // ── A tour date asks; everything else simply comes (item 234) ────────
        // Confirming the emailed link proves the address. On a TOUR date it does not also reserve
        // a place: the guide or manager approves that, which is where the business says the money
        // is settled. Every other kind of event keeps the rule it had.
        //
        // A HOSTED event is a tour date in this one respect (item 235, DECISION 7): the venue
        // decides, so the click asks and holds nothing. Writing an accepted row here would have
        // the public count, the reminder and the phone all saying somebody had a place at a
        // weekend the host had never looked at.
        var isTour = TourSeats.IsTourDate(ev);
        var asksRatherThanComes = isTour || hosted is not null;
        var wantedSeats = TourSeats.Clamp(invite.Seats);

        // Capacity is likewise the organiser's own number, and likewise theirs to override in
        // person — the direct AddAttendee path has never checked it either. A guest who asked for
        // their own link still cannot walk past a full house. Counted in PLACES, and never applied
        // to a tour date, where confirming holds nothing.
        var alreadyFull = !asksRatherThanComes
            && invite.InvitedByAppUserId is null
            && ev.AttendeeCapacity is int cap
            && TourSeats.PlacesTaken(attendees, excludingAppUserId: user.Id) >= cap;
        if (alreadyFull) return Conflict("This event filled up before you confirmed.");

        var attendee = attendees.FirstOrDefault(a => a.AppUserId == user.Id);
        if (attendee is null)
        {
            db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
            {
                Id                 = Guid.NewGuid(),
                OrgCalendarEventId = ev.Id,
                AppUserId          = user.Id,
                RsvpStatus         = asksRatherThanComes ? RsvpStatus.Invited : RsvpStatus.Accepted,
                SeatStatus         = asksRatherThanComes ? TourSeatStatus.Requested : null,
                Seats              = asksRatherThanComes ? wantedSeats : 1,
                DateRsvp           = DateTime.UtcNow,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = user.Id,
            });
        }
        else
        {
            // A confirmed hosted booking already holds this row, and its Accepted/Reserved pair is
            // what every count on the site reads. A second link clicked afterwards must not quietly
            // demote a place the venue has already agreed to.
            var alreadyDecided = hosted is not null && attendee.SeatStatus == TourSeatStatus.Reserved;
            if (!alreadyDecided)
            {
                attendee.RsvpStatus = asksRatherThanComes ? RsvpStatus.Invited : RsvpStatus.Accepted;
                attendee.SeatStatus = asksRatherThanComes ? TourSeatStatus.Requested : null;
                attendee.Seats      = asksRatherThanComes ? wantedSeats : 1;
                attendee.DateRsvp   = DateTime.UtcNow;
            }
        }

        // Item 235: the click is the ask. One live booking per person per event, so a second link
        // adds nothing — and the host decides this from their own board, which is the only place
        // a day pass starts counting.
        HostedEventBooking? asked = null;
        if (hosted is not null)
        {
            asked = await HostedEventGuestDoor.AddDayPassRequestAsync(
                db, hosted, user.Id, invite.Seats, note: null, ct);
            if (asked is not null) asked.ContactPhone = invite.Phone;
            if (invite.FirstName is { } first && invite.LastName is { } last)
                await BookingContact.FillEmptyNamesAsync(
                    db, user.Id, new BookingContact.Details(first, last, invite.Phone ?? ""), ct);
        }

        invite.DateConfirmed        = DateTime.UtcNow;
        invite.ConfirmedByAppUserId = user.Id;
        invite.Token                = null;   // single use
        invite.DateUpdated          = DateTime.UtcNow;

        // One transaction over the attendance AND the letter about it (item 239b). Two saves
        // inside it: SendAskedAsync reads the booking back by id, so the row has to exist before
        // it runs, and the letter it queues is only written by the second save. Either both land
        // or neither — a guest told "nothing is held yet" for a request that rolled back is as
        // wrong as a request with no letter.
        //
        // Only when there IS such a letter. Without a hosted request this path is exactly what it
        // was: one save, then the tour's welcome below — which goes through IEmailService and so
        // writes on a connection of its own. Inside a transaction that would be a second writer
        // waiting on the first, for no gain, since there is nothing here for it to be atomic with.
        // The two never meet today (a hosted event always asks rather than comes, so the welcome
        // is skipped whenever `asked` is set); this keeps it that way if either rule moves.
        //
        // IsRelational, like MyProfileController: InMemory has no transactions and throws.
        await using var tx = asked is not null && db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;

        await db.SaveChangesAsync(ct);

        // Item 233: now that they are actually coming, the tour's own welcome — with the walk as
        // a calendar file. The mail before this one was a one-line "confirm you're coming" sent
        // to an address nobody had proved yet; this is the first point at which there is somebody
        // to write to.
        //
        // Item 234 holds that mail back on a TOUR date until the business approves the seat. "Here
        // is where to stand" is untrue of a request nobody has looked at, and a guest who acted on
        // it would turn up to a walk that had never reserved them a place.
        //
        // A hosted event holds it back for the same reason and sends its own in phase 2.3: the
        // tour's welcome talks about a meeting point and a guide, and a weekend at a hotel has
        // neither.
        if (!asksRatherThanComes)
            await _tourMail.SendSignUpAsync(db, ev.Id, invite.Email, invite.DisplayName ?? user.DisplayName, ct);

        // A hosted event's own acknowledgement (item 235 phase 6). The page they land on says the
        // same thing, but a link clicked on a phone in a car park is a page nobody reads twice,
        // and "nothing is held yet" is the part that must survive being half-read.
        if (asked is not null)
        {
            await hostedMail.SendAskedAsync(db, asked.Id, ct);
            await db.SaveChangesAsync(ct);
        }

        if (tx is not null) await tx.CommitAsync(ct);

        return Ok(new EventAttendanceConfirmation(
            ev.Id, ev.Title, ev.Organization.Name, ev.Organization.UrlName, ev.UrlName,
            ev.StartDateTime,
            HostedEventId: hosted?.Id,
            PartySize: wantedSeats,
            // Asked rather than assumed: an email-link account has no password, but this link may
            // equally have been clicked by somebody who has had one for years, and offering to set
            // a password to them reads as a warning that something is wrong with their account.
            AccountHasNoPassword: !await _users.HasPasswordAsync(user),
            // A tour date's seat waits for the business (item 234); a hosted event says so its own way.
            AwaitsApproval: isTour));
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Public events a stranger may ask to attend — the same rule the public read path applies.
    /// </summary>
    private static IQueryable<OrgCalendarEvent> VisiblePublicEvent(BenDataContext db)
        => db.OrgCalendarEvents.AsNoTracking()
            .Where(e => e.IsPublic
                     && e.CaseId == null
                     && (e.Place == null || e.Place.Kind == PlaceKind.PublicLocation));

    /// <summary>256 bits, URL-safe. Guessing one must not be a way in.</summary>
    private static string NewToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    /// <summary>
    /// Queues the confirmation link into the caller's context, and treats a failure as non-fatal.
    /// </summary>
    /// <remarks>
    /// <para>Not saved here: the caller's save writes the letter with the token it carries (item
    /// 239b).</para>
    ///
    /// <para>The caller must not fail because of the letter, or asking to attend would break
    /// entirely the moment mail was misconfigured — and nothing here may tell it whether the letter
    /// went, for the reason given where it is caught.</para>
    /// </remarks>
    private async Task TryQueueAsync(
        BenDataContext db, string email, OrgCalendarEvent ev, string token, CancellationToken ct)
    {
        // Queued whether or not mail is set up: it waits in the outbox until it is, readable at
        // /admin/mail meanwhile. Said, without the token or link — used by anybody, it confirms
        // attendance in this person's name and burns their own copy (NoCredentialsInLogsTests).
        if (!_email.IsConfigured)
            _logger.LogInformation(
                "Email is not configured; the attendance link for {Email} to event {EventId} is waiting in the outbox.",
                email, ev.Id);

        var link = _site.AbsoluteUrl($"/attending/{token}");
        var safeTitle = NotificationText.Safe(ev.Title);

        try
        {
            await _outbox.EnqueueAsync(db, new EmailMessage(email,
                $"Confirm you're coming to {ev.Title}",
                $"<p>You said you'd like to come to <strong>{safeTitle}</strong> on "
                + $"{ev.StartDateTime:dddd, MMMM d}.</p>"
                + $"<p><a href=\"{link}\">Confirm you're coming</a></p>"
                + "<p>That link is good for two weeks, and only works once. If this wasn't you, "
                + "nothing happens unless you click it.</p>"), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged rather than surfaced: telling the caller the send failed would also tell them
            // the address exists, and there is nothing they could do about it either way. Error
            // rather than Warning, because the database log keeps Error and above.
            _logger.LogError(ex, "Could not queue an event attendance link to {Email}.", email);
        }
    }
}
