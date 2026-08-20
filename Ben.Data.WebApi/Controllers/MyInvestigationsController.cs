using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

[ApiController]
[Route("api/my-investigations")]
[Authorize]
public sealed class MyInvestigationsController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public MyInvestigationsController(IDbContextFactory<BenDataContext> db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MyInvestigationItem>>> GetMyInvestigations(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var attendances = await db.InvestigationAttendees.AsNoTracking()
            .Where(a => a.AppUserId == userId)
            .Include(a => a.Investigation)
                .ThenInclude(i => i.Case)
            // The organization comes off the investigation directly now. Reaching it through the
            // case would drop every case-less visit from this list without saying so.
            .Include(a => a.Investigation)
                .ThenInclude(i => i.Organization)
            .OrderByDescending(a => a.Investigation.ScheduledDateTime)
            .ToListAsync(ct);

        return Ok(attendances.Select(a => new MyInvestigationItem(
            AttendeeId:        a.Id,
            InvestigationId:   a.InvestigationId,
            CaseId:            a.Investigation.CaseId,
            CaseReference:     a.Investigation.Case is null
                                   ? null
                                   : $"#{a.Investigation.Case.CaseYear}-{a.Investigation.Case.OrgCaseNumber:D3}",
            CaseTitle:         a.Investigation.Case?.Title,
            OrgId:             a.Investigation.OrganizationId,
            OrgName:           a.Investigation.Organization.Name,
            OrgUrlName:        a.Investigation.Organization.UrlName,
            Title:             a.Investigation.Title,
            ScheduledDateTime: a.Investigation.ScheduledDateTime,
            EndDateTime:       a.Investigation.EndDateTime,
            Location:          a.Investigation.Location,
            Status:            a.Investigation.Status,
            AssignedRole:      a.AssignedRole,
            Rsvp:              a.Rsvp,
            DidAttend:         a.DidAttend,
            EvidenceDueDate:   a.Investigation.EvidenceDueDate)));
    }

    /// <summary>
    /// The investigations this person actually attended, for their own map.
    /// </summary>
    /// <remarks>
    /// <para><b>Past and <c>DidAttend == true</c>, and nothing else.</b> Not "invited to", not
    /// "accepted the invitation" — a map of places you said you might go to is not a map of where
    /// you have been, and the second is the only one worth having.</para>
    ///
    /// <para>That makes this deliberately sparse today: nothing marks attendance except a manager
    /// ticking the box, so most people will see an empty map until arrival check-in lands. An
    /// honestly empty map is better than a full one that overstates where somebody has been.</para>
    /// </remarks>
    [HttpGet("attended")]
    public async Task<ActionResult<IEnumerable<AttendedInvestigationItem>>> GetAttended(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var now = DateTime.UtcNow;
        var rows = await db.InvestigationAttendees.AsNoTracking()
            .Where(a => a.AppUserId == userId
                     && a.DidAttend == true
                     && a.Investigation.ScheduledDateTime < now)
            // Ordered on the ENTITY, before the projection. Sorting after it asks EF to order by a
            // property of a record it is constructing, which it cannot turn into SQL — and it says
            // so by throwing at runtime, not by failing to compile. Both callers wrap this in a
            // catch that falls back to an empty list, so the 500 surfaced as "you have not
            // attended anything yet" and the map was quietly empty for everyone.
            .OrderByDescending(a => a.Investigation.ScheduledDateTime)
            .Select(a => new AttendedInvestigationItem(
                a.InvestigationId,
                a.Investigation.Title,
                a.Investigation.ScheduledDateTime,
                a.Investigation.OrganizationId,
                a.Investigation.Organization.Name,
                a.Investigation.CaseId,
                a.Investigation.Case == null
                    ? null
                    : "#" + a.Investigation.Case.CaseYear + "-" + a.Investigation.Case.OrgCaseNumber,
                a.Investigation.PlaceId,
                a.Investigation.Place == null ? null : a.Investigation.Place.Name,
                a.Investigation.Place == null ? null : a.Investigation.Place.City,
                a.Investigation.Place == null ? null : a.Investigation.Place.State,
                a.Investigation.Latitude,
                a.Investigation.Longitude,
                a.Investigation.GeocodeNote,
                a.IsLead))
            .ToListAsync(ct);

        return Ok(rows);
    }

    [HttpPut("{attendeeId:guid}/rsvp")]
    public async Task<ActionResult> UpdateRsvp(Guid attendeeId, [FromBody] UpdateMyRsvpRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var attendee = await db.InvestigationAttendees
            .Include(a => a.Investigation)
            .FirstOrDefaultAsync(a => a.Id == attendeeId, ct);
        if (attendee is null) return NotFound();
        if (attendee.AppUserId != userId) return Forbid();

        // RSVP is only meaningful before the investigation takes place.
        if (attendee.Investigation.ScheduledDateTime < DateTime.UtcNow)
            return UnprocessableEntity("This investigation has already taken place; RSVP can no longer be changed.");

        attendee.Rsvp = request.Rsvp;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public sealed record MyInvestigationItem(
    Guid                AttendeeId,
    Guid                InvestigationId,
    // Null for a visit with no client case — a group going to a landmark on its own account. The
    // three case fields travel together: all three are set, or all three are null.
    Guid?               CaseId,
    string?             CaseReference,
    string?             CaseTitle,
    // Read from the investigation itself rather than through the case, so a case-less visit still
    // says which group ran it.
    Guid                OrgId,
    string              OrgName,
    string              OrgUrlName,
    string              Title,
    DateTime            ScheduledDateTime,
    DateTime?           EndDateTime,
    string?             Location,
    InvestigationStatus Status,
    string?             AssignedRole,
    RsvpStatus          Rsvp,
    bool?               DidAttend,
    DateTime?           EvidenceDueDate);

public sealed record UpdateMyRsvpRequest(RsvpStatus Rsvp);

/// <summary>
/// One investigation a person actually attended, for their own map and list.
/// </summary>
/// <remarks>
/// Carries no permission flags, unlike the organization's row: this list is one person's own
/// history, and there is nothing here to edit from it. What they may change about an investigation
/// is decided where investigations are managed, not here.
/// </remarks>
public sealed record AttendedInvestigationItem(
    Guid InvestigationId,
    string Title,
    DateTime ScheduledDateTime,
    Guid OrganizationId,
    string OrganizationName,
    Guid? CaseId,
    string? CaseReference,
    Guid? PlaceId,
    string? PlaceName,
    string? PlaceCity,
    string? PlaceState,
    decimal? Latitude,
    decimal? Longitude,
    string? GeocodeNote,
    bool WasLead);
