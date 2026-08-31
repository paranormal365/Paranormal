using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Ben.Data.WebApi.Services;

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

    public PublicEventController(IDbContextFactory<BenDataContext> db, ICmsMarkupSanitizer sanitizer)
    { _db = db; _sanitizer = sanitizer; }

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
            var (lat, lon) = PublicCoordinates.Approximate(e.PlaceLat, e.PlaceLon);
            return new PublicEventListItem(
                e.Id, e.UrlName, e.OrganizationId, e.OrgName, e.OrgUrl, e.Title,
                e.StartDateTime, e.EndDateTime, e.IsAllDay,
                e.PlaceCity, e.PlaceState, lat, lon,
                e.Attending, e.AttendeeCapacity,
                IsOnline: !string.IsNullOrWhiteSpace(e.MeetingUrl));
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
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return NotFound();

        var userId = GetCurrentUserId();

        var attending = await db.OrgCalendarEventAttendees.AsNoTracking()
            .Where(a => a.OrgCalendarEventId == eventId)
            .ToListAsync(ct);

        var acceptedCount = attending.Count(a => a.RsvpStatus == RsvpStatus.Accepted);
        var mine = userId != Guid.Empty
            ? attending.FirstOrDefault(a => a.AppUserId == userId)
            : null;
        var hasRsvpd = mine is { RsvpStatus: RsvpStatus.Accepted };

        // The organizer's own members always know where their event is.
        var isOrganizer = userId != Guid.Empty
            && await db.OrganizationUserMemberships.AsNoTracking()
                .AnyAsync(m => m.OrganizationId == ev.OrganizationId && m.AppUserId == userId && m.IsActive, ct);

        var mayHaveExact = !ev.HideExactLocation || hasRsvpd || isOrganizer;

        return Ok(new PublicEventRecord(
            ev.Id, ev.OrganizationId, ev.Organization.Name, ev.Organization.UrlName,
            ev.Title, _sanitizer.SanitizeHtml(ev.Description),
            ev.StartDateTime, ev.EndDateTime, ev.IsAllDay, ev.MeetingUrl,
            BuildLocation(ev, mayHaveExact),
            acceptedCount, ev.AttendeeCapacity, ev.RsvpClosesAt,
            BuildFlags(ev, userId, hasRsvpd, acceptedCount)));
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
                PlaceCity = e.Place != null ? e.Place.City : null,
                PlaceState = e.Place != null ? e.Place.State : null,
                PlaceLat = e.Place != null ? e.Place.Latitude : null,
                PlaceLon = e.Place != null ? e.Place.Longitude : null,
                Attending = e.Attendees.Count(a => a.RsvpStatus == RsvpStatus.Accepted),
            })
            .ToListAsync(ct);

        return Ok(events.Select(e =>
        {
            var (lat, lon) = PublicCoordinates.Approximate(e.PlaceLat, e.PlaceLon);
            return new PublicEventListItem(
                e.Id, e.UrlName, e.OrganizationId, e.OrgName, e.OrgUrl, e.Title,
                e.StartDateTime, e.EndDateTime, e.IsAllDay,
                e.PlaceCity, e.PlaceState, lat, lon,
                e.Attending, e.AttendeeCapacity,
                IsOnline: !string.IsNullOrWhiteSpace(e.MeetingUrl));
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
    public async Task<ActionResult<PublicEventRecord>> Rsvp(Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var ev = await VisibleEvents(db).FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return NotFound();

        var attendees = await db.OrgCalendarEventAttendees
            .Where(a => a.OrgCalendarEventId == eventId)
            .ToListAsync(ct);

        var existing = attendees.FirstOrDefault(a => a.AppUserId == userId);
        if (existing is { RsvpStatus: RsvpStatus.Accepted })
            return await GetEvent(eventId, ct);

        if (DateTime.UtcNow > ev.RsvpClosingTime)
            return Conflict("Sign-ups for this event have closed.");

        // Counted excluding this caller's own row, so somebody re-accepting after cancelling is not
        // refused by a seat they are not occupying.
        var accepted = attendees.Count(a => a.RsvpStatus == RsvpStatus.Accepted && a.AppUserId != userId);
        if (ev.AttendeeCapacity is int cap && accepted >= cap)
            return Conflict("This event is full.");

        if (existing is not null)
        {
            existing.RsvpStatus = RsvpStatus.Accepted;
            existing.DateRsvp   = DateTime.UtcNow;
        }
        else
        {
            db.OrgCalendarEventAttendees.Add(new OrgCalendarEventAttendee
            {
                Id                 = Guid.NewGuid(),
                OrgCalendarEventId = eventId,
                AppUserId          = userId,
                RsvpStatus         = RsvpStatus.Accepted,
                DateRsvp           = DateTime.UtcNow,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            });
        }

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
    internal static IQueryable<OrgCalendarEvent> VisibleEvents(BenDataContext db)
        => db.OrgCalendarEvents.AsNoTracking()
            .Where(e => e.IsPublic
                     && e.CaseId == null
                     && (e.Place == null || e.Place.Kind == PlaceKind.PublicLocation));

    private static PublicEventLocationRecord BuildLocation(OrgCalendarEvent ev, bool mayHaveExact)
    {
        var city  = ev.Place?.City ?? ev.OrganizationAddress?.City;
        var state = ev.Place?.State ?? ev.OrganizationAddress?.State;

        var (lat, lon) = PublicCoordinates.Approximate(
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

    private static PublicEventFlags BuildFlags(
        OrgCalendarEvent ev, Guid userId, bool hasRsvpd, int acceptedCount)
    {
        var isFull    = ev.AttendeeCapacity is int cap && acceptedCount >= cap;
        // The SAME rule the sign-up endpoints enforce. When this said "closed" and the endpoint
        // still accepted, the button vanished from a tour a guest could legitimately still join.
        var hasClosed = DateTime.UtcNow > ev.RsvpClosingTime;

        var reason =
            hasRsvpd            ? null
            : userId == Guid.Empty ? "Sign in to say you're coming."
            : hasClosed         ? "Sign-ups for this event have closed."
            : isFull            ? "This event is full."
            : null;

        return new PublicEventFlags(
            CanRsvp: userId != Guid.Empty && !hasRsvpd && !hasClosed && !isFull,
            HasRsvpd: hasRsvpd,
            IsFull: isFull,
            RsvpHasClosed: hasClosed,
            RsvpBlockedReason: reason);
    }
}
