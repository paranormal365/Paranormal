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
/// The organizer's side of a venue on this site: is there one, what has it said, and asking it
/// (item 235 phase 9).
/// </summary>
/// <remarks>
/// <b>Asked about one event, for its exact nights.</b> The request copies the dates at the moment
/// of asking, so the venue answers a question it can check against its own diary, and a night added
/// afterwards is a new question rather than one the old yes silently stretches to cover.
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/venue")]
public sealed class EventVenueController : OrgCmsControllerBase
{
    private readonly Services.Access.HostedEventAccess _access;
    private readonly PlatformMessageService _messages;

    public EventVenueController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access,
        PlatformMessageService messages)
        : base(dbFactory, mapper, security)
    { _access = access; _messages = messages; }

    [HttpGet]
    public async Task<ActionResult<EventVenueRecord>> Get(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanReadEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await LoadEventAsync(db, orgId, eventId, ct);
        if (hosted is null) return NotFound();

        return Ok(await DescribeAsync(db, hosted, ct));
    }

    /// <summary>Asks the verified venue at this event's place whether it may happen there.</summary>
    [HttpPost("ask")]
    public async Task<ActionResult<EventVenueRecord>> Ask(
        Guid orgId, Guid eventId, [FromBody] AskTheVenueRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await LoadEventAsync(db, orgId, eventId, ct);
        if (hosted is null) return NotFound();

        var current = await DescribeAsync(db, hosted, ct);
        if (!current.CanAsk) return Conflict(current.WhyNotAsk ?? "There is nobody to ask about this event.");

        var dates = hosted.Nights.Select(n => n.Date.Date).OrderBy(d => d).ToList();
        var now = DateTime.UtcNow;
        var message = request.Message?.Trim() is { Length: > 0 } m ? m : null;
        if (message is { Length: > 2000 })
            return BadRequest("Keep the message to the venue under 2,000 characters.");

        db.VenueHostingRequests.Add(new VenueHostingRequest
        {
            Id = Guid.NewGuid(),
            HostedEventId = hosted.Id,
            RequestingOrganizationId = orgId,
            VenueOrganizationId = current.VenueOrganizationId!.Value,
            FromDate = dates[0],
            ToDate = dates[^1],
            Message = message,
            DateCreated = now,
            CreatedByAppUserId = userId.Value,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // The filtered index: a second tab asked in the same moment. Not an error the person made.
            return Conflict($"You have already asked {current.VenueName}. They haven't answered yet.");
        }

        var askers = await db.Organizations.AsNoTracking()
            .Where(o => o.Id == orgId).Select(o => o.Name).FirstAsync(ct);
        var recipients = await VenueNotices.PeopleWhoAnswerForAsync(db, current.VenueOrganizationId.Value, ct);
        await _messages.SendAsync(
            $"{askers} would like to hold {hosted.Name} at {hosted.Place.Name}",
            $"<p><strong>{VenueNotices.Safe(askers)}</strong> would like to hold "
            + $"<strong>{VenueNotices.Safe(hosted.Name)}</strong> at {VenueNotices.Safe(hosted.Place.Name)} "
            + $"on {Dates(dates)}.</p>"
            + (message is null ? "" : $"<p>They wrote: “{VenueNotices.Safe(message)}”</p>")
            + $"<p><a href=\"/organizations/{current.VenueOrganizationId}/venue-requests\">Answer them</a></p>",
            recipients, userId.Value, ct);

        return Ok(await DescribeAsync(db, hosted, ct));
    }

    /// <summary>Takes back a question the venue has not answered yet.</summary>
    [HttpPost("withdraw")]
    public async Task<ActionResult<EventVenueRecord>> Withdraw(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var hosted = await LoadEventAsync(db, orgId, eventId, ct);
        if (hosted is null) return NotFound();

        var pending = await db.VenueHostingRequests
            .FirstOrDefaultAsync(r => r.HostedEventId == eventId && r.Status == VenueHostingRequestStatus.Pending, ct);
        if (pending is null) return Conflict("There is no unanswered question to take back.");

        pending.Status = VenueHostingRequestStatus.Withdrawn;
        pending.DateUpdated = DateTime.UtcNow;
        pending.UpdatedByAppUserId = userId.Value;
        await db.SaveChangesAsync(ct);

        return Ok(await DescribeAsync(db, hosted, ct));
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private static Task<HostedEvent?> LoadEventAsync(BenDataContext db, Guid orgId, Guid eventId, CancellationToken ct)
        => db.HostedEvents.AsNoTracking()
            .Include(e => e.Nights)
            .Include(e => e.Place)
            .Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);

    private static async Task<EventVenueRecord> DescribeAsync(BenDataContext db, HostedEvent hosted, CancellationToken ct)
    {
        var venue = await VenueGrants.VerifiedVenueAtAsync(db, hosted.PlaceId, ct);
        var placeName = hosted.Place?.Name ?? "this place";

        if (venue is null)
            return new(null, null, null, false, null, null, null, false,
                $"Nobody runs {placeName} as their venue on this site, so there is nobody here to ask.");

        if (venue.OrganizationId == hosted.OrganizationId)
            return new(venue.OrganizationId, venue.Organization.Name, venue.Id, true, null, null, null, false,
                "This is your own venue. Nobody else needs to say yes.");

        var latest = await db.VenueHostingRequests.AsNoTracking()
            .Include(r => r.RequestingOrganization)
            .Where(r => r.HostedEventId == hosted.Id && r.VenueOrganizationId == venue.OrganizationId)
            .OrderByDescending(r => r.DateCreated)
            .FirstOrDefaultAsync(ct);

        var onTheSite = await VenueGrants.ForEventAsync(db, hosted, ct);
        var item = HostedEventReadiness.Describe(hosted, onTheSite).First(i => i.Area == "Venue");
        var problem = item.Done ? null : item.Sentence;

        string? whyNot = null;
        if (HostedEventStates.CalledOff.Contains(hosted.LifecycleState)
            || hosted.LifecycleState is HostedEventLifecycleState.Ended or HostedEventLifecycleState.Archived)
            whyNot = "This event is over or called off, so there is nothing to ask the venue.";
        else if (hosted.Nights.Count == 0)
            whyNot = "Give the event its dates first. The venue is asked about exact nights.";
        else if (latest is { Status: VenueHostingRequestStatus.Pending })
            whyNot = $"You asked {venue.Organization.Name} on {latest.DateCreated:MM/dd/yyyy} and they haven't answered yet.";
        else if (item.Done)
            whyNot = $"{venue.Organization.Name} has already said yes to these dates.";

        return new(
            venue.OrganizationId, venue.Organization.Name, venue.Id, false,
            latest is null ? null : VenueRecords.Request(latest, hosted, placeName),
            onTheSite?.Grant is { } grant ? VenueRecords.Grant(grant, hosted.Organization?.Name ?? "", placeName, []) : null,
            problem, whyNot is null, whyNot);
    }

    private static string Dates(IReadOnlyList<DateTime> dates)
        => dates.Count == 1 ? dates[0].ToString("MM/dd/yyyy") : $"{dates[0]:MM/dd/yyyy}–{dates[^1]:MM/dd/yyyy}";
}
