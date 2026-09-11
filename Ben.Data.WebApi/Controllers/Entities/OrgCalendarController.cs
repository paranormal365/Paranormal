using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Tours;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>Calendar event types CRUD (org admin only).</summary>
[ApiController]
[Route("api/organizations/{orgId:guid}/calendar-event-types")]
[Authorize]
[Ben.Data.WebApi.Services.FeatureGated(Ben.Data.WebApi.Services.SiteSettingKeys.FeatureEvents)]
public sealed class OrgCalendarEventTypeController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public OrgCalendarEventTypeController(IDbContextFactory<BenDataContext> db, IMapper mapper,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    { _db = db; _mapper = mapper; _security = security; }

    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    /// <summary>Phase B additive gate (item 156): the old admin rule OR a Calendar role grant.</summary>
    private async Task<bool> IsAdminOrHasAsync(
        Guid orgId, OrganizationSecurityAction action, CancellationToken ct)
        => await IsOrgAdminAsync(orgId, ct)
        || await _security.HasAccessAsync(GetCurrentUserId(), orgId,
               OrganizationSecurityTable.OrgCalendar, action, ct);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrgCalendarEventTypeRecord>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var types = await db.OrgCalendarEventTypes.AsNoTracking()
            .Where(t => t.OrganizationId == orgId)
            .OrderBy(t => t.SortOrder).ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<OrgCalendarEventTypeRecord>>(types));
    }

    [HttpPost]
    public async Task<ActionResult<OrgCalendarEventTypeRecord>> Create(
        Guid orgId, [FromBody] UpsertCalendarEventTypeRequest request, CancellationToken ct)
    {
        if (!await IsAdminOrHasAsync(orgId, OrganizationSecurityAction.Create, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = new OrgCalendarEventType
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            Name = request.Name.Trim(), ColorClass = request.ColorClass?.Trim(),
            IconClass = request.IconClass?.Trim(), SortOrder = request.SortOrder,
            IsActive = request.IsActive, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.OrgCalendarEventTypes.Add(entity);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetAll), new { orgId },
            _mapper.Map<OrgCalendarEventTypeRecord>(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<OrgCalendarEventTypeRecord>> Update(
        Guid orgId, Guid id, [FromBody] UpsertCalendarEventTypeRequest request, CancellationToken ct)
    {
        if (!await IsAdminOrHasAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.OrgCalendarEventTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();
        entity.Name = request.Name.Trim(); entity.ColorClass = request.ColorClass?.Trim();
        entity.IconClass = request.IconClass?.Trim(); entity.SortOrder = request.SortOrder;
        entity.IsActive = request.IsActive; entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        return Ok(_mapper.Map<OrgCalendarEventTypeRecord>(entity));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid id, CancellationToken ct)
    {
        if (!await IsAdminOrHasAsync(orgId, OrganizationSecurityAction.Delete, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.OrgCalendarEventTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();
        db.OrgCalendarEventTypes.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<bool> IsOrgMemberAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgMemberAsync(db, orgId, userId, ct);
    }

    private async Task<bool> IsOrgAdminAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgAdminAsync(db, orgId, userId, ct);
    }
}

/// <summary>Calendar events CRUD + attendee management.</summary>
[ApiController]
[Route("api/organizations/{orgId:guid}/calendar")]
[Authorize]
[Ben.Data.WebApi.Services.FeatureGated(Ben.Data.WebApi.Services.SiteSettingKeys.FeatureEvents)]
public sealed class OrgCalendarEventController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    private readonly Ben.Data.Common.Interfaces.IEmailService _email;
    private readonly Ben.Data.Common.SiteIdentity _site;
    private readonly ILogger<OrgCalendarEventController> _logger;

    /// <summary>
    /// Event descriptions are authored in a rich-text editor and shown on a PUBLIC page, so the
    /// markup is cleaned before it is stored — the same rule the CMS and publication controllers
    /// follow.
    /// </summary>
    private readonly ICmsMarkupSanitizer _sanitizer;

    public OrgCalendarEventController(IDbContextFactory<BenDataContext> db, IMapper mapper,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security,
        Ben.Data.Common.Interfaces.IEmailService email,
        Microsoft.Extensions.Options.IOptions<Ben.Data.Common.SiteIdentity> site,
        ILogger<OrgCalendarEventController> logger,
        ICmsMarkupSanitizer sanitizer,
        Ben.Data.WebApi.Services.Tours.TourGuestMailer tourMail)
    { _db = db; _mapper = mapper;  _security = security; _email = email; _site = site.Value; _logger = logger; _sanitizer = sanitizer; _tourMail = tourMail; }

    /// <summary>
    /// The tour's own welcome, sent when a seat is APPROVED rather than when it is asked for
    /// (item 234).
    /// </summary>
    private readonly Ben.Data.WebApi.Services.Tours.TourGuestMailer _tourMail;

    /// <summary>Cleans a description, preserving "no description" as null rather than "".</summary>
    private string? CleanDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description)) return null;
        var cleaned = _sanitizer.SanitizeHtml(description).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? null : cleaned;
    }

    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    /// <summary>Phase B additive gate (item 156): the old admin rule OR a Calendar role grant.
    /// Event create/update/delete are member-open already and stay that way — this covers the
    /// two attendee-management spots that were admin-only.</summary>
    private async Task<bool> IsAdminOrHasAsync(Guid orgId, CancellationToken ct)
        => await IsOrgAdminAsync(orgId, ct)
        || await _security.HasAccessAsync(GetCurrentUserId(), orgId,
               OrganizationSecurityTable.OrgCalendar, OrganizationSecurityAction.Update, ct);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrgCalendarEventRecord>>> GetAll(
        Guid orgId, [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var query = db.OrgCalendarEvents.AsNoTracking()
            .Include(e => e.EventType)
            .Include(e => e.Case)
            .Include(e => e.Attendees).Include(e => e.OrganizationAddress)
            .Include(e => e.Tour).Include(e => e.Guides).ThenInclude(g => g.AppUser)
            .Where(e => e.OrganizationId == orgId);

        if (from.HasValue) query = query.Where(e => e.EndDateTime >= from.Value);
        if (to.HasValue)   query = query.Where(e => e.StartDateTime <= to.Value);

        var events = await query.OrderBy(e => e.StartDateTime).ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<OrgCalendarEventRecord>>(events));
    }

    [HttpGet("{eventId:guid}")]
    public async Task<ActionResult<OrgCalendarEventRecord>> GetById(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var exists = await db.OrgCalendarEvents.AsNoTracking()
            .AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        return exists ? Ok(await ProjectAsync(db, eventId, ct)) : NotFound();
    }

    [HttpPost]
    public async Task<ActionResult<OrgCalendarEventRecord>> Create(
        Guid orgId, [FromBody] UpsertCalendarEventRequest request, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = new OrgCalendarEvent
        {
            Id = Guid.NewGuid(), OrganizationId = orgId,
            EventTypeId = request.EventTypeId, CaseId = request.CaseId,
            Title = request.Title.Trim(), Description = CleanDescription(request.Description),
            Location = request.Location?.Trim(),
            OrganizationAddressId = request.OrganizationAddressId,
            MeetingUrl = NormaliseUrl(request.MeetingUrl),
            StartDateTime = request.StartDateTime, EndDateTime = request.EndDateTime,
            IsAllDay = request.IsAllDay, IsPublic = request.IsPublic,
            PlaceId = request.PlaceId,
            HideExactLocation = request.HideExactLocation,
            AttendeeCapacity = request.AttendeeCapacity,
            TimeZoneId = Trimmed(request.TimeZoneId),
            RsvpClosesAt = request.RsvpClosesAt,
            RecurrenceRule = request.RecurrenceRule?.Trim(),
            TourId = request.TourId,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };

        if (ZoneRefusal(entity.TimeZoneId) is string zoneRefusal)
            return BadRequest(zoneRefusal);

        if (entity.IsPublic
            && await PublicEventRefusalAsync(db, entity.CaseId, entity.PlaceId, ct) is string refusal)
            return BadRequest(refusal);

        if (await TourDateRefusalAsync(db, orgId, entity, ct) is string tourRefusal)
            return BadRequest(tourRefusal);

        await ApplyTourDefaultsAsync(db, entity, request, ct);

        await EnsurePublicSlugAsync(db, entity, ct);

        db.OrgCalendarEvents.Add(entity);
        await db.SaveChangesAsync(ct);

        if (await SetGuidesAsync(db, orgId, entity, request, userId, seedFromTour: true, ct) is string guideRefusal)
        {
            // Nothing has been promised to anybody yet, so the cleanest answer to a bad guide is
            // to undo the date rather than leave one nobody is leading.
            db.OrgCalendarEvents.Remove(entity);
            await db.SaveChangesAsync(ct);
            return BadRequest(guideRefusal);
        }

        return CreatedAtAction(nameof(GetById), new { orgId, eventId = entity.Id },
            await ProjectAsync(db, entity.Id, ct));
    }


    /// <summary>
    /// Whether this event may be made public, or the reason it may not.
    /// </summary>
    /// <remarks>
    /// <para><b>A public event is never at somebody's home.</b> A listing with a date and an address
    /// is an invitation for strangers to turn up, which is a sharper version of the rule that
    /// already refuses <c>InvestigationVisibility.Public</c> for a private residence — and there is
    /// still no mechanism for asking a client to agree to it.</para>
    ///
    /// <para>Two signals, because either alone leaves a gap. A <b>case</b> is a client engagement and
    /// is at their address by default, whether or not a place was ever recorded. A <b>place</b> says
    /// what kind of location it is outright. An event with neither is the organization publishing
    /// about a venue of their own choosing, which is theirs to publish.</para>
    ///
    /// <para>The reasons are written to be shown to a person: an organizer who ticks the box and
    /// gets a bare refusal has learned nothing about what to do instead.</para>
    /// </remarks>
    private static async Task<string?> PublicEventRefusalAsync(
        BenDataContext db, Guid? caseId, Guid? placeId, CancellationToken ct)
    {
        if (caseId is not null)
            return "An event attached to a case can't be made public — a case is somebody's home, "
                 + "and publishing when people will be there isn't ours or yours to decide. "
                 + "Remove the case link, or create a separate public event for the venue.";

        if (placeId is Guid id)
        {
            var kind = await db.Places.AsNoTracking()
                .Where(p => p.Id == id).Select(p => (PlaceKind?)p.Kind).FirstOrDefaultAsync(ct);

            if (kind is null)
                return "That location could not be found.";

            if (kind == PlaceKind.PrivateResidence)
                return "That location is a private residence, so this event can't be made public. "
                     + "Public events are for landmarks, businesses, and your own addresses.";
        }

        return null;
    }


    /// <summary>
    /// Why this date cannot run as asked, or null.
    /// </summary>
    /// <remarks>
    /// <para><b>A public date of a tour business belongs to a tour</b> (item 233). The tour is
    /// what the business pays for, so a public date without one would be a tour run without being
    /// counted — and, more to the point, a guest would be signing up to something with no meeting
    /// point, no length and nobody named as its guide.</para>
    /// <para>Groups that do not run tours are untouched: their calendar is what it always was.</para>
    /// </remarks>
    private static async Task<string?> TourDateRefusalAsync(
        BenDataContext db, Guid orgId, OrgCalendarEvent entity, CancellationToken ct)
    {
        var org = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == orgId)
            .Select(o => new { o.Kind, o.RunsPublicTours })
            .FirstOrDefaultAsync(ct);
        if (org is null) return null;

        var runsTours = org.RunsPublicTours || org.Kind == OrganizationKind.GhostWalkingTour;

        if (entity.TourId is null)
            return runsTours && entity.IsPublic
                ? "A public date belongs to one of your tours. Set the tour up first — its meeting "
                + "point, how long it runs and who guides it are what a guest is told — then "
                + "schedule this date under it."
                : null;

        var tour = await db.Tours.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == entity.TourId && t.OrganizationId == orgId, ct);
        if (tour is null)
            return "That tour isn't one of yours.";
        if (tour.RetiredAtUtc is not null)
            return $"\"{tour.Name}\" is retired, so it takes no new dates. Bring it back first if "
                 + "you are running it again.";
        if (!tour.IsBookable && entity.IsPublic)
            return $"\"{tour.Name}\" is paused, so it isn't taking sign-ups. Un-pause it to put a "
                 + "public date on the calendar.";

        return null;
    }

    /// <summary>Why this zone cannot be saved, or null when it can.</summary>
    /// <remarks>
    /// Checked against what the machine actually resolves rather than against the short list the
    /// screens offer: a business in a zone nobody thought to list must not be locked out, and a
    /// typo must not be stored as a clock that silently reads as UTC forever.
    /// </remarks>
    private static string? ZoneRefusal(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        try { TimeZoneInfo.FindSystemTimeZoneById(id); return null; }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return $"\"{id}\" isn't a time zone this server knows. Pick one from the list.";
        }
    }

    /// <summary>Null for anything blank, so an empty box is stored as "nobody said".</summary>
    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Fills in from the tour whatever the date did not say.</summary>
    /// <remarks>
    /// The meeting point, the length and the size of a group are properties of the tour, and
    /// re-typing them per date is how three dates of one tour end up meeting in three places.
    /// An explicit value on the request always wins.
    /// </remarks>
    private static async Task ApplyTourDefaultsAsync(
        BenDataContext db, OrgCalendarEvent entity, UpsertCalendarEventRequest request, CancellationToken ct)
    {
        if (entity.TourId is null) return;
        var tour = await db.Tours.AsNoTracking().FirstOrDefaultAsync(t => t.Id == entity.TourId, ct);
        if (tour is null) return;

        entity.OrganizationAddressId ??= tour.StartOrganizationAddressId;
        entity.AttendeeCapacity ??= tour.DefaultCapacity;

        // The clock too. A walk meets where the tour starts, so it runs on the tour's zone unless
        // the date says otherwise — and taking a copy rather than reading through to the tour
        // means a date already advertised does not silently move when the tour is edited.
        entity.TimeZoneId ??= tour.TimeZoneId;

        if (tour.DurationMinutes is { } minutes && entity.EndDateTime <= entity.StartDateTime)
            entity.EndDateTime = entity.StartDateTime.AddMinutes(minutes);
    }

    /// <summary>Sets who leads a date, or the refusal naming who cannot.</summary>
    private static async Task<string?> SetGuidesAsync(
        BenDataContext db, Guid orgId, OrgCalendarEvent entity,
        UpsertCalendarEventRequest request, Guid userId, bool seedFromTour, CancellationToken ct)
    {
        var wanted = request.GuideAppUserIds?.Distinct().ToList();

        // A new date of a tour inherits the tour's guides when it says nothing, because that is
        // almost always right and a date with nobody on it tells a guest nothing.
        //
        // Narrowed to CURRENT members on the way through. Nothing removes a guide from a tour
        // when they leave the group, so an inherited list could name somebody who is no longer a
        // member — and the check below would then refuse every new date on that tour with
        // "invite them first", about a person who has left. The tour's list is stale, not the
        // date's, and refusing the date is the wrong place to complain about it.
        if (wanted is null && seedFromTour && entity.TourId is { } tourId)
            wanted = await db.TourGuides.AsNoTracking()
                .Where(g => g.TourId == tourId
                         && db.OrganizationUserMemberships.Any(
                                m => m.OrganizationId == orgId && m.AppUserId == g.AppUserId && m.IsActive))
                .OrderBy(g => g.SortOrder)
                .Select(g => g.AppUserId).ToListAsync(ct);

        if (wanted is null) return null;

        if (await TourController.WhoIsNotAMemberAsync(db, orgId, wanted, ct) is string refusal)
            return refusal;

        var existing = await db.OrgCalendarEventGuides
            .Where(g => g.OrgCalendarEventId == entity.Id).ToListAsync(ct);
        db.OrgCalendarEventGuides.RemoveRange(existing);

        var now = DateTime.UtcNow;
        for (var i = 0; i < wanted.Count; i++)
            db.OrgCalendarEventGuides.Add(new OrgCalendarEventGuide
            {
                Id = Guid.NewGuid(), OrgCalendarEventId = entity.Id, AppUserId = wanted[i],
                SortOrder = i, DateCreated = now, CreatedByAppUserId = userId,
            });

        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>One event as the client reads it, tour name and guides filled in.</summary>
    private async Task<OrgCalendarEventRecord> ProjectAsync(
        BenDataContext db, Guid eventId, CancellationToken ct)
    {
        var loaded = await db.OrgCalendarEvents.AsNoTracking()
            .Include(e => e.EventType).Include(e => e.Case).Include(e => e.Attendees)
            .Include(e => e.OrganizationAddress).Include(e => e.Tour)
            .Include(e => e.Guides).ThenInclude(g => g.AppUser)
            .FirstAsync(e => e.Id == eventId, ct);

        var guideIds = loaded.Guides.Select(g => g.AppUserId).ToList();
        var photos = await db.AppUserPhotos.AsNoTracking()
            .Where(p => guideIds.Contains(p.AppUserId) && p.IsPublic && p.IsActive)
            .Select(p => new { p.AppUserId, p.UploadFileId })
            .ToListAsync(ct);

        return _mapper.Map<OrgCalendarEventRecord>(loaded) with
        {
            TourName = loaded.Tour?.Name,
            Guides = [.. loaded.Guides.OrderBy(g => g.SortOrder).Select(g => new EventGuideRecord(
                g.AppUserId,
                g.AppUser.DisplayName ?? g.AppUser.Email ?? "A guide",
                g.AppUser.Handle,
                photos.FirstOrDefault(p => p.AppUserId == g.AppUserId)?.UploadFileId))],
        };
    }

    /// <summary>
    /// Gives a newly-public event its readable URL, and leaves an existing one alone.
    /// </summary>
    /// <remarks>
    /// Assigned once, on the way to being public, and never regenerated. A slug that followed the
    /// title would break every link somebody had already shared the moment an organizer fixed a
    /// typo — and the whole reason for a slug is that people share it.
    /// </remarks>
    private static async Task EnsurePublicSlugAsync(
        BenDataContext db, OrgCalendarEvent entity, CancellationToken ct)
    {
        if (!entity.IsPublic || entity.UrlName is not null) return;

        var candidate = UrlSlug.FromDateAndTitle(entity.StartDateTime, entity.Title)
                        ?? entity.StartDateTime.ToString("yyyy-MM-dd");

        entity.UrlName = await UrlSlug.MakeUniqueAsync(candidate, async slug =>
            await db.OrgCalendarEvents
                .AnyAsync(e => e.OrganizationId == entity.OrganizationId
                            && e.UrlName == slug
                            && e.Id != entity.Id, ct));
    }

    /// <summary>
    /// Why this calendar row cannot be edited here, or null.
    /// </summary>
    /// <remarks>
    /// <para>A hosted event's umbrella row is written from the event (item 235), so its title,
    /// dates, place, zone and public flag all come from there. Letting the calendar change them
    /// would leave two screens each believing they own the row, and the next save from either one
    /// would silently undo the other.</para>
    ///
    /// <para>The sentence names the event and says where to go, because a refusal that only says
    /// no is a refusal somebody works around.</para>
    /// </remarks>
    private static async Task<string?> UmbrellaRefusalAsync(
        BenDataContext db, OrgCalendarEvent entity, CancellationToken ct)
    {
        if (entity.HostedEventId is not Guid hostedId) return null;

        var name = await db.HostedEvents.AsNoTracking()
            .Where(e => e.Id == hostedId).Select(e => e.Name).FirstOrDefaultAsync(ct);

        return $"This date belongs to the event \u201c{name}\u201d and is kept in step with it. "
             + "Change it on the event's own page — its dates, venue and description all come "
             + "from there.";
    }

    [HttpPut("{eventId:guid}")]
    public async Task<ActionResult<OrgCalendarEventRecord>> Update(
        Guid orgId, Guid eventId, [FromBody] UpsertCalendarEventRequest request, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.OrgCalendarEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();
        if (await UmbrellaRefusalAsync(db, entity, ct) is string managed) return BadRequest(managed);
        entity.EventTypeId = request.EventTypeId; entity.CaseId = request.CaseId;
        entity.Title = request.Title.Trim(); entity.Description = CleanDescription(request.Description);
        entity.Location = request.Location?.Trim();
        entity.OrganizationAddressId = request.OrganizationAddressId;
        entity.MeetingUrl = NormaliseUrl(request.MeetingUrl);
        entity.StartDateTime = request.StartDateTime; entity.EndDateTime = request.EndDateTime;
        entity.IsAllDay = request.IsAllDay; entity.IsPublic = request.IsPublic;
        entity.PlaceId = request.PlaceId;
        entity.HideExactLocation = request.HideExactLocation;
        entity.AttendeeCapacity = request.AttendeeCapacity;
        entity.TimeZoneId = Trimmed(request.TimeZoneId);
        entity.RsvpClosesAt = request.RsvpClosesAt;
        entity.RecurrenceRule = request.RecurrenceRule?.Trim();
        entity.TourId = request.TourId;

        if (ZoneRefusal(entity.TimeZoneId) is string zoneRefusal)
            return BadRequest(zoneRefusal);

        if (entity.IsPublic
            && await PublicEventRefusalAsync(db, entity.CaseId, entity.PlaceId, ct) is string refusal)
            return BadRequest(refusal);

        if (await TourDateRefusalAsync(db, orgId, entity, ct) is string tourRefusal)
            return BadRequest(tourRefusal);

        // The same tour defaults as on create, or a date that was edited would lose the clock it
        // was scheduled with the moment somebody changed its title.
        await ApplyTourDefaultsAsync(db, entity, request, ct);

        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;

        await EnsurePublicSlugAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);

        // An edit that says nothing about guides leaves them alone; one that names them replaces
        // the list, which is how a guide swapped the afternoon of a walk gets swapped.
        if (request.GuideAppUserIds is not null
            && await SetGuidesAsync(db, orgId, entity, request, userId, seedFromTour: false, ct) is string guideRefusal)
            return BadRequest(guideRefusal);

        return Ok(await ProjectAsync(db, entity.Id, ct));
    }

    [HttpDelete("{eventId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid eventId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.OrgCalendarEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();
        if (await UmbrellaRefusalAsync(db, entity, ct) is string managed)
            return BadRequest(managed + " To take it off the calendar, archive the event there.");

        // A review cites the date it was written after, and that key is NoAction — so a business
        // deleting a walk it had run would be handed a database error with nothing to act on.
        // The review goes with the date: it is a review of a tour, and the group purge already
        // makes exactly this move in exactly this order.
        // Loaded and removed rather than ExecuteDelete: a date has a handful of reviews at most,
        // and ExecuteDelete is refused outright by the in-memory provider the controller suites
        // run on — a rule nothing could test is a rule that breaks in production instead.
        var reviews = await db.TourReviews.Where(r => r.OrgCalendarEventId == eventId).ToListAsync(ct);
        db.TourReviews.RemoveRange(reviews);

        db.OrgCalendarEvents.Remove(entity);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Tidies a pasted meeting link, or drops it if it isn't a usable web address.
    /// </summary>
    /// <remarks>
    /// People paste "zoom.us/j/123" as often as the full URL, so a bare host gets https://
    /// rather than being rejected. Anything that still will not parse as http(s) is stored as null
    /// instead of as text that would render a dead link — a link that goes nowhere is worse than
    /// no link, because someone will click it while a meeting is starting.
    /// </remarks>
    internal static string? NormaliseUrl(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (!value.Contains("://", StringComparison.Ordinal))
            value = "https://" + value;

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                ? uri.ToString()
                : null;
    }

    // ── Attendees ─────────────────────────────────────────────────────────────

    [HttpGet("{eventId:guid}/attendees")]
    public async Task<ActionResult<IEnumerable<OrgCalendarEventAttendeeRecord>>> GetAttendees(
        Guid orgId, Guid eventId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.OrgCalendarEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct))
            return NotFound();
        var attendees = await db.OrgCalendarEventAttendees.AsNoTracking()
            .Include(a => a.AppUser)
            .Where(a => a.OrgCalendarEventId == eventId)
            .ToListAsync(ct);
        return Ok(_mapper.Map<IEnumerable<OrgCalendarEventAttendeeRecord>>(attendees));
    }

    /// <summary>Invites someone by email address, for people outside the organization.</summary>
    /// <remarks>
    /// <para>By address rather than by search on purpose. The existing user search only returns
    /// people who already share an organization with the caller, so it cannot serve this case at
    /// all — and widening it would hand every group administrator a searchable directory of the
    /// whole site. Requiring the address means you can only invite someone you already know how
    /// to contact, which is how you knew to invite them.</para>
    ///
    /// <para>It reveals only whether a *published* address belongs to an account, to someone who
    /// already knows that address — which is what publishing an address means. Private sign-in
    /// addresses are not searchable here at all.</para>
    /// </remarks>
    [HttpPost("{eventId:guid}/attendees/by-email")]
    public async Task<ActionResult<OrgCalendarEventAttendeeRecord>> AddAttendeeByEmail(
        Guid orgId, Guid eventId, [FromBody] AddAttendeeByEmailRequest request, CancellationToken ct)
    {
        // Adding somebody to an event is EDITING THE CALENDAR, not merely belonging to the group
        // (Ben, 2026-08-27: make it "part of the permissions when hiring someone meant to run a
        // walking ghost tour"). A guide is given the calendar grant and can sign up the walk-up
        // standing in front of them; a member without it cannot put names on a night they have
        // nothing to do with. There is deliberately no time limit here — the guide is present,
        // which is the whole basis for trusting the judgement.
        if (!await IsAdminOrHasAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();

        var email = request.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email)) return BadRequest("An email address is required.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.OrgCalendarEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct))
            return NotFound();

        // Matched against the user's *published* addresses, never AppUser.Email. The sign-in
        // address is private by design — the profile page says so in as many words — so resolving
        // an invite against it would turn this endpoint into a way of confirming somebody's
        // private login from the outside. Only an address its owner marked public, did not hide,
        // and has validated will match.
        var target = await db.UserEmails.AsNoTracking()
            .Where(e => e.IsPublic && !e.IsHidden && e.IsValidated
                     && e.EmailAddress.ToLower() == email.ToLower())
            .Select(e => new { AppUserId = e.AppUserId })
            .FirstOrDefaultAsync(ct);

        if (target is null)
            return NotFound("No account here publishes that email address.");

        if (await db.OrgCalendarEventAttendees
                .AnyAsync(a => a.OrgCalendarEventId == eventId && a.AppUserId == target.AppUserId, ct))
            return BadRequest("They are already invited.");

        var invited = new OrgCalendarEventAttendee
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = eventId,
            AppUserId = target.AppUserId,
            RsvpStatus = RsvpStatus.Invited, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.OrgCalendarEventAttendees.Add(invited);
        await db.SaveChangesAsync(ct);

        var loadedInvite = await db.OrgCalendarEventAttendees.AsNoTracking()
            .Include(a => a.AppUser).FirstAsync(a => a.Id == invited.Id, ct);
        return Ok(_mapper.Map<OrgCalendarEventAttendeeRecord>(loadedInvite));
    }

    [HttpPost("{eventId:guid}/attendees")]
    public async Task<ActionResult<OrgCalendarEventAttendeeRecord>> AddAttendee(
        Guid orgId, Guid eventId, [FromBody] AddAttendeeRequest request, CancellationToken ct)
    {
        // Adding somebody to an event is EDITING THE CALENDAR, not merely belonging to the group
        // (Ben, 2026-08-27: make it "part of the permissions when hiring someone meant to run a
        // walking ghost tour"). A guide is given the calendar grant and can sign up the walk-up
        // standing in front of them; a member without it cannot put names on a night they have
        // nothing to do with. There is deliberately no time limit here — the guide is present,
        // which is the whole basis for trusting the judgement.
        if (!await IsAdminOrHasAsync(orgId, ct)) return Forbid();
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.OrgCalendarEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct))
            return NotFound();

        var attendee = new OrgCalendarEventAttendee
        {
            Id = Guid.NewGuid(), OrgCalendarEventId = eventId,
            AppUserId = request.AppUserId, AssignedTask = request.AssignedTask?.Trim(),
            RsvpStatus = RsvpStatus.Invited, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.OrgCalendarEventAttendees.Add(attendee);
        await db.SaveChangesAsync(ct);
        var loaded = await db.OrgCalendarEventAttendees.AsNoTracking()
            .Include(a => a.AppUser).FirstAsync(a => a.Id == attendee.Id, ct);
        return CreatedAtAction(nameof(GetAttendees), new { orgId, eventId },
            _mapper.Map<OrgCalendarEventAttendeeRecord>(loaded));
    }

    /// <summary>
    /// Sends a sign-up link to somebody who has no account here — the walk-up at the meeting point.
    /// </summary>
    /// <remarks>
    /// <para><b>The gap this closes (Ben, 2026-08-27).</b> A ghost walking tour is thirty guests a
    /// session who are not members of anything, and the late ones turn up with friends. Neither
    /// existing path served them: <c>AddAttendeeByEmail</c> resolves an <i>existing</i> account by
    /// published address, and <c>AddAttendee</c> needs an account id — a walk-up has neither. The
    /// guest could sign themselves up, and mostly will, but the person handing the guide cash on
    /// the pavement could not be helped by the guide at all.</para>
    ///
    /// <para><b>It sends a link rather than creating the attendance.</b> Deliberately. Letting an
    /// organiser type any address and have an account appear would create accounts for people who
    /// never asked, from addresses nobody verified — the email click is what makes the address
    /// real, and the guest has a phone in their hand. What the permission buys is the organiser's
    /// authority travelling with the link: it confirms after sign-ups close and past a full house,
    /// which is the whole point for a late arrival.</para>
    ///
    /// <para><b>It answers the same way whether or not that address has an account.</b> The public
    /// flow is careful not to become an account-existence oracle and this must not undo that from
    /// the inside — a guide is a hired stranger, not an administrator, and "does this person have
    /// an account" is not theirs to learn. The published-address rule on
    /// <c>AddAttendeeByEmail</c> stays exactly as strict as it was.</para>
    /// </remarks>
    [HttpPost("{eventId:guid}/guest-invites")]
    public async Task<ActionResult<bool>> InviteGuest(
        Guid orgId, Guid eventId, [FromBody] InviteGuestRequest request, CancellationToken ct)
    {
        if (!await IsAdminOrHasAsync(orgId, ct)) return Forbid();

        var email = request.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > 320)
            return BadRequest("A valid email address is needed.");

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var ev = await db.OrgCalendarEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        // Reuse a pending row rather than stacking one per attempt, exactly as the public flow
        // does — a guide who is not sure the first one arrived will simply send it again.
        var invite = await db.EventAttendanceInvites
            .FirstOrDefaultAsync(i => i.OrgCalendarEventId == eventId && i.Email == email, ct);

        if (invite is { DateConfirmed: not null })
            return Ok(true);   // already coming; say nothing that distinguishes the case

        var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

        if (invite is null)
        {
            invite = new EventAttendanceInvite
            {
                Id                 = Guid.NewGuid(),
                OrgCalendarEventId = eventId,
                Email              = email,
                DisplayName        = string.IsNullOrWhiteSpace(request.DisplayName) ? null : request.DisplayName.Trim(),
                Token              = token,
                DateExpires        = DateTime.UtcNow.AddDays(14),
                InvitedByAppUserId = userId,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            };
            db.EventAttendanceInvites.Add(invite);
        }
        else
        {
            invite.Token              = token;
            invite.DateExpires        = DateTime.UtcNow.AddDays(14);
            invite.DateUpdated        = DateTime.UtcNow;
            invite.UpdatedByAppUserId = userId;
            // A guest who asked for their own link and is now being vouched for by the guide gets
            // the organiser's latitude from here on: same person, better standing.
            invite.InvitedByAppUserId = userId;
            if (!string.IsNullOrWhiteSpace(request.DisplayName)) invite.DisplayName = request.DisplayName.Trim();
        }

        await db.SaveChangesAsync(ct);

        // Item 233 deliberately does NOT send the tour's own welcome here, attachment and all.
        // A guide typed this address on a pavement in the dark and nobody has proved it yet; the
        // full details — meeting point, guide's face, how to pay — go out when the link is
        // confirmed, which is the first moment there is somebody on the other end of it.
        if (_email.IsConfigured)
        {
            var link = _site.AbsoluteUrl($"/attending/{token}");
            var safeTitle = Ben.Data.WebApi.Services.NotificationText.Safe(ev.Title);
            try
            {
                await _email.SendAsync(email,
                    $"You're signed up for {ev.Title}",
                    $"<p>Someone from the group signed you up for <strong>{safeTitle}</strong> on "
                    + $"{ev.StartDateTime:dddd, MMMM d}.</p>"
                    + $"<p><a href=\"{link}\">Confirm you're coming</a></p>"
                    + "<p>Confirming lets you share photos, recordings and anything else from the "
                    + "night with the group. That link is good for two weeks and only works once.</p>", ct);
            }
            catch (Exception ex)
            {
                // Logged, never surfaced: the same reasoning as the public flow, and a guide can
                // do nothing about a bounced address anyway.
                _logger.LogWarning(ex, "Could not send a guest sign-up link for event {EventId}.", eventId);
            }
        }
        else
        {
            _logger.LogInformation(
                "Email is not configured; guest sign-up link for event {EventId} was not sent. Token: {Token}",
                eventId, token);
        }

        return Ok(true);
    }

    [HttpPut("{eventId:guid}/attendees/{attendeeId:guid}/rsvp")]
    public async Task<ActionResult<OrgCalendarEventAttendeeRecord>> Rsvp(
        Guid orgId, Guid eventId, Guid attendeeId, [FromBody] RsvpRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        var attendee = await db.OrgCalendarEventAttendees
            .FirstOrDefaultAsync(a => a.Id == attendeeId && a.OrgCalendarEventId == eventId, ct);
        if (attendee is null) return NotFound();
        // Only the attendee themselves or an org admin can update RSVP
        if (attendee.AppUserId != userId && !await IsAdminOrHasAsync(orgId, ct)) return Forbid();
        attendee.RsvpStatus = request.RsvpStatus;
        attendee.DateRsvp   = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        var loaded = await db.OrgCalendarEventAttendees.AsNoTracking()
            .Include(a => a.AppUser).FirstAsync(a => a.Id == attendee.Id, ct);
        return Ok(_mapper.Map<OrgCalendarEventAttendeeRecord>(loaded));
    }

    [HttpDelete("{eventId:guid}/attendees/{attendeeId:guid}")]
    public async Task<IActionResult> RemoveAttendee(
        Guid orgId, Guid eventId, Guid attendeeId, CancellationToken ct)
    {
        if (!await IsAdminOrHasAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var attendee = await db.OrgCalendarEventAttendees
            .FirstOrDefaultAsync(a => a.Id == attendeeId && a.OrgCalendarEventId == eventId, ct);
        if (attendee is null) return NotFound();
        db.OrgCalendarEventAttendees.Remove(attendee);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Seats on a tour date (item 234, Ben 2026-09-10) ──────────────────────
    //
    // Ben: "They would not be confirmed until the tour guide or manager approves them meaning they
    // have settled how money will be or has been exchanged." THIS SITE NEVER TAKES THE MONEY.
    // Approving is the business saying that side of it is sorted; nothing here is a payment record
    // and nothing here should ever be read as one.

    /// <summary>
    /// Approves a seat: the places are held, and the guest is written to.
    /// </summary>
    /// <remarks>
    /// <para>Refused when the places asked for do not fit, <b>in words that name how many are
    /// left</b> — a business told only "full" cannot tell whether to approve a smaller party.</para>
    ///
    /// <para>Approving is what sends the tour's own welcome, with the walk attached as a calendar
    /// file. Until this moment the guest has been told nothing but "we have your request", because
    /// "here is where to stand" is untrue of a seat nobody has agreed to.</para>
    /// </remarks>
    [HttpPost("{eventId:guid}/attendees/{attendeeId:guid}/approve")]
    public async Task<ActionResult<OrgCalendarEventAttendeeRecord>> ApproveSeat(
        Guid orgId, Guid eventId, Guid attendeeId, CancellationToken ct)
    {
        if (!await IsAdminOrHasAsync(orgId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);

        var ev = await db.OrgCalendarEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        var attendees = await db.OrgCalendarEventAttendees
            .Where(a => a.OrgCalendarEventId == eventId).ToListAsync(ct);

        var seat = attendees.FirstOrDefault(a => a.Id == attendeeId);
        if (seat is null) return NotFound();
        if (seat.SeatStatus == TourSeatStatus.Reserved) return Ok(await SeatRecordAsync(db, seat.Id, ct));

        var wanted = Math.Max(1, seat.Seats);
        var taken  = TourSeats.PlacesTaken(attendees, excludingAppUserId: seat.AppUserId);
        if (TourSeats.WhyTheseSeatsCannotBeApproved(ev.AttendeeCapacity, taken, wanted) is { } refusal)
            return Conflict(refusal);

        seat.SeatStatus             = TourSeatStatus.Reserved;
        seat.RsvpStatus             = RsvpStatus.Accepted;
        seat.SeatDecidedUtc         = DateTime.UtcNow;
        seat.SeatDecidedByAppUserId = GetCurrentUserId();
        seat.GuestAcknowledgedUtc   = null;
        await db.SaveChangesAsync(ct);

        // Now it is true, so now it is sent.
        if (await db.AppUsers.AsNoTracking()
                .Where(u => u.Id == seat.AppUserId)
                .Select(u => new { u.Email, u.DisplayName })
                .FirstOrDefaultAsync(ct) is { Email: { Length: > 0 } address } guest)
        {
            await _tourMail.SendSignUpAsync(db, eventId, address, guest.DisplayName, ct);
        }

        return Ok(await SeatRecordAsync(db, seat.Id, ct));
    }

    /// <summary>
    /// Turns a seat down, and says so.
    /// </summary>
    /// <remarks>
    /// Recorded rather than deleted. A guest who is not coming has to be able to see that they are
    /// not coming, and a row that vanishes reads to them as a request that was never received.
    /// </remarks>
    [HttpPost("{eventId:guid}/attendees/{attendeeId:guid}/turn-down")]
    public async Task<ActionResult<OrgCalendarEventAttendeeRecord>> TurnDownSeat(
        Guid orgId, Guid eventId, Guid attendeeId, CancellationToken ct)
    {
        if (!await IsAdminOrHasAsync(orgId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.OrgCalendarEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct))
            return NotFound();

        var seat = await db.OrgCalendarEventAttendees
            .FirstOrDefaultAsync(a => a.Id == attendeeId && a.OrgCalendarEventId == eventId, ct);
        if (seat is null) return NotFound();

        seat.SeatStatus             = TourSeatStatus.TurnedDown;
        seat.RsvpStatus             = RsvpStatus.Declined;
        seat.SeatDecidedUtc         = DateTime.UtcNow;
        seat.SeatDecidedByAppUserId = GetCurrentUserId();
        await db.SaveChangesAsync(ct);

        return Ok(await SeatRecordAsync(db, seat.Id, ct));
    }

    private async Task<OrgCalendarEventAttendeeRecord> SeatRecordAsync(
        BenDataContext db, Guid attendeeId, CancellationToken ct)
        => _mapper.Map<OrgCalendarEventAttendeeRecord>(
            await db.OrgCalendarEventAttendees.AsNoTracking()
                .Include(a => a.AppUser).FirstAsync(a => a.Id == attendeeId, ct));

    private async Task<bool> IsOrgMemberAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgMemberAsync(db, orgId, userId, ct);
    }

    private async Task<bool> IsOrgAdminAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgAdminAsync(db, orgId, userId, ct);
    }
}

