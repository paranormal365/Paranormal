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
                    .ThenInclude(c => c.Organization)
            .OrderByDescending(a => a.Investigation.ScheduledDateTime)
            .ToListAsync(ct);

        return Ok(attendances.Select(a => new MyInvestigationItem(
            AttendeeId:        a.Id,
            InvestigationId:   a.InvestigationId,
            CaseId:            a.Investigation.CaseId,
            CaseReference:     $"#{a.Investigation.Case.CaseYear}-{a.Investigation.Case.OrgCaseNumber:D3}",
            CaseTitle:         a.Investigation.Case.Title,
            OrgId:             a.Investigation.Case.OrganizationId,
            OrgName:           a.Investigation.Case.Organization.Name,
            OrgUrlName:        a.Investigation.Case.Organization.UrlName,
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

    [HttpPut("{attendeeId:guid}/rsvp")]
    public async Task<ActionResult> UpdateRsvp(Guid attendeeId, [FromBody] UpdateMyRsvpRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var attendee = await db.InvestigationAttendees.FindAsync([attendeeId], ct);
        if (attendee is null) return NotFound();
        if (attendee.AppUserId != userId) return Forbid();

        attendee.Rsvp = request.Rsvp;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public sealed record MyInvestigationItem(
    Guid                AttendeeId,
    Guid                InvestigationId,
    Guid                CaseId,
    string              CaseReference,
    string              CaseTitle,
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
