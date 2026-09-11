using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Tours;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Public events — the ones an organization opens to anybody, and the way a site user says they are
/// coming.
/// </summary>
/// <remarks>
/// <para>Ben's reason for this, and it shapes the design: <i>"These will benefit the organizations
/// because it is also an introduction to them by people attending... giving them the ability to
/// create open events might benefit us as well by increasing their numbers."</i> It is the first
/// surface on the platform that brings in somebody who has never heard of any of these groups, so
/// the organization is named prominently and the listing reads as an invitation rather than a
/// record.</para>
///
/// <para><b>The flag existed and did nothing.</b> <c>OrgCalendarEvent.IsPublic</c> has been stored
/// and settable since the calendar was built, and until now no endpoint anywhere read it. This is
/// the endpoint that makes it mean something.</para>
///
/// <para><b>A public event is never at a private residence.</b> Enforced where an event is made
/// public, not here — but restated in the read path's own filter so a row that somehow became
/// public against the rule still does not reach a visitor. Publishing a date and an address at
/// somebody's home is an invitation for strangers to turn up there, which is a sharper version of
/// the rule that already refuses <c>InvestigationVisibility.Public</c> for a residence.</para>
///
/// <para><b>The exact address is withheld at the projection.</b> When an event hides its location
/// until somebody is coming, a reader who is not coming receives a payload with no field for it —
/// never the address with a flag asking the client to hide it.</para>
/// </remarks>
[ApiController]
[Route("api/public/events")]
[Ben.Data.WebApi.Services.FeatureGated(Ben.Data.WebApi.Services.SiteSettingKeys.FeatureEvents)]
public sealed class PublicEventController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    /// <summary>
    /// Cleans the description on the way OUT, not only on the way in.
    /// </summary>
    /// <remarks>
    /// The save path sanitizes too, so nothing new can land dirty. This exists for everything that
    /// landed BEFORE it did: descriptions have been authored in a rich-text editor and stored raw
    /// since events were built, and this endpoint is <c>[AllowAnonymous]</c> — the markup is handed
    /// to every visitor. Cleaning here covers those rows without a migration that rewrites
    /// somebody's content, and it keeps the guarantee true even if a future write path forgets.
    /// The same reasoning as asking publication rules per request instead of caching them.
    /// </remarks>
    private readonly ICmsMarkupSanitizer _sanitizer;
    private readonly Services.Tours.TourGuestMailer _tourMail;

    public PublicEventController(
        IDbContextFactory<BenDataContext> db, ICmsMarkupSanitizer sanitizer,
        Services.Tours.TourGuestMailer tourMail)
    { _db = db; _sanitizer = sanitizer; _tourMail = tourMail; }

    // ── Reading ──────────────────────────────────────────────────────────────

    /// <summary>Upcoming public events, optionally narrowed to one organization.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PublicEventListItem>>> GetUpcoming(
        [FromQuery] string? orgUrlName, [FromQuery] int maxResults = 50, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        // Events that have ENDED are not upcoming. VisibleEvents deliberately carries no date
        // filter — a past event's own page must still resolve, or every link ever shared to one
        // breaks — so each listing adds its own, and this one had none. The result was that the
        // top of every public events list, on the website and in the app, was the OLDEST event
        // the group had ever run.
        //
        // End, not start: something happening right now is still worth showing somebody.
        var now = DateTime.UtcNow;
        var query = VisibleEvents(db).Where(e => e.EndDateTime >= now);

        if (!string.IsNullOrWhiteSpace(orgUrlName))
        {
            // Resolved to an id first rather than joined on the name: it picks up a retired address
            // as well as the current one, and filters on an indexed key instead of a string.
            var (org, _) = await OrganizationUrlNames.ResolveAsync(db, orgUrlName, ct);
            if (org is null) return Ok(Array.Empty<PublicEventListItem>());

            query = query.Where(e => e.OrganizationId == org.Id);
        }

        var events = await query
            .OrderBy(e => e.StartDateTime)
            .Take(Math.Clamp(maxResults, 1, 200))
            .Select(e => new
            {
                e.Id, e.UrlName, e.OrganizationId, OrgName = e.Organization.Name, OrgUrl = e.Organization.UrlName,
                e.Title, e.StartDateTime, e.EndDateTime, e.IsAllDay, e.MeetingUrl,
                e.AttendeeCapacity,
                TourName = e.Tour != null ? e.Tour.Name : null,
                TourUrl = e.Tour != null ? e.Tour.UrlName : null,
                // The clock the reader is shown. An event starts at eight where it happens, not
                // where the reader happens to be sitting — Ben's rule, 2026-09-10. The event's own
                // zone first; a tour date scheduled before events had one falls back to its
                // tour's; an event with neither is shown in UTC and told so.
                TourZone = e.TimeZoneId ?? (e.Tour != null ? e.Tour.TimeZoneId : null),
                // A tour's meeting point is advertised, so its pin is where it actually is —
                // the whole reason Ben wanted tours on the map.
                TourLat = e.Tour != null ? e.Tour.StartOrganizationAddress.Latitude : null,
                TourLon = e.Tour != null ? e.Tour.StartOrganizationAddress.Longitude : null,
                TourCity = e.Tour != null ? e.Tour.StartOrganizationAddress.City : null,
                TourState = e.Tour != null ? e.Tour.StartOrganizationAddress.State : null,
                PlaceCity = e.Place != null ? e.Place.City : null,
                PlaceState = e.Place != null ? e.Place.State : null,
                PlaceLat = e.Place != null ? e.Place.Latitude : null,
                PlaceLon = e.Place != null ? e.Place.Longitude : null,
                Attending = e.Attendees.Count(a => a.RsvpStatus == RsvpStatus.Accepted),
            })
            .ToListAsync(ct);

        return Ok(events.Select(e =>
        {
            // Approximate on the list for everybody, attendee or not. A discovery map is one map,
            // and a pin that sharpened for some readers would be a way to work out who is coming.
            // A TOUR is the exception, and not really an exception at all: its meeting point is
            // advertised on a leaflet, so blurring it would hide nothing and help nobody.
            // A tour's meeting point is advertised, so its pin is exact — but only when it has
            // one. An address that has not been geocoded used to return (null, null) here and
            // drop the walk off the map and out of the nearby search entirely, which is worse
            // than the approximate pin it would have had before tours existed.
            var (lat, lon) = e.TourLat is not null && e.TourLon is not null
                ? (e.TourLat, e.TourLon)
                : PublicCoordinates.Approximate(e.PlaceLat, e.PlaceLon);
            return new PublicEventListItem(
                e.Id, e.UrlName, e.OrganizationId, e.OrgName, e.OrgUrl, e.Title,
                e.StartDateTime, e.EndDateTime, e.IsAllDay,
                e.PlaceCity ?? e.TourCity, e.PlaceState ?? e.TourState, lat, lon,
                e.Attending, e.AttendeeCapacity,
                IsOnline: !string.IsNullOrWhiteSpace(e.MeetingUrl),
                TourName: e.TourName, TourUrlName: e.TourUrl, TimeZoneId: e.TourZone);
        }).ToList());
    }

    /// <summary>One public event, with as much of its location as this reader may have.</summary>
    [HttpGet("{eventId:guid}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicEventRecord>> GetEvent(Guid eventId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var ev = await VisibleEvents(db)
            .Include(e => e.Organization)
            .Include(e => e.Place)
            .Include(e => e.OrganizationAddress)
            .Include(e => e.Tour).ThenInclude(t => t!.StartOrganizationAddress)
            .Include(e => e.HostedEvent)
            .Include(e => e.Guides).ThenInclude(g => g.AppUser)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return NotFound();

        var userId = GetCurrentUserId();

        var attending = await db.OrgCalendarEventAttendees.AsNoTracking()
            .Where(a => a.OrgCalendarEventId == eventId)
            .ToListAsync(ct);

        // PLACES, not rows (item 234): a sign-up may hold more than one, and every row written
        // before this held exactly one, so the sum equals what this used to count.
        var acceptedCount = TourSeats.PlacesTaken(attending);
        var mine = userId != Guid.Empty
            ? attending.FirstOrDefault(a => a.AppUserId == userId)
            : null;
        var hasRsvpd = mine is { RsvpStatus: RsvpStatus.Accepted };

        // The organizer's own members always know where their event is.
        var isOrganizer = userId != Guid.Empty
            && await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == ev.OrganizationId && m.AppUserId == userId && m.IsActive, ct);

        // A tour's meeting point is public by nature; anything else keeps the old rule.
        var mayHaveExact = ev.TourId is not null || !ev.HideExactLocation || hasRsvpd || isOrganizer;

        var guideIds = ev.Guides.Select(g => g.AppUserId).ToList();
        var guidePhotos = await db.AppUserPhotos.AsNoTracking()
            .Where(p => guideIds.Contains(p.AppUserId) && p.IsPublic && p.IsActive)
            .Select(p => new { p.AppUserId, p.UploadFileId })
            .ToListAsync(ct);

        var rating = ev.TourId is { } ratedTourId
            ? await TourRatingAsync(db, ratedTourId, ct)
            : (null, 0);

        return Ok(new PublicEventRecord(
            ev.Id, ev.OrganizationId, ev.Organization.Name, ev.Organization.UrlName,
            ev.Title, _sanitizer.SanitizeHtml(ev.Description),
            ev.StartDateTime, ev.EndDateTime, ev.IsAllDay, ev.MeetingUrl,
            BuildLocation(ev, mayHaveExact),
            acceptedCount, ev.AttendeeCapacity, ev.RsvpClosesAt,
            BuildFlags(ev, userId, hasRsvpd, acceptedCount, mine?.SeatStatus),
            TourName: ev.Tour?.Name,
            TourUrlName: ev.Tour?.UrlName,
            Guides: [.. ev.Guides.OrderBy(g => g.SortOrder).Select(g => new PublicGuideRecord(
                g.AppUser.DisplayName ?? "A guide",
                g.AppUser.Handle,
                guidePhotos.FirstOrDefault(p => p.AppUserId == g.AppUserId)?.UploadFileId))],
            TourRating: rating.Item1,
            TourRatingCount: rating.Item2,
            // Their own seat, so a page can say "waiting on the business" rather than offering a
            // button that would do nothing (item 234). Null for somebody who has asked for nothing.
            MySeat: mine is null ? null : new PublicSeatRecord(
                mine.SeatStatus, Math.Max(1, mine.Seats), mine.SeatDecidedUtc, mine.GuestAcknowledgedUtc),
            // See the list projection: the event's own clock, then its tour's, then UTC.
            TimeZoneId: ev.TimeZoneId ?? ev.Tour?.TimeZoneId,
            // Additive (item 235): a reader that knows what a hosted event is asks for the rest —
            // the separate dates, the venue's own name — from the public hosted-event endpoint. One
            // that does not sees an ordinary public event, which is what the umbrella is for.
            HostedEventId: ev.HostedEventId,
            HostedEventName: ev.HostedEvent != null ? ev.HostedEvent.Name : null));
    }

    /// <summary>
    /// One public event by its readable URL — <c>/o/{org}/events/{slug}</c>.
    /// </summary>
    /// <remarks>
    /// The route people actually share. Resolves the slug to an id and hands off, so there is one
    /// projection rather than two that could disagree about what a visitor may see.
    /// </remarks>
    [HttpGet("~/api/public/organizations/{orgUrlName}/events/{eventSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicEventRecord>> GetEventBySlug(
        string orgUrlName, string eventSlug, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var slug = Ben.Data.Common.SlugText.NormalizeOrEmpty(eventSlug);

        var (org, _) = await OrganizationUrlNames.ResolveAsync(db, orgUrlName, ct);
        if (org is null) return NotFound();

        var id = await VisibleEvents(db)
            .Where(e => e.OrganizationId == org.Id && e.UrlName == slug)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefaultAsync(ct);

        return id is Guid eventId ? await GetEvent(eventId, ct) : NotFound();
    }

    /// <summary>
    /// The public events this caller has said they are coming to.
    /// </summary>
    /// <remarks>
    /// Without this, saying you are coming to something is a statement that vanishes: the RSVP
    /// creates an <c>OrgCalendarEventAttendee</c>, and <c>/my-investigations</c> reads
    /// <c>InvestigationAttendee</c> — a different table — so nothing anywhere afterwards told a
    /// person what they had signed up for.
    ///
    /// <para>Recently-past events are included rather than dropped on the day. Somebody checking
    /// "what was that place called?" the morning after has nowhere else to look, and a list that
    /// empties itself the moment an event ends is the kind of tidiness nobody asked for.</para>
    /// </remarks>
    [HttpGet("mine")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<PublicEventListItem>>> GetMine(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var since = DateTime.UtcNow.AddDays(-30);

        var events = await VisibleEvents(db)
            .Where(e => e.EndDateTime >= since
                     && e.Attendees.Any(a => a.AppUserId == userId && a.RsvpStatus == RsvpStatus.Accepted))
            .OrderBy(e => e.StartDateTime)
            .Select(e => new
            {
                e.Id, e.UrlName, e.OrganizationId, OrgName = e.Organization.Name, OrgUrl = e.Organization.UrlName,
                e.Title, e.StartDateTime, e.EndDateTime, e.IsAllDay, e.MeetingUrl,
                e.AttendeeCapacity,
                TourName = e.Tour != null ? e.Tour.Name : null,
                TourUrl = e.Tour != null ? e.Tour.UrlName : null,
                // The clock the reader is shown. An event starts at eight where it happens, not
                // where the reader happens to be sitting — Ben's rule, 2026-09-10. The event's own
                // zone first; a tour date scheduled before events had one falls back to its
                // tour's; an event with neither is shown in UTC and told so.
                TourZone = e.TimeZoneId ?? (e.Tour != null ? e.Tour.TimeZoneId : null),
                // A tour's meeting point is advertised, so its pin is where it actually is —
                // the whole reason Ben wanted tours on the map.
                TourLat = e.Tour != null ? e.Tour.StartOrganizationAddress.Latitude : null,
                TourLon = e.Tour != null ? e.Tour.StartOrganizationAddress.Longitude : null,
                TourCity = e.Tour != null ? e.Tour.StartOrganizationAddress.City : null,
                TourState = e.Tour != null ? e.Tour.StartOrganizationAddress.State : null,
                PlaceCity = e.Place != null ? e.Place.City : null,
                PlaceState = e.Place != null ? e.Place.State : null,
                PlaceLat = e.Place != null ? e.Place.Latitude : null,
                PlaceLon = e.Place != null ? e.Place.Longitude : null,
                Attending = e.Attendees.Count(a => a.RsvpStatus == RsvpStatus.Accepted),
            })
            .ToListAsync(ct);

        return Ok(events.Select(e =>
        {
            // A tour's meeting point is advertised, so its pin is exact — but only when it has
            // one. An address that has not been geocoded used to return (null, null) here and
            // drop the walk off the map and out of the nearby search entirely, which is worse
            // than the approximate pin it would have had before tours existed.
            var (lat, lon) = e.TourLat is not null && e.TourLon is not null
                ? (e.TourLat, e.TourLon)
                : PublicCoordinates.Approximate(e.PlaceLat, e.PlaceLon);
            return new PublicEventListItem(
                e.Id, e.UrlName, e.OrganizationId, e.OrgName, e.OrgUrl, e.Title,
                e.StartDateTime, e.EndDateTime, e.IsAllDay,
                e.PlaceCity ?? e.TourCity, e.PlaceState ?? e.TourState, lat, lon,
                e.Attending, e.AttendeeCapacity,
                IsOnline: !string.IsNullOrWhiteSpace(e.MeetingUrl),
                TourName: e.TourName, TourUrlName: e.TourUrl, TimeZoneId: e.TourZone);
        }).ToList());
    }

    // ── Coming along ─────────────────────────────────────────────────────────

    /// <summary>
    /// Says this caller is coming.
    /// </summary>
    /// <remarks>
    /// Requires an account, per Ben: it is the line between browsing and attending, and it is what
    /// makes an attendee somebody the organization can actually reach. Idempotent — pressing it
    /// twice is the same statement, not two people.
    /// </remarks>
    [HttpPost("{eventId:guid}/rsvp")]
    [Authorize]
    public async Task<ActionResult<PublicEventRecord>> Rsvp(
        Guid eventId, CancellationToken ct, [FromQuery] int? seats = null)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var ev = await VisibleEvents(db).Include(e => e.Tour)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return NotFound();

        // Item 233. Pausing a tour is documented as stopping sign-ups, and the calendar refuses a
        // new date for a paused one — but nothing here read it, so every date already on the
        // calendar kept taking guests and sending them a meeting point for a walk that is not
        // running. Retired is the same answer for a stronger reason.
        if (WhyTourIsNotTakingSignUps(ev) is { } tourClosed) return Conflict(tourClosed);

        var attendees = await db.OrgCalendarEventAttendees
            .Where(a => a.OrgCalendarEventId == eventId)
            .ToListAsync(ct);

        // ── A tour date asks; everything else simply comes (item 234) ────────
        // Ben: "They would not be confirmed until the tour guide or manager approves them meaning
        // they have settled how money will be or has been exchanged." So on a tour date this
        // endpoint records a REQUEST — Invited, with the seat waiting — and the places are held
        // only when the business approves. Every other kind of event keeps the rule it had.
        var isTour = TourSeats.IsTourDate(ev);
        var wanted = TourSeats.Clamp(seats);

        var existing = attendees.FirstOrDefault(a => a.AppUserId == userId);

        // Already settled: an accepted seat, or a request already waiting on the business. Pressing
        // the button again is the same statement, not a second guest.
        if (existing is { RsvpStatus: RsvpStatus.Accepted }
         || existing is { SeatStatus: TourSeatStatus.Requested })
            return await GetEvent(eventId, ct);

        if (DateTime.UtcNow > ev.RsvpClosingTime)
            return Conflict("Sign-ups for this event have closed.");

        // Counted in PLACES and excluding this caller's own row, so somebody re-accepting after
        // cancelling is not refused by a seat they are not occupying.
        //
        // A tour date is NOT refused here even when it is full: a request holds nothing, and the
        // overflow is a waiting list the business works through rather than a closed door.
        var taken = TourSeats.PlacesTaken(attendees, excludingAppUserId: userId);
        if (!isTour && ev.AttendeeCapacity is int cap && taken >= cap)
            return Conflict("This event is full.");

        if (existing is not null)
        {
            existing.RsvpStatus = isTour ? RsvpStatus.Invited : RsvpStatus.Accepted;
            existing.SeatStatus = isTour ? TourSeatStatus.Requested : null;
            existing.Seats      = isTour ? wanted : 1;
            existing.DateRsvp   = DateTime.UtcNow;
            existing.SeatDecidedUtc = null;
            existing.SeatDecidedByAppUserId = null;
            existing.GuestAcknowledgedUtc = null;
        }
        else
        {
            db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
            {
                Id                 = Guid.NewGuid(),
                OrgCalendarEventId = eventId,
                AppUserId          = userId,
                RsvpStatus         = isTour ? RsvpStatus.Invited : RsvpStatus.Accepted,
                SeatStatus         = isTour ? TourSeatStatus.Requested : null,
                Seats              = isTour ? wanted : 1,
                DateRsvp           = DateTime.UtcNow,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            });
        }

        await db.SaveChangesAsync(ct);

        // Item 233: a tour date sends the business's own welcome, with the walk attached as a
        // calendar file. This path sent NOTHING before — a signed-in guest pressed "I'm coming"
        // and heard from us again only in the reminder the night before, if at all. Non-tour
        // events are untouched: the mailer answers to a tour or does nothing.
        //
        // Item 234 moves that mail to the moment of APPROVAL for a tour date. "You're signed up,
        // here is where to stand" is untrue of a request nobody has looked at, and a guest who
        // acted on it would turn up to a walk that had never reserved them a place.
        if (!isTour
            && await db.AppUsers.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Email, u.DisplayName })
                .FirstOrDefaultAsync(ct) is { Email: { Length: > 0 } address } guest)
        {
            await _tourMail.SendSignUpAsync(db, eventId, address, guest.DisplayName, ct);
        }

        return await GetEvent(eventId, ct);
    }

    /// <summary>
    /// The guest saying back that they know the seat is theirs (item 234).
    /// </summary>
    /// <remarks>
    /// <para>Ben: <i>"the person who is touring can confirm it on the app - if they want."</i>
    /// <b>Optional, always.</b> Nothing is withheld for want of it, no reminder waits on it and no
    /// place is released without it — it is one of them telling the other they saw it, and the
    /// business gets to see that their guest is expecting to be there.</para>
    ///
    /// <para>Only a seat that has actually been reserved can be acknowledged. Acknowledging a
    /// request nobody has approved would be the guest confirming something to themselves.</para>
    /// </remarks>
    [HttpPost("{eventId:guid}/my-seat/acknowledge")]
    [Authorize]
    public async Task<ActionResult<PublicEventRecord>> AcknowledgeSeat(Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var seat = await db.OrgCalendarEventAttendees
            .FirstOrDefaultAsync(a => a.OrgCalendarEventId == eventId && a.AppUserId == userId, ct);
        if (seat is null) return NotFound();

        // Reserved OR turned down — any seat the business has ANSWERED.
        //
        // Reserved-only was the first rule and it left a refusal counting on the guest's bell for
        // ever: nothing could clear it, because the only thing that clears it is this. A guest
        // saying "got it" to bad news is exactly as reasonable as saying it to good news, and it
        // is the difference between a notification and a permanent mark.
        if (seat.SeatStatus is not (TourSeatStatus.Reserved or TourSeatStatus.TurnedDown))
            return Conflict("The tour hasn't answered this one yet.");

        // Idempotent: pressing it twice is the same statement, and the FIRST time is the one worth
        // keeping — that is when they saw it.
        seat.GuestAcknowledgedUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await GetEvent(eventId, ct);
    }

    /// <summary>
    /// Says this caller is no longer coming.
    /// </summary>
    /// <remarks>
    /// Stops the address being served again. It cannot un-tell somebody who already read it, and
    /// pretending otherwise would be dishonest — but leaving it available to a cancelled attendee
    /// would make cancelling meaningless.
    /// </remarks>
    [HttpDelete("{eventId:guid}/rsvp")]
    [Authorize]
    public async Task<IActionResult> CancelRsvp(Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var attendee = await db.OrgCalendarEventAttendees
            .FirstOrDefaultAsync(a => a.OrgCalendarEventId == eventId && a.AppUserId == userId, ct);
        if (attendee is null) return NotFound();

        attendee.RsvpStatus = RsvpStatus.Declined;
        attendee.DateRsvp   = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return NoContent();
    }

    // ── Plumbing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The events a visitor may see at all.
    /// </summary>
    /// <remarks>
    /// The residence rule is restated here rather than trusted from the write path. Making an event
    /// public already refuses a case-linked or residence event; repeating the filter on read means
    /// a row that became public some other way — a script, a migration, a bug — still never reaches
    /// anybody. Cheap, and the failure it guards against is the one nobody would notice.
    /// </remarks>
    /// <summary>
    /// The one definition of which events a visitor may see. Internal so the nearby search reuses
    /// it rather than restating it — a second copy of this rule is the copy that drifts.
    /// </summary>
    /// <summary>A tour's average stars and how many left them. Zero ratings answer null.</summary>
    /// <remarks>
    /// Rounded to one decimal at the source so the website, the phone and any future reader agree
    /// about what "4.7" means rather than each rounding the same average differently.
    /// </remarks>
    internal static async Task<(decimal?, int)> TourRatingAsync(
        BenDataContext db, Guid tourId, CancellationToken ct)
    {
        var stars = await db.TourReviews.AsNoTracking()
            .Where(r => r.TourId == tourId && r.HiddenAtUtc == null)
            .Select(r => r.Stars)
            .ToListAsync(ct);

        return stars.Count == 0
            ? (null, 0)
            : (Math.Round((decimal)stars.Average(), 1, MidpointRounding.AwayFromZero), stars.Count);
    }

    internal static IQueryable<OrgCalendarEvent> VisibleEvents(BenDataContext db)
        => db.OrgCalendarEvents.AsNoTracking()
            .Where(e => e.IsPublic
                     && e.CaseId == null
                     && (e.Place == null || e.Place.Kind == PlaceKind.PublicLocation));

    private static PublicEventLocationRecord BuildLocation(OrgCalendarEvent ev, bool mayHaveExact)
    {
        var city  = ev.Tour?.StartOrganizationAddress?.City ?? ev.Place?.City ?? ev.OrganizationAddress?.City;
        var state = ev.Tour?.StartOrganizationAddress?.State ?? ev.Place?.State ?? ev.OrganizationAddress?.State;

        // A tour's own page and its card both draw the exact meeting point, and the street
        // address is printed a line above this — so blurring the detail pin hid nothing and made
        // the two views disagree about where the same walk starts.
        var (lat, lon) =
            ev.Tour?.StartOrganizationAddress is { Latitude: { } tourLat, Longitude: { } tourLon }
                ? ((decimal?)tourLat, (decimal?)tourLon)
                : PublicCoordinates.Approximate(
                    ev.Place?.Latitude ?? ev.OrganizationAddress?.Latitude,
                    ev.Place?.Longitude ?? ev.OrganizationAddress?.Longitude);

        var exact = mayHaveExact ? ExactAddressOf(ev) : null;

        return new PublicEventLocationRecord(
            city, state, lat, lon,
            ExactAddress: exact,
            IsExactAddressHidden: ev.HideExactLocation && !mayHaveExact);
    }

    private static string? ExactAddressOf(OrgCalendarEvent ev)
    {
        // A tour date meets where its tour starts, whatever else the date carries. The tour is
        // the thing with a meeting point; the date is only when it happens.
        if (ev.Tour?.StartOrganizationAddress is { } start)
            return string.Join(", ", new[]
            {
                start.StreetAddress1, start.StreetAddress2, start.City, start.State, start.ZipCode,
            }.Where(p => !string.IsNullOrWhiteSpace(p)));

        var parts = new[]
        {
            ev.Place?.StreetAddress1 ?? ev.OrganizationAddress?.StreetAddress1,
            ev.Place?.StreetAddress2 ?? ev.OrganizationAddress?.StreetAddress2,
            ev.Place?.City ?? ev.OrganizationAddress?.City,
            ev.Place?.State ?? ev.OrganizationAddress?.State,
            ev.Place?.ZipCode ?? ev.OrganizationAddress?.ZipCode,
        }.Where(p => !string.IsNullOrWhiteSpace(p));

        var address = string.Join(", ", parts);

        // Free text is the fallback, and often the only thing an organizer wrote — "the car park
        // behind the church" is a real answer that no address table will ever hold.
        return string.IsNullOrWhiteSpace(address)
            ? (string.IsNullOrWhiteSpace(ev.Location) ? null : ev.Location)
            : address;
    }

    /// <summary>
    /// Why this date's tour is not taking sign-ups, or null.
    /// </summary>
    /// <remarks>
    /// One sentence in one place, asked by the button, by the RSVP and by the confirmation link,
    /// so a guest cannot be told two different things by two different doors.
    /// </remarks>
    internal static string? WhyTourIsNotTakingSignUps(OrgCalendarEvent ev)
        => ev.Tour is null ? null
         : ev.Tour.RetiredAtUtc is not null
            ? "This tour is no longer running."
         : !ev.Tour.IsBookable
            ? "This tour isn't taking sign-ups just now."
         : null;

    private static PublicEventFlags BuildFlags(
        OrgCalendarEvent ev, Guid userId, bool hasRsvpd, int acceptedCount,
        TourSeatStatus? mySeat = null)
    {
        var isFull    = ev.AttendeeCapacity is int cap && acceptedCount >= cap;
        // Asked for and waiting on the business (item 234). Not "coming" — nothing is held yet —
        // but the button must not be offered again, and the reason must say what is happening
        // rather than leaving somebody pressing a control that answers with silence.
        var waiting   = mySeat == TourSeatStatus.Requested;
        var turnedDown = mySeat == TourSeatStatus.TurnedDown;
        // The SAME rule the sign-up endpoints enforce. When this said "closed" and the endpoint
        // still accepted, the button vanished from a tour a guest could legitimately still join.
        var hasClosed = DateTime.UtcNow > ev.RsvpClosingTime;
        // Same rule again, from the same method the endpoints call.
        var tourClosed = WhyTourIsNotTakingSignUps(ev);

        // A tour date that is full still TAKES requests — the overflow is a waiting list the
        // business works through, and it is the business who decides. So fullness blocks the
        // button on an ordinary event and only warns on a tour date.
        var fullStops = isFull && !TourSeats.IsTourDate(ev);

        var reason =
              hasRsvpd          ? null
            : waiting           ? "Your seat is with the tour — they'll confirm it."
            : turnedDown        ? "The tour couldn't take this booking."
            : tourClosed        ?? (
              userId == Guid.Empty ? "Sign in to say you're coming."
            : hasClosed         ? "Sign-ups for this event have closed."
            : fullStops         ? "This event is full."
            : isFull            ? "This date is full — ask anyway and the tour will let you know."
            : null);

        return new PublicEventFlags(
            CanRsvp: userId != Guid.Empty && !hasRsvpd && !waiting && !turnedDown
                  && !hasClosed && !fullStops && tourClosed is null,
            HasRsvpd: hasRsvpd,
            IsFull: isFull,
            RsvpHasClosed: hasClosed,
            RsvpBlockedReason: reason);
    }
}
