using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// A member's desk: next investigation, open cases, unread messages, gear checked out (item 204).
/// </summary>
/// <remarks>
/// <para>One call, because the Home page draws it in one breath. Every query here is the same
/// one another screen already runs — the action-needed banners, the notification bell, My
/// Equipment, My Investigations — so nothing can be shown here that those screens would
/// contradict.</para>
///
/// <para>"Open" is the site's own rule: a case whose status is at most Summarized (Paused sits
/// after Transferred in the enum precisely so it does not count). "Upcoming" is scheduled from now
/// on, or still running. "Checked out" is the CheckedOut state; overdue is that plus a due date in
/// the past, never a state of its own.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/me")]
public sealed class MyDeskController : BenControllerBase
{
    private const int ListCap = 5;
    private readonly IDbContextFactory<BenDataContext> _db;

    public MyDeskController(IDbContextFactory<BenDataContext> db) => _db = db;

    [HttpGet("desk")]
    public async Task<ActionResult<MemberDeskResponse>> GetDesk(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var memberships = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => new { m.OrganizationId, m.Role })
            .ToListAsync(ct);
        var orgIds = memberships.Select(m => m.OrganizationId).Distinct().ToList();
        var adminOrgIds = memberships
            .Where(m => m.Role == OrganizationMemberRole.Owner || m.Role == OrganizationMemberRole.Administrator)
            .Select(m => m.OrganizationId).Distinct().ToList();

        // Next investigation: one this person is on, scheduled from now on or still running.
        var upcoming = db.InvestigationAttendees.AsNoTracking()
            .Where(a => a.AppUserId == userId && a.Rsvp != RsvpStatus.Declined
                     && a.Investigation.Status != InvestigationStatus.Cancelled
                     && (a.Investigation.ScheduledDateTime >= now
                         || (a.Investigation.EndDateTime != null && a.Investigation.EndDateTime >= now)));
        var upcomingCount = await upcoming.CountAsync(ct);
        var next = await upcoming
            .OrderBy(a => a.Investigation.ScheduledDateTime)
            .Select(a => new DeskInvestigation(
                a.Investigation.Id, a.Investigation.Title, a.Investigation.UrlName,
                a.Investigation.ScheduledDateTime, a.Investigation.EndDateTime,
                a.Investigation.OrganizationId, a.Investigation.Organization.Name, a.Investigation.Organization.UrlName,
                a.Investigation.Location, a.IsLead,
                a.Investigation.Attendees.Count(x => x.Rsvp != RsvpStatus.Declined)))
            .FirstOrDefaultAsync(ct);

        // Open cases in this person's groups, the ones they are a contact on first.
        var myContactCaseIds = await db.CaseContacts.AsNoTracking()
            .Where(c => c.AppUserId == userId).Select(c => c.CaseId).ToListAsync(ct);
        var openCases = db.Cases.AsNoTracking()
            .Where(c => orgIds.Contains(c.OrganizationId) && c.Status <= CaseStatus.Summarized);
        var openCaseCount = await openCases.CountAsync(ct);
        var cases = await openCases
            .OrderByDescending(c => myContactCaseIds.Contains(c.Id))
            .ThenByDescending(c => c.DateCaseOpened)
            .Take(ListCap)
            .Select(c => new DeskCase(
                c.Id, c.Title, c.UrlName, c.CaseYear, c.OrgCaseNumber, c.Status.ToString(),
                c.OrganizationId, c.Organization.Name, c.Organization.UrlName,
                c.DateCaseOpened, myContactCaseIds.Contains(c.Id)))
            .ToListAsync(ct);

        // Unread: the bell's own count, computed the bell's own way.
        var unread = await db.OrgMessageRecipients.AsNoTracking()
            .CountAsync(r => r.RecipientAppUserId == userId && r.DateRead == null, ct);

        // Gear in this person's hands.
        var checkedOut = db.EquipmentCheckouts.AsNoTracking()
            .Where(k => k.BorrowerAppUserId == userId && k.Status == EquipmentCheckoutStatus.CheckedOut);
        var gearCount = await checkedOut.CountAsync(ct);
        var overdue = await checkedOut.CountAsync(k => k.DateDue != null && k.DateDue < now, ct);
        var gear = await checkedOut
            .OrderBy(k => k.DateDue ?? DateTime.MaxValue)
            .Take(ListCap)
            .Select(k => new DeskGear(
                k.Id, k.EquipmentItemId, k.EquipmentItem.DisplayName,
                k.EquipmentItem.OwningOrganization != null ? k.EquipmentItem.OwningOrganization.Name : null,
                k.DateCheckedOut, k.DateDue, k.DateDue != null && k.DateDue < now))
            .ToListAsync(ct);

        // What the banners count, summed: requests waiting on groups this person can act for.
        var pending = 0;
        if (adminOrgIds.Count > 0)
        {
            // The same three states the action-needed banners count (Pending, Viewed, UnderReview),
            // so the desk and the banner above it never disagree.
            pending += await db.ClientRequestOrganizations.AsNoTracking()
                .CountAsync(a => adminOrgIds.Contains(a.OrganizationId)
                              && (a.Status == ClientOrgRequestStatus.Pending
                               || a.Status == ClientOrgRequestStatus.Viewed
                               || a.Status == ClientOrgRequestStatus.UnderReview), ct);
            pending += await db.OrganizationMembershipRequests.AsNoTracking()
                .CountAsync(r => adminOrgIds.Contains(r.OrganizationId)
                              && r.Status == OrganizationMembershipRequestStatus.Pending, ct);
        }

        return Ok(new MemberDeskResponse(
            orgIds.Count, next, upcomingCount, openCaseCount, cases, unread,
            gearCount, gear, overdue, pending));
    }
}
