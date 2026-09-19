using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Data.WebApi.Services.Venues;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The venue's side: the groups asking to hold events here, the yeses, and taking one back
/// (item 235 phase 9).
/// </summary>
/// <remarks>
/// <para><b>Gated on the group's settings, deliberately.</b> Every other hosted endpoint uses the
/// event permissions, because deciding who comes to a weekend is not the same job as changing a
/// group's billing. This one is the exception: letting another group publish at your address is a
/// commitment the building makes, and taking it back cancels somebody else's event. Those are the
/// decisions a group keeps for its owners and administrators.</para>
///
/// <para><b>Only the verified venue answers.</b> A group that has since lost the place — because a
/// claim was reversed — is refused in words rather than allowed to grant permission for a building
/// it no longer speaks for.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}")]
public sealed class VenueRequestsController : OrgCmsControllerBase
{
    private readonly PlatformMessageService _messages;
    private readonly HostedEventCalendarSync _sync;

    public VenueRequestsController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security,
        PlatformMessageService messages,
        HostedEventCalendarSync sync)
        : base(dbFactory, mapper, security)
    { _messages = messages; _sync = sync; }

    [HttpGet("venue-requests")]
    public async Task<ActionResult<VenueRequestListRecord>> Get(Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayAnswerAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        return Ok(await ListAsync(db, orgId, null, ct));
    }

    /// <summary>Says yes: a grant for the requested dates, and what goes with it.</summary>
    [HttpPost("venue-requests/{requestId:guid}/approve")]
    public async Task<ActionResult<VenueRequestListRecord>> Approve(
        Guid orgId, Guid requestId, [FromBody] ApproveVenueRequestRequest body, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayAnswerAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var request = await PendingAsync(db, orgId, requestId, ct);
        if (request is null) return NotFound("That request has already been answered, or taken back.");

        var hosted = request.HostedEvent;
        var venue = await VenueGrants.VerifiedVenueAtAsync(db, hosted.PlaceId, ct);
        if (venue?.OrganizationId != orgId)
            return Conflict("You are no longer the venue at that address, so this isn't yours to answer.");

        var now = DateTime.UtcNow;
        var grant = new OrganizationVenueGrant
        {
            Id = Guid.NewGuid(),
            VenueOrganizationId = orgId,
            GranteeOrganizationId = request.RequestingOrganizationId,
            PlaceId = hosted.PlaceId,
            ValidFrom = request.FromDate.Date,
            ValidTo = request.ToDate.Date,
            AllowRooms = body.AllowRooms,
            AllowHistory = body.AllowHistory,
            AllowStaff = body.AllowStaff,
            DateCreated = now,
            CreatedByAppUserId = userId.Value,
        };
        db.OrganizationVenueGrants.Add(grant);

        request.Status = VenueHostingRequestStatus.Approved;
        request.DecidedUtc = now;
        request.DecidedByAppUserId = userId.Value;
        request.OrganizationVenueGrantId = grant.Id;
        request.DateUpdated = now;
        request.UpdatedByAppUserId = userId.Value;

        hosted.VenueGrantId = grant.Id;
        hosted.VenueArrangement = HostedEventVenueArrangement.PlatformGrant;
        hosted.DateUpdated = now;
        hosted.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);

        var span = VenueGrants.Span(grant);
        await TellTheOrganizerAsync(db, request, userId.Value,
            $"{venue.Organization.Name} said yes to {hosted.Name}",
            $"<p><strong>{VenueNotices.Safe(venue.Organization.Name)}</strong> has said yes to "
            + $"<strong>{VenueNotices.Safe(hosted.Name)}</strong> being held there on {span}.</p>"
            + $"<p>{WhatTheyLent(grant)}</p>"
            + $"<p><a href=\"/organizations/{request.RequestingOrganizationId}/events/{hosted.Id}\">Open the event</a></p>",
            ct);

        return Ok(await ListAsync(db, orgId,
            $"{request.RequestingOrganization.Name} may hold {hosted.Name} here on {span}. They have been told.", ct));
    }

    /// <summary>Says no, with the reason the organizer is shown.</summary>
    [HttpPost("venue-requests/{requestId:guid}/decline")]
    public async Task<ActionResult<VenueRequestListRecord>> Decline(
        Guid orgId, Guid requestId, [FromBody] VenueReasonRequest body, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayAnswerAsync(userId.Value, orgId, ct)) return Forbid();

        if (Reason(body) is not { } reason)
            return BadRequest("Say why. They will read it, and a no without a reason is the one that gets asked again.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var request = await PendingAsync(db, orgId, requestId, ct);
        if (request is null) return NotFound("That request has already been answered, or taken back.");

        var now = DateTime.UtcNow;
        request.Status = VenueHostingRequestStatus.Declined;
        request.DecidedUtc = now;
        request.DecidedByAppUserId = userId.Value;
        request.DecisionNote = reason;
        request.DateUpdated = now;
        request.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);

        var venueName = await db.Organizations.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.Name).FirstAsync(ct);
        await TellTheOrganizerAsync(db, request, userId.Value,
            $"{venueName} can't host {request.HostedEvent.Name}",
            $"<p><strong>{VenueNotices.Safe(venueName)}</strong> can't host "
            + $"<strong>{VenueNotices.Safe(request.HostedEvent.Name)}</strong>.</p>"
            + $"<p>They said: “{VenueNotices.Safe(reason)}”</p>",
            ct);

        return Ok(await ListAsync(db, orgId, $"{request.RequestingOrganization.Name} has been told, with your reason.", ct));
    }

    /// <summary>
    /// Takes back a yes. Every event still to happen under it stops, and its guests are told.
    /// </summary>
    [HttpPost("venue-grants/{grantId:guid}/revoke")]
    public async Task<ActionResult<VenueRequestListRecord>> Revoke(
        Guid orgId, Guid grantId, [FromBody] VenueReasonRequest body,
        [FromServices] EventGuestMailer mail, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await MayAnswerAsync(userId.Value, orgId, ct)) return Forbid();

        if (Reason(body) is not { } reason)
            return BadRequest("Say why. The organizer and every guest who had a place will read it.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var grant = await db.OrganizationVenueGrants
            .Include(g => g.GranteeOrganization)
            .FirstOrDefaultAsync(g => g.Id == grantId && g.VenueOrganizationId == orgId, ct);
        if (grant is null) return NotFound();
        if (grant.RevokedUtc is not null) return Conflict("That yes has already been taken back.");

        var now = DateTime.UtcNow;
        var stopped = await VenueWithdrawal.WithdrawAsync(db, grant, userId.Value, reason, _sync, now, ct);
        await SaveAndAuditAsync(db, grant, grant.Id, userId.Value, ct);

        var venueName = await db.Organizations.AsNoTracking().Where(o => o.Id == orgId).Select(o => o.Name).FirstAsync(ct);

        // AFTER the save. A withdrawal that failed must not have told anybody their weekend is off.
        var told = 0;
        foreach (var ev in stopped)
            told += await mail.SendVenueWithdrewAsync(db, ev.HostedEventId, venueName, reason, ct);

        var recipients = await VenueNotices.PeopleWhoAnswerForAsync(db, grant.GranteeOrganizationId, ct);
        await _messages.SendAsync(
            $"{venueName} has withdrawn permission",
            $"<p><strong>{VenueNotices.Safe(venueName)}</strong> has withdrawn its yes for {VenueGrants.Span(grant)}.</p>"
            + $"<p>They said: “{VenueNotices.Safe(reason)}”</p>"
            + (stopped.Count == 0
                ? "<p>No published event was resting on it.</p>"
                : "<p>These events have stopped, their guests have been told, and any credit spent on them "
                  + "is back with your group:</p><ul>"
                  + string.Concat(stopped.Select(s => $"<li>{VenueNotices.Safe(s.Name)}</li>")) + "</ul>"),
            recipients, userId.Value, ct);

        var note = stopped.Count switch
        {
            0 => $"Withdrawn. No published event was resting on it; {grant.GranteeOrganization.Name} has been told.",
            1 => $"Withdrawn. {stopped[0].Name} has stopped, {Letters(told)} and {grant.GranteeOrganization.Name} has been told.",
            _ => $"Withdrawn. {stopped.Count} events have stopped, {Letters(told)} and {grant.GranteeOrganization.Name} has been told.",
        };

        return Ok(await ListAsync(db, orgId, note, ct));
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private Task<bool> MayAnswerAsync(Guid userId, Guid orgId, CancellationToken ct)
        => IsCmsAuthorizedAsync(userId, orgId,
            OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct);

    private static Task<VenueHostingRequest?> PendingAsync(BenDataContext db, Guid orgId, Guid requestId, CancellationToken ct)
        => db.VenueHostingRequests
            .Include(r => r.HostedEvent)
            .Include(r => r.RequestingOrganization)
            .FirstOrDefaultAsync(r => r.Id == requestId && r.VenueOrganizationId == orgId
                                   && r.Status == VenueHostingRequestStatus.Pending, ct);

    private static string? Reason(VenueReasonRequest body)
        => body.Reason?.Trim() is { Length: > 0 } r ? (r.Length > 2000 ? r[..2000] : r) : null;

    private static string Letters(int told) => told switch
    {
        0 => "no guest had an address to write to",
        1 => "one guest has been written to",
        _ => $"{told} guests have been written to",
    };

    private static string WhatTheyLent(OrganizationVenueGrant grant)
    {
        var lent = new List<string>();
        if (grant.AllowRooms) lent.Add("you may put their rooms on your plan");
        if (grant.AllowHistory) lent.Add("your event page may tell the building's story from their profile");
        if (grant.AllowStaff) lent.Add("their own people may see who is coming and help on the door");
        return lent.Count == 0
            ? "They said yes to the dates, and lent nothing else."
            : "They also said " + string.Join("; ", lent) + ".";
    }

    private async Task TellTheOrganizerAsync(
        BenDataContext db, VenueHostingRequest request, Guid senderId, string subject, string body, CancellationToken ct)
    {
        var recipients = await VenueNotices.PeopleWhoAnswerForAsync(db, request.RequestingOrganizationId, ct);
        if (!recipients.Contains(request.CreatedByAppUserId)) recipients.Add(request.CreatedByAppUserId);
        await _messages.SendAsync(subject, body, recipients, senderId, ct);
    }

    private static async Task<VenueRequestListRecord> ListAsync(
        BenDataContext db, Guid orgId, string? note, CancellationToken ct)
    {
        var requests = await db.VenueHostingRequests.AsNoTracking()
            .Include(r => r.RequestingOrganization)
            .Include(r => r.HostedEvent).ThenInclude(e => e.Nights)
            .Include(r => r.HostedEvent).ThenInclude(e => e.Place)
            .Where(r => r.VenueOrganizationId == orgId && r.Status != VenueHostingRequestStatus.Withdrawn)
            .ToListAsync(ct);

        var waiting = requests.Where(r => r.Status == VenueHostingRequestStatus.Pending)
            .OrderBy(r => r.DateCreated)
            .Select(r => VenueRecords.Request(r, r.HostedEvent, r.HostedEvent.Place.Name ?? "a place"))
            .ToList();

        var answered = requests.Where(r => r.Status != VenueHostingRequestStatus.Pending)
            .OrderByDescending(r => r.DecidedUtc)
            .Take(50)
            .Select(r => VenueRecords.Request(r, r.HostedEvent, r.HostedEvent.Place.Name ?? "a place"))
            .ToList();

        var grants = await db.OrganizationVenueGrants.AsNoTracking()
            .Include(g => g.GranteeOrganization)
            .Include(g => g.Place)
            .Where(g => g.VenueOrganizationId == orgId)
            .OrderBy(g => g.RevokedUtc != null).ThenBy(g => g.ValidFrom)
            .ToListAsync(ct);

        var grantIds = grants.Select(g => g.Id).ToList();
        var events = await db.HostedEvents.AsNoTracking()
            .Where(e => e.VenueGrantId != null && grantIds.Contains(e.VenueGrantId.Value))
            .Select(e => new { e.VenueGrantId, e.Id, e.Name, e.LifecycleState })
            .ToListAsync(ct);

        return new(
            waiting, answered,
            [.. grants.Select(g => VenueRecords.Grant(g, g.GranteeOrganization.Name, g.Place.Name ?? "a place",
                [.. events.Where(e => e.VenueGrantId == g.Id).Select(e => new VenueGrantEventRecord(e.Id, e.Name, e.LifecycleState))]))],
            note);
    }
}
