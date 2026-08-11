using AutoMapper;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        return await db.OrganizationUserMemberships.AnyAsync(
            m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive
              && (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator), ct);
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
            .Include(e => e.Attendees)
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
            .Include(e => e.EventType).Include(e => e.Case).Include(e => e.Attendees)
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
            StartDateTime = request.StartDateTime, EndDateTime = request.EndDateTime,
            IsAllDay = request.IsAllDay, IsPublic = request.IsPublic,
            RecurrenceRule = request.RecurrenceRule?.Trim(),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.OrgCalendarEvents.Add(entity);
        await db.SaveChangesAsync(ct);

        var loaded = await db.OrgCalendarEvents.AsNoTracking()
            .Include(e => e.EventType).Include(e => e.Case).Include(e => e.Attendees)
            .FirstAsync(e => e.Id == entity.Id, ct);
        return CreatedAtAction(nameof(GetById), new { orgId, eventId = entity.Id },
            _mapper.Map<OrgCalendarEventRecord>(loaded));
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
        entity.StartDateTime = request.StartDateTime; entity.EndDateTime = request.EndDateTime;
        entity.IsAllDay = request.IsAllDay; entity.IsPublic = request.IsPublic;
        entity.RecurrenceRule = request.RecurrenceRule?.Trim();
        entity.DateUpdated = DateTime.UtcNow;
        entity.UpdatedByAppUserId = userId == Guid.Empty ? null : userId;
        await db.SaveChangesAsync(ct);
        var loaded = await db.OrgCalendarEvents.AsNoTracking()
            .Include(e => e.EventType).Include(e => e.Case).Include(e => e.Attendees)
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
        return await db.OrganizationUserMemberships.AnyAsync(
            m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive
              && (m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator), ct);
    }
}

// ── Request records ───────────────────────────────────────────────────────────

public sealed record UpsertCalendarEventTypeRequest(
    string Name, string? ColorClass, string? IconClass, int SortOrder, bool IsActive);

public sealed record UpsertCalendarEventRequest(
    string Title, string? Description, string? Location,
    DateTime StartDateTime, DateTime EndDateTime, bool IsAllDay, bool IsPublic,
    Guid? EventTypeId, Guid? CaseId, string? RecurrenceRule);

public sealed record AddAttendeeRequest(Guid AppUserId, string? AssignedTask);
public sealed record RsvpRequest(Ben.Data.Common.Enums.RsvpStatus RsvpStatus);
