using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// An organization's investigations, including ones that belong to no client case.
/// </summary>
/// <remarks>
/// <para>The counterpart to <see cref="InvestigationController"/>, which is nested under a case and
/// therefore cannot express a visit that has none — a group going to a landmark on its own account.
/// Both write the same entity; this one is simply reachable without a case in the route.</para>
///
/// <para><b>The invariant is enforced here, not in the database.</b> An investigation must have a
/// case or a place: <c>CaseId is not null || PlaceId is not null</c>. A check constraint would be
/// the obvious home for it, except the InMemory provider the tests run against ignores check
/// constraints entirely — the rule would hold in production and silently not hold in every test,
/// which is the worst of both.</para>
/// </remarks>
[ApiController]
[Route("api/organizations/{orgId:guid}/investigations")]
[Authorize]
public sealed class OrgInvestigationsController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;
    private readonly IAuditLogService _auditLog;

    public OrgInvestigationsController(
        IDbContextFactory<BenDataContext> db, IMapper mapper, IAuditLogService auditLog)
    {
        _db = db;
        _mapper = mapper;
        _auditLog = auditLog;
    }

    /// <summary>
    /// Every investigation this organization ran, with or without a case, each carrying what the
    /// caller may do with it.
    /// </summary>
    /// <remarks>
    /// Returns <see cref="OrgInvestigationRow"/> rather than <c>InvestigationRecord</c> because
    /// this is the map-and-grid feed: it needs the place and case denormalised for display, and it
    /// needs the permission verdicts. The UI renders those verdicts and never derives them — a
    /// client that decides for itself who may edit is a client that can be told otherwise.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrgInvestigationRow>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        if (!await IsMemberAsync(orgId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);

        // Filtered on the investigation's own OrganizationId. Joining through the case here would
        // silently exclude exactly the rows this controller exists to serve.
        var list = await db.Investigations.AsNoTracking()
            .Include(i => i.Case)
            .Include(i => i.Place)
            .Where(i => i.OrganizationId == orgId)
            .Select(i => new
            {
                i.Id, i.Title, i.ScheduledDateTime, i.EndDateTime, i.Status, i.Visibility, i.Location,
                i.CaseId, i.Place, i.PlaceId, i.Latitude, i.Longitude, i.GeocodeNote,
                CaseYear = i.Case == null ? (int?)null : i.Case.CaseYear,
                CaseNumber = i.Case == null ? (int?)null : i.Case.OrgCaseNumber,
                CaseTitle = i.Case == null ? null : i.Case.Title,
                AttendeeCount = i.Attendees.Count,
            })
            .OrderByDescending(i => i.ScheduledDateTime)
            .ToListAsync(ct);

        var flags = await InvestigationAccess.ComputeFlagsAsync(
            db, orgId, list.Select(i => i.Id).ToList(),
            GetCurrentUserId(), User.IsInRole(RoleNames.SuperAdmin), ct);

        return Ok(list.Select(i =>
        {
            var f = flags.TryGetValue(i.Id, out var found)
                ? found
                // Defaulting to "no" rather than "yes" if a row somehow went missing between the
                // two queries. A permission gap should close, not open.
                : new InvestigationPermissionFlags(false, false);

            return new OrgInvestigationRow(
                Id: i.Id,
                Title: i.Title,
                ScheduledDateTime: i.ScheduledDateTime,
                EndDateTime: i.EndDateTime,
                Status: i.Status,
                Visibility: i.Visibility,
                Location: i.Location,
                CaseId: i.CaseId,
                CaseReference: i.CaseYear is null ? null : $"#{i.CaseYear}-{i.CaseNumber:D3}",
                CaseTitle: i.CaseTitle,
                PlaceId: i.PlaceId,
                PlaceName: i.Place?.Name,
                PlaceCity: i.Place?.City,
                PlaceState: i.Place?.State,
                Latitude: i.Latitude,
                Longitude: i.Longitude,
                GeocodeNote: i.GeocodeNote,
                AttendeeCount: i.AttendeeCount,
                CanEditRecord: f.CanEditRecord,
                CanCompleteMyFindings: f.CanCompleteMyFindings);
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InvestigationRecord>> GetById(
        Guid orgId, Guid id, CancellationToken ct)
    {
        if (!await IsMemberAsync(orgId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var inv = await db.Investigations.AsNoTracking()
            .Include(i => i.Attendees)
            .FirstOrDefaultAsync(i => i.Id == id && i.OrganizationId == orgId, ct);

        // Matched on id and org together, so another organization's investigation is invisible
        // rather than merely forbidden.
        return inv is null ? NotFound() : Ok(_mapper.Map<InvestigationRecord>(inv));
    }

    /// <summary>
    /// Schedules an investigation. With no <c>CaseId</c>, a place is required.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<InvestigationRecord>> Create(
        Guid orgId, [FromBody] CreateOrgInvestigationRequest request, CancellationToken ct)
    {
        if (!await IsMemberAsync(orgId, ct)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.Title)) return BadRequest("A title is required.");

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        // A case supplied here still has to belong to the organization in the route. Trusting the
        // route id alone is the "broken ID chain" shape this codebase has already been bitten by:
        // a member of their own org passes their own orgId to satisfy the membership check, plus
        // somebody else's caseId, and reaches another organization's data.
        if (request.CaseId is { } caseId
            && !await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct))
            return NotFound("Case not found.");

        var hasPlace = request.PlaceId is not null || (request.NewPlace?.HasAnything ?? false);
        if (request.CaseId is null && !hasPlace)
            return BadRequest("An investigation with no case must say where it happened. Choose a place or describe one.");

        var entity = new Investigation
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CaseId = request.CaseId,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            Location = request.Location?.Trim(),
            ScheduledDateTime = request.ScheduledDateTime,
            EndDateTime = request.EndDateTime,
            Status = InvestigationStatus.Scheduled,
            EvidenceDueDate = request.EvidenceDueDate,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.Investigations.Add(entity);

        var placement = await InvestigationPlacement.ApplyAsync(
            db, entity, request.PlaceId, request.NewPlace, userId, ct);
        if (placement.Error is not null) return BadRequest(placement.Error);

        // A landmark defaults to sharing with others who have worked it; a home does not. Chosen
        // from the place rather than left to whoever clicks fastest.
        entity.Visibility = request.Visibility ?? InvestigationVisibilityFilter.DefaultFor(placement.Place);
        if (InvestigationVisibilityFilter.Reject(entity.Visibility, placement.Place) is { } scopeError)
            return BadRequest(scopeError);

        // Same auto-calendar-event behaviour as the case-bound controller, so a visit booked this
        // way still shows up on the group's calendar. CaseId is nullable on OrgCalendarEvent, so
        // this works case-less unchanged.
        var calEvent = new OrgCalendarEvent
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            CaseId = request.CaseId,
            Title = $"Investigation: {entity.Title}",
            Description = entity.Description,
            Location = entity.Location,
            StartDateTime = entity.ScheduledDateTime,
            EndDateTime = entity.EndDateTime ?? entity.ScheduledDateTime.AddHours(2),
            IsAllDay = false,
            IsPublic = false,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.OrgCalendarEvents.Add(calEvent);
        entity.OrgCalendarEventId = calEvent.Id;

        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogCreateAsync(
            nameof(Investigation), entity.Id, entity, userId, AppSources.WebApi, ct));

        var loaded = await db.Investigations.AsNoTracking()
            .Include(i => i.Attendees)
            .FirstAsync(i => i.Id == entity.Id, ct);

        return CreatedAtAction(nameof(GetById), new { orgId, id = entity.Id },
            _mapper.Map<InvestigationRecord>(loaded));
    }

    // ── Arrival ───────────────────────────────────────────────────────────────

    /// <summary>Who is on the team and who has turned up. Readable by any member.</summary>
    [HttpGet("{id:guid}/roster")]
    public async Task<ActionResult<IEnumerable<InvestigationRosterEntry>>> GetRoster(
        Guid orgId, Guid id, CancellationToken ct)
    {
        if (!await IsMemberAsync(orgId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Investigations.AsNoTracking()
                .AnyAsync(i => i.Id == id && i.OrganizationId == orgId, ct))
            return NotFound();

        var rows = await db.InvestigationAttendees.AsNoTracking()
            .Where(a => a.InvestigationId == id)
            .OrderByDescending(a => a.IsLead)
            .ThenBy(a => a.AppUser.DisplayName)
            .Select(a => new InvestigationRosterEntry(
                a.Id,
                a.AppUserId,
                a.AppUser.DisplayName,
                a.AssignedRole,
                a.IsLead,
                a.Rsvp,
                a.DidAttend,
                a.DateArrived,
                // Whether it was self-reported, without naming who overrode it — the roster is
                // read by the whole team and "somebody else recorded this" is the part that
                // matters to them.
                a.DidAttend == true && a.AttendanceRecordedByAppUserId == null))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// "I'm here." Records the caller's own arrival on an investigation they are on.
    /// </summary>
    /// <remarks>
    /// <para>Its own endpoint rather than a field on the attendance update, because the two are
    /// different claims: this one leaves <c>AttendanceRecordedByAppUserId</c> null, which is what
    /// makes self-reported arrival distinguishable from a manager's later correction.</para>
    ///
    /// <para><c>StatedArrivalTime</c> is optional and may be in the past. Sites with no signal are
    /// the norm, so checking in afterwards and saying when you actually got there is the ordinary
    /// path — not an exception to apologise for. Future times are refused: that is a typo, not a
    /// memory.</para>
    /// </remarks>
    [HttpPost("{id:guid}/check-in")]
    public async Task<ActionResult<InvestigationRosterEntry>> CheckIn(
        Guid orgId, Guid id, [FromBody] CheckInRequest request, CancellationToken ct)
    {
        if (!await IsMemberAsync(orgId, ct)) return Forbid();

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await db.Investigations.AsNoTracking()
                .AnyAsync(i => i.Id == id && i.OrganizationId == orgId, ct))
            return NotFound();

        // Only someone actually on the team can say they were there. Membership of the group is
        // not enough — otherwise anyone could add themselves to the record of any visit.
        var attendee = await db.InvestigationAttendees
            .FirstOrDefaultAsync(a => a.InvestigationId == id && a.AppUserId == userId, ct);
        if (attendee is null)
            return Forbid();

        var arrivedAt = request.StatedArrivalTime ?? DateTime.UtcNow;
        if (arrivedAt > DateTime.UtcNow.AddMinutes(5))
            return BadRequest("That arrival time is in the future. Check the date and try again.");

        attendee.DidAttend = true;
        attendee.DateArrived = arrivedAt;
        // Null, deliberately: see AttendanceRecordedByAppUserId. Cleared rather than left alone so
        // that checking in after a manager marked you absent restores it to your own account.
        attendee.AttendanceRecordedByAppUserId = null;

        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(
            nameof(InvestigationAttendee), attendee.Id,
            new InvestigationAttendee { Id = attendee.Id }, attendee, userId, AppSources.WebApi, ct));

        return Ok(await ToRosterEntryAsync(db, attendee.Id, ct));
    }

    /// <summary>
    /// Records or corrects somebody else's attendance. Stamps the caller as the source.
    /// </summary>
    /// <remarks>
    /// The counterpart to check-in, for the person who forgot, or had no signal and never got
    /// round to it. Gated on managing the investigation, and the caller's id is written to
    /// <c>AttendanceRecordedByAppUserId</c> so the roster can tell the two apart afterwards.
    /// </remarks>
    [HttpPut("{id:guid}/attendees/{attendeeId:guid}/attendance")]
    public async Task<ActionResult<InvestigationRosterEntry>> OverrideAttendance(
        Guid orgId, Guid id, Guid attendeeId, [FromBody] OverrideAttendanceRequest request, CancellationToken ct)
    {
        if (!await IsMemberAsync(orgId, ct)) return Forbid();

        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var attendee = await db.InvestigationAttendees
            .FirstOrDefaultAsync(a => a.Id == attendeeId
                                   && a.InvestigationId == id
                                   && a.Investigation.OrganizationId == orgId, ct);
        if (attendee is null) return NotFound();

        if (!await InvestigationAccess.CanManageAsync(
                db, id, userId, User.IsInRole(RoleNames.SuperAdmin), ct))
            return Forbid();

        attendee.DidAttend = request.DidAttend;
        attendee.DateArrived = request.DidAttend == true ? request.StatedArrivalTime : null;
        // Stamped even when marking someone absent: "who says so" matters just as much for that.
        attendee.AttendanceRecordedByAppUserId = userId;

        await db.SaveChangesAsync(ct);

        _ = TryAuditAsync(_auditLog.LogUpdateAsync(
            nameof(InvestigationAttendee), attendee.Id,
            new InvestigationAttendee { Id = attendee.Id }, attendee, userId, AppSources.WebApi, ct));

        return Ok(await ToRosterEntryAsync(db, attendee.Id, ct));
    }

    private static async Task<InvestigationRosterEntry> ToRosterEntryAsync(
        BenDataContext db, Guid attendeeId, CancellationToken ct)
        => await db.InvestigationAttendees.AsNoTracking()
            .Where(a => a.Id == attendeeId)
            .Select(a => new InvestigationRosterEntry(
                a.Id, a.AppUserId, a.AppUser.DisplayName, a.AssignedRole, a.IsLead,
                a.Rsvp, a.DidAttend, a.DateArrived,
                a.DidAttend == true && a.AttendanceRecordedByAppUserId == null))
            .FirstAsync(ct);

    /// <summary>
    /// Membership check, delegating to the shared helper rather than adding a seventh hand-copied
    /// <c>IsOrgMemberAsync</c> — the duplication a previous dedupe pass was cleaning up.
    /// </summary>
    private async Task<bool> IsMemberAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgMemberAsync(db, orgId, GetCurrentUserId(), ct);
    }
}

