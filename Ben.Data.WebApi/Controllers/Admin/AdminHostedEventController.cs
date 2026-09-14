using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Admin;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// The operator's view of every hosted event: the numbers, the list, removal, and appeals (item 235 phase 17b).
/// </summary>
/// <remarks>
/// <para><b>Asked for by Ben on 2026-09-13:</b> a list like the user list — organizer, event, dates — with view and
/// delete, where delete removes the event, credits the credit back to the organizer, and emails a generic rejection
/// with the ability to appeal.</para>
///
/// <para><b>"Delete" is removal, not a purge.</b> The event, its bookings and its history stay, so an appeal has
/// something to restore and a dispute has something to read; see <see cref="HostedEventRemovals"/>.</para>
///
/// <para><b>Both sides are told.</b> Guests with a place get the ordinary not-going-ahead letter with no reason, and
/// the organizer's side gets the generic removal letter with the appeal link. Both go after the save.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/hosted-events")]
public sealed class AdminHostedEventController : BenControllerBase
{
    private const int TopN = 8;

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly HostedEventCalendarSync _sync;
    private readonly EventGuestMailer _mail;
    private readonly PlatformMessageService _messages;

    public AdminHostedEventController(
        IDbContextFactory<BenDataContext> dbFactory, HostedEventCalendarSync sync, EventGuestMailer mail,
        PlatformMessageService messages)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _mail = mail;
        _messages = messages;
    }

    /// <summary>Every hosted event, newest first, with the appeals waiting and those answered in the last month.</summary>
    [HttpGet]
    public async Task<ActionResult<AdminHostedEventsRecord>> Get(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return Ok(await ListAsync(db, null, ct));
    }

    /// <summary>What removing this event would do.</summary>
    [HttpGet("{eventId:guid}/removal-effect")]
    public async Task<ActionResult<AdminHostedEventRemovalEffect>> RemovalEffect(Guid eventId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await HostedEventRemovals.PreviewAsync(db, eventId, ct) is { } effect
            ? Ok(new AdminHostedEventRemovalEffect(effect.AlreadyRemoved, effect.CreditReturns, effect.PartiesWithPlaces, effect.PeopleWithPlaces))
            : NotFound();
    }

    [HttpPost("{eventId:guid}/remove")]
    public async Task<ActionResult<AdminHostedEventsRecord>> Remove(
        Guid eventId, [FromBody] RemoveHostedEventRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (request.Note?.Trim().Length > HostedEventRemovals.MaxNote)
            return BadRequest($"Keep the note under {HostedEventRemovals.MaxNote:N0} characters.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var hosted = await db.HostedEvents.Include(e => e.Nights).Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (hosted is null) return NotFound();

        var now = DateTime.UtcNow;
        var removal = await HostedEventRemovals.RemoveAsync(db, hosted, userId, request.Note, _sync, now, ct);
        if (removal is null) return Conflict("This event has already been removed.");

        await db.SaveChangesAsync(ct);
        if (HttpContext?.RequestServices?.GetService<IAuditLogService>() is { } audit)
            await TryAuditAsync(audit.LogCreateAsync(nameof(HostedEventRemoval), removal.Id, removal, userId, AppSources.WebApi));

        // After the save: the guests first, with no reason, then the organizer's side with the appeal.
        removal.GuestsTold = await _mail.SendCalledOffAsync(db, hosted.Id, reason: null, ct);
        var organizers = await HostedEventRemovals.OrganizerRecipientsAsync(db, hosted, ct);
        await _mail.SendRemovedAsync(hosted, hosted.Organization.Name, removal.CreditReturned, organizers, ct);
        await _messages.SendAsync(
            $"{hosted.Name} was removed from the site",
            $"<p><strong>{Safe(hosted.Name)}</strong> was removed because it doesn't meet our guidelines for hosted events. "
            + (removal.CreditReturned ? "The event credit spent on it has been returned. " : "")
            + "If you think this was a mistake, you can appeal.</p>"
            + $"<p><a href=\"/organizations/{hosted.OrganizationId}/events/{hosted.Id}#event-removed\">Appeal this decision</a></p>",
            [.. organizers.Select(o => o.Id)], userId, ct);
        await db.SaveChangesAsync(ct);

        var said = $"{hosted.Name} is removed."
                 + (removal.CreditReturned ? " Its credit went back to the organizer." : "")
                 + (removal.GuestsTold switch { 0 => "", 1 => " One guest was told.", var n => $" {n} guests were told." })
                 + " The organizer has been sent the letter with the appeal link.";
        return Ok(await ListAsync(db, said, ct));
    }

    [HttpPost("appeals/{removalId:guid}/decide")]
    public async Task<ActionResult<AdminHostedEventsRecord>> Decide(
        Guid removalId, [FromBody] DecideHostedEventAppealRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var removal = await db.HostedEventRemovals.FirstOrDefaultAsync(r => r.Id == removalId, ct);
        if (removal is null) return NotFound();

        var latest = await HostedEventRemovals.LatestAsync(db, removal.HostedEventId, ct);
        if (latest?.Id != removal.Id) return Conflict("The event has been removed again since this appeal. Answer the newer one.");

        // The audit log compares two copies of the same type, so the before-picture is the row itself.
        var before = db.Entry(removal).CurrentValues.ToObject();
        if (await HostedEventRemovals.DecideAsync(db, removal, request.Uphold, request.Note, userId, _sync, DateTime.UtcNow, ct) is { } refusal)
            return BadRequest(refusal);

        await db.SaveChangesAsync(ct);
        if (HttpContext?.RequestServices?.GetService<IAuditLogService>() is { } audit)
            await TryAuditAsync(audit.LogUpdateAsync(nameof(HostedEventRemoval), removal.Id, before, removal, userId, AppSources.WebApi));

        var hosted = await db.HostedEvents.AsNoTracking().Include(e => e.Organization).FirstAsync(e => e.Id == removal.HostedEventId, ct);
        var organizers = await HostedEventRemovals.OrganizerRecipientsAsync(db, hosted, ct);
        await _mail.SendAppealAnsweredAsync(hosted, request.Uphold, removal.DecisionNote, organizers, ct);
        await _messages.SendAsync(
            request.Uphold ? $"{hosted.Name} is back as a draft" : $"Your appeal about {hosted.Name}",
            (request.Uphold
                ? $"<p>Your appeal was upheld. <strong>{Safe(hosted.Name)}</strong> is back as a draft.</p>"
                : $"<p>Your appeal about <strong>{Safe(hosted.Name)}</strong> was reviewed, and the event stays removed.</p>")
            + (removal.DecisionNote is { } note ? $"<p>The reviewer said: “{Safe(note)}”</p>" : "")
            + $"<p><a href=\"/organizations/{hosted.OrganizationId}/events/{hosted.Id}\">Open the event</a></p>",
            [.. organizers.Select(o => o.Id)], userId, ct);

        return Ok(await ListAsync(db,
            request.Uphold ? $"{hosted.Name} is back as a draft, and the organizer has been told."
                           : "Declined. The organizer has been told, with your note.", ct));
    }

    /// <summary>The events dashboard, over a window of days.</summary>
    [HttpGet("stats")]
    public async Task<ActionResult<AdminHostedEventStats>> Stats([FromQuery] int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 7, 365);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return Ok(await HostedEventOversightStats.ReadAsync(db, days, DateTime.UtcNow, TopN, ct));
    }

    /// <summary>
    /// Whether hosted events are working: holds, answers, letters, errors on event addresses, refusals and the
    /// scheduled jobs. For development rather than the business; see <see cref="HostedEventHealthStats"/>.
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult<AdminHostedEventHealth>> Health(
        [FromServices] IConfiguration configuration,
        [FromServices] Services.Scheduling.ScheduledJobLedger ledger,
        [FromQuery] int days = 30, CancellationToken ct = default)
    {
        days = Math.Clamp(days, 7, 365);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var errors = await HostedEventErrorLog.ReadAsync(db, configuration, days, TopN, ct);
        return Ok(await HostedEventHealthStats.ReadAsync(db, days, DateTime.UtcNow, errors, ledger.SinceUtc, ledger.Snapshot(), ct));
    }

    private static string Safe(string text) => System.Net.WebUtility.HtmlEncode(text);

    private static async Task<AdminHostedEventsRecord> ListAsync(BenDataContext db, string? note, CancellationToken ct)
    {
        var events = await db.HostedEvents.AsNoTracking()
            .OrderByDescending(e => e.DateCreated)
            .Take(2000)
            .Select(e => new AdminHostedEventRow(
                e.Id,
                e.OrganizationId,
                e.Organization.Name,
                e.Organization.UrlName,
                e.Name,
                e.UrlName,
                e.CreatedByAppUserId,
                e.CreatedByAppUser.DisplayName ?? e.CreatedByAppUser.Email,
                e.StartsOn,
                e.EndsOn,
                e.LifecycleState,
                db.HostedEventBookings.Where(b => b.HostedEventId == e.Id && b.Status == HostedEventBookingStatus.Confirmed)
                    .Sum(b => (int?)b.PartySize) ?? 0,
                db.HostedEventBookings.Count(b => b.HostedEventId == e.Id
                    && (b.Status == HostedEventBookingStatus.Requested || b.Status == HostedEventBookingStatus.Held)),
                e.Place.Name,
                e.Place.City,
                e.Place.State,
                e.DateCreated,
                db.HostedEventRemovals.Where(r => r.HostedEventId == e.Id).OrderByDescending(r => r.RemovedUtc)
                    .Select(r => (HostedEventAppealState?)r.AppealState).FirstOrDefault()))
            .ToListAsync(ct);

        var monthAgo = DateTime.UtcNow.AddDays(-30);
        var appeals = await db.HostedEventRemovals.AsNoTracking()
            .Where(r => r.AppealState == HostedEventAppealState.Waiting
                     || (r.AppealState != HostedEventAppealState.NotAppealed && r.DecidedUtc > monthAgo))
            .OrderBy(r => r.AppealState == HostedEventAppealState.Waiting ? 0 : 1)
            .ThenBy(r => r.AppealedUtc)
            .Take(200)
            .Select(r => new AdminHostedEventAppealRecord(
                r.Id, r.HostedEventId, r.HostedEvent.OrganizationId, r.HostedEvent.Organization.Name, r.HostedEvent.Name,
                r.HostedEvent.StartsOn, r.PreviousState, r.Note,
                r.RemovedByAppUser.DisplayName ?? r.RemovedByAppUser.Email ?? "Somebody", r.RemovedUtc, r.CreditReturned,
                r.AppealState, r.AppealMessage,
                r.AppealedByAppUser != null ? r.AppealedByAppUser.DisplayName ?? r.AppealedByAppUser.Email : null, r.AppealedUtc,
                r.DecisionNote,
                r.DecidedByAppUser != null ? r.DecidedByAppUser.DisplayName ?? r.DecidedByAppUser.Email : null, r.DecidedUtc))
            .ToListAsync(ct);

        return new AdminHostedEventsRecord(events, appeals, note);
    }
}
