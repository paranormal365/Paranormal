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

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>Calendar event types CRUD (org admin only).</summary>
[ApiController]
[Route("api/organizations/{orgId:guid}/calendar-event-types")]
[Authorize]
public sealed class OrgCalendarEventTypeController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public OrgCalendarEventTypeController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    { _db = db; _mapper = mapper; }

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
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
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
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
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
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
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
public sealed class OrgCalendarEventController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public OrgCalendarEventController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    { _db = db; _mapper = mapper; }

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
        var ev = await db.OrgCalendarEvents.AsNoTracking()
            .Include(e => e.EventType).Include(e => e.Case).Include(e => e.Attendees).Include(e => e.OrganizationAddress)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        return ev is null ? NotFound() : Ok(_mapper.Map<OrgCalendarEventRecord>(ev));
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
            Title = request.Title.Trim(), Description = request.Description?.Trim(),
            Location = request.Location?.Trim(),
            OrganizationAddressId = request.OrganizationAddressId,
            MeetingUrl = NormaliseUrl(request.MeetingUrl),
            StartDateTime = request.StartDateTime, EndDateTime = request.EndDateTime,
            IsAllDay = request.IsAllDay, IsPublic = request.IsPublic,
            PlaceId = request.PlaceId,
            HideExactLocation = request.HideExactLocation,
            AttendeeCapacity = request.AttendeeCapacity,
            RsvpClosesAt = request.RsvpClosesAt,
            RecurrenceRule = request.RecurrenceRule?.Trim(),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };

        if (entity.IsPublic
            && await PublicEventRefusalAsync(db, entity.CaseId, entity.PlaceId, ct) is string refusal)
            return BadRequest(refusal);

        await EnsurePublicSlugAsync(db, entity, ct);

        db.OrgCalendarEvents.Add(entity);
        await db.SaveChangesAsync(ct);

        var loaded = await db.OrgCalendarEvents.AsNoTracking()
            .Include(e => e.EventType).Include(e => e.Case).Include(e => e.Attendees).Include(e => e.OrganizationAddress)
            .FirstAsync(e => e.Id == entity.Id, ct);
        return CreatedAtAction(nameof(GetById), new { orgId, eventId = entity.Id },
            _mapper.Map<OrgCalendarEventRecord>(loaded));
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
        entity.EventTypeId = request.EventTypeId; entity.CaseId = request.CaseId;
        entity.Title = request.Title.Trim(); entity.Description = request.Description?.Trim();
        entity.Location = request.Location?.Trim();
        entity.OrganizationAddressId = request.OrganizationAddressId;
        entity.MeetingUrl = NormaliseUrl(request.MeetingUrl);
        entity.StartDateTime = request.StartDateTime; entity.EndDateTime = request.EndDateTime;
        entity.IsAllDay = request.IsAllDay; entity.IsPublic = request.IsPublic;
        entity.PlaceId = request.PlaceId;
        entity.HideExactLocation = request.HideExactLocation;
        entity.AttendeeCapacity = request.AttendeeCapacity;
        entity.RsvpClosesAt = request.RsvpClosesAt;
        entity.RecurrenceRule = request.RecurrenceRule?.Trim();

        if (entity.IsPublic
            && await PublicEventRefusalAsync(db, entity.CaseId, entity.PlaceId, ct) is string refusal)
            return BadRequest(refusal);

        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;

        await EnsurePublicSlugAsync(db, entity, ct);
        await db.SaveChangesAsync(ct);
        var loaded = await db.OrgCalendarEvents.AsNoTracking()
            .Include(e => e.EventType).Include(e => e.Case).Include(e => e.Attendees).Include(e => e.OrganizationAddress)
            .FirstAsync(e => e.Id == entity.Id, ct);
        return Ok(_mapper.Map<OrgCalendarEventRecord>(loaded));
    }

    [HttpDelete("{eventId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid eventId, CancellationToken ct)
    {
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var entity = await db.OrgCalendarEvents
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (entity is null) return NotFound();
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
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
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
        if (!await IsOrgMemberAsync(orgId, ct)) return Forbid();
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
        if (attendee.AppUserId != userId && !await IsOrgAdminAsync(orgId, ct)) return Forbid();
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
        if (!await IsOrgAdminAsync(orgId, ct)) return Forbid();
        await using var db = await _db.CreateDbContextAsync(ct);
        var attendee = await db.OrgCalendarEventAttendees
            .FirstOrDefaultAsync(a => a.Id == attendeeId && a.OrgCalendarEventId == eventId, ct);
        if (attendee is null) return NotFound();
        db.OrgCalendarEventAttendees.Remove(attendee);
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
    DateTime? RsvpClosesAt = null);

public sealed record AddAttendeeByEmailRequest(string? Email);

public sealed record AddAttendeeRequest(Guid AppUserId, string? AssignedTask);
public sealed record RsvpRequest(Ben.Data.Common.Enums.RsvpStatus RsvpStatus);