/// <summary>
/// One investigation as the organization's map-and-grid view needs it.
/// </summary>
/// <remarks>
/// <para>Flat and denormalised on purpose: the map draws a pin per row and the grids list them
/// beside each other, so making the client fetch a case and a place per row would be a request
/// storm in service of a shape nobody wanted.</para>
///
/// <para><c>GeocodeNote</c> travels with the row so the screen can list what it could not place
/// beneath the map, rather than silently drawing fewer pins than there are investigations.</para>
///
/// <para>Field notes: <c>Location</c> is the team's own free text, often somewhere other than the
/// address on file. <c>CaseId</c>, <c>CaseReference</c> and <c>CaseTitle</c> are null together for
/// a case-less visit. <c>CanEditRecord</c> is the server's verdict — render it, do not re-derive
/// it — and <c>CanCompleteMyFindings</c> is attendance-based, so a participant who runs nothing
/// still qualifies.</para>
/// </remarks>
public sealed record OrgInvestigationRow(
    Guid Id,
    string Title,
    DateTime ScheduledDateTime,
    DateTime? EndDateTime,
    InvestigationStatus Status,
    InvestigationVisibility Visibility,
    string? Location,
    Guid? CaseId,
    string? CaseReference,
    string? CaseTitle,
    Guid? PlaceId,
    string? PlaceName,
    string? PlaceCity,
    string? PlaceState,
    decimal? Latitude,
    decimal? Longitude,
    string? GeocodeNote,
    int AttendeeCount,
    bool CanEditRecord,
    bool CanCompleteMyFindings);