// ── Request records ───────────────────────────────────────────────────────────

public sealed record UpsertCalendarEventTypeRequest(
    string Name, string? ColorClass, string? IconClass, int SortOrder, bool IsActive);

public sealed record UpsertCalendarEventRequest(
    string Title, string? Description, string? Location,
    DateTime StartDateTime, DateTime EndDateTime, bool IsAllDay, bool IsPublic,
    Guid? EventTypeId, Guid? CaseId, string? RecurrenceRule,
    Guid? OrganizationAddressId = null,
    string? MeetingUrl = null,
    // Public-event fields (item #87), defaulted so every existing caller is unaffected.
    Guid? PlaceId = null,
    bool HideExactLocation = false,
    int? AttendeeCapacity = null,
    DateTime? RsvpClosesAt = null,
    // Item 233: which tour this date runs, and who is leading it. Defaulted so every existing
    // caller is unaffected; null guides means "leave whoever is already on it alone".
    Guid? TourId = null,
    IReadOnlyList<Guid>? GuideAppUserIds = null,
    // The IANA zone this event happens in. Null on a tour date takes the tour's; null on
    // anything else leaves it unsaid, and a public listing then shows UTC and says so.
    string? TimeZoneId = null);

public sealed record AddAttendeeByEmailRequest(string? Email);

public sealed record AddAttendeeRequest(Guid AppUserId, string? AssignedTask);

/// <summary>A walk-up an organiser is signing up: an address, and a name if they gave one.</summary>
/// <remarks>
/// The name is optional because a guide taking details on a pavement in the dark will often get
/// only the address, and half a record beats refusing the person.
/// </remarks>
public sealed record InviteGuestRequest(string? Email, string? DisplayName);
public sealed record RsvpRequest(Ben.Data.Common.Enums.RsvpStatus RsvpStatus);