/// <summary>
/// One person on an investigation's team, and whether they turned up.
/// </summary>
/// <remarks>
/// <c>SelfReported</c> is the provenance the whole check-in design exists to preserve: it says the
/// person recorded their own arrival rather than having it recorded for them. Who did the
/// recording is not exposed here — the roster is read by the whole team, and the part that matters
/// to them is that it came from somewhere other than the attendee.
/// </remarks>
public sealed record InvestigationRosterEntry(
    Guid AttendeeId,
    Guid AppUserId,
    string? DisplayName,
    string? AssignedRole,
    bool IsLead,
    RsvpStatus Rsvp,
    bool? DidAttend,
    DateTime? DateArrived,
    bool SelfReported);

/// <summary>
/// "I'm here." <c>StatedArrivalTime</c> null means now.
/// </summary>
/// <remarks>
/// Past times are expected, not exceptional — most sites have no signal, so checking in afterwards
/// and saying when you got there is the normal path.
/// </remarks>
public sealed record CheckInRequest(DateTime? StatedArrivalTime = null);

/// <summary>Records or corrects somebody else's attendance. The caller is stamped as the source.</summary>
public sealed record OverrideAttendanceRequest(bool? DidAttend, DateTime? StatedArrivalTime = null);

/// <summary>
/// Schedules an investigation against an organization, with a case or without one.
/// </summary>
/// <remarks>
/// <para>Deliberately not <c>UpsertInvestigationRequest</c>: that shape takes its case from the
/// route and has no way to say "no case at all", which is the entire point of this endpoint.</para>
///
/// <para><c>CaseId</c> null means a visit with no client case. <c>PlaceId</c> names an existing
/// place; <c>NewPlace</c> describes one to create. With no case, one of the latter two is required.</para>
/// </remarks>
public sealed record CreateOrgInvestigationRequest(
    string Title,
    DateTime ScheduledDateTime,
    string? Description = null,
    string? Location = null,
    DateTime? EndDateTime = null,
    DateTime? EvidenceDueDate = null,
    Guid? CaseId = null,
    Guid? PlaceId = null,
    NewPlaceRequest? NewPlace = null,
    InvestigationVisibility? Visibility = null);
