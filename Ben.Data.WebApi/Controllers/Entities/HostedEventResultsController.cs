using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The end of an event: the results letter to everybody who had a place, the QR code it carries,
/// and the ranked results the iPhone app shows (event-scenario-gaps, 10/21/2026).
/// </summary>
/// <remarks>
/// Ben's scenario: <i>"When done, he had created a page using a CMS type builder and sent a final
/// email to the guests with a link to the evidence page and a QR code to jump there directly if they
/// didn't have the iPhone app. They could see the event results in the app."</i>
/// </remarks>
public sealed class HostedEventResultsController : OrgCmsControllerBase
{
    private readonly Services.Access.HostedEventAccess _access;
    private readonly EventGuestMailer _mailer;
    private readonly Ben.Data.Common.SiteIdentity _site;
    private readonly ILogger<HostedEventResultsController> _logger;

    public HostedEventResultsController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access, EventGuestMailer mailer,
        IOptions<Ben.Data.Common.SiteIdentity> site, ILogger<HostedEventResultsController> logger)
        : base(dbFactory, mapper, security)
    {
        _access = access;
        _mailer = mailer;
        _site = site.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends "the results are ready" to every confirmed party that hasn't had it, with a link and a
    /// QR code to the results page.
    /// </summary>
    /// <remarks>
    /// To everybody with a place, whether the door saw them or not: a guest who missed the night was
    /// told in their letter the results would come. Once per party; pressing it again reaches only
    /// the ones it hasn't (a party confirmed late, or a letter that failed).
    /// </remarks>
    [HttpPost("~/api/organizations/{orgId:guid}/events/{eventId:guid}/after/results")]
    public async Task<ActionResult<HostedEventResultsSentRecord>> SendResults(
        Guid orgId, Guid eventId, [FromBody] SendHostedEventResultsRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > 2000 }) return BadRequest("The note is longer than 2,000 characters.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);
        if (ev is null) return NotFound();

        if (WhyResultsCannotGo(ev, _mailer.IsConfigured) is { } refusal) return BadRequest(refusal);

        var resultsUrl = AbsoluteResultsUrl(ev);
        var bookings = await db.HostedEventBookings.Include(b => b.LeadAppUser)
            .Where(b => b.HostedEventId == ev.Id && b.Status == HostedEventBookingStatus.Confirmed)
            .ToListAsync(ct);

        int sent = 0, already = 0, noAddress = 0, failed = 0;
        var now = DateTime.UtcNow;
        foreach (var booking in bookings)
        {
            if (booking.ResultsSentUtc is not null) { already++; continue; }
            booking.HostedEvent = ev;
            try
            {
                if (!await _mailer.SendResultsAsync(booking, resultsUrl, note, ct)) { noAddress++; continue; }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Not stamped, so pressing it again reaches them.
                _logger.LogWarning(e, "Could not send the results to booking {BookingId}.", booking.Id);
                failed++;
                continue;
            }
            booking.ResultsSentUtc = now;
            sent++;
        }

        if (failed == 0) ev.ResultsSentUtc = now;
        await db.SaveChangesAsync(ct);

        if (failed > 0)
            return BadRequest($"{failed} letter(s) couldn't be sent just now. Press it again to send the rest.");

        return Ok(new HostedEventResultsSentRecord(sent, already, noAddress, resultsUrl,
            await HostedEventAfterController.AfterAsync(db, ev, ct)));
    }

    /// <summary>Why the results letter cannot go yet, or null when it can.</summary>
    internal static string? WhyResultsCannotGo(HostedEvent ev, bool mailIsConfigured)
    {
        if (ev.LifecycleState is not (HostedEventLifecycleState.Ended or HostedEventLifecycleState.Archived))
            return "The results letter goes once the event is over.";
        if (!mailIsConfigured)
            return "Email isn't set up on this site, so the letter can't go.";
        return null;
    }

    /// <summary>The results QR code as a picture, for a mail client that strips inline ones.</summary>
    /// <remarks>
    /// Anonymous, because a mail client is. It draws only the event's own results address, which is
    /// public by the time the letter goes.
    /// </remarks>
    [HttpGet("~/api/public/hosted-events/{eventId:guid}/results-qr.png")]
    [AllowAnonymous]
    public async Task<IActionResult> ResultsQr(Guid eventId, [FromQuery] int? size, CancellationToken ct)
    {
        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.AsNoTracking().Include(e => e.Organization)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null || !IsShown(ev)) return NotFound();

        return File(EventPasses.Png(AbsoluteResultsUrl(ev), size ?? 8), "image/png");
    }

    /// <summary>
    /// The event's accepted evidence, ranked, with where its results page is — what the app shows.
    /// </summary>
    [HttpGet("~/api/public/hosted-events/{eventId:guid}/results")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicHostedEventResultsRecord>> Results(Guid eventId, CancellationToken ct)
    {
        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.AsNoTracking().Include(e => e.Organization).Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null || !IsShown(ev)) return NotFound();

        var umbrella = await db.OrgCalendarEvents.AsNoTracking()
            .Where(c => c.HostedEventId == ev.Id && c.IsPublic)
            .Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);

        var items = umbrella is Guid calendarId && ev.CollectsEvidence
            ? await EventEvidenceController.RankedAcceptedAsync(db, calendarId, ct)
            : [];

        return Ok(new PublicHostedEventResultsRecord(
            ev.Id, ev.Name, ev.CollectsEvidence, AbsoluteResultsUrl(ev), ev.AsksForVotes,
            HostedEventEvidence.WindowSentence(ev, [.. ev.Nights]),
            ev.ResultsSentUtc,
            [.. items.Select(i => new PublicHostedEventResultItem(
                i.Id, i.UploadFileId, i.FileName, i.ContentType, i.SubmitterDisplayName, i.Note,
                i.PositiveVotes, i.CaughtAtUtc ?? i.DateCreated,
                _site.ApiAbsoluteUrl($"/api/events/{i.OrgCalendarEventId}/evidence/{i.Id}/file")))]));
    }

    private static bool IsShown(HostedEvent ev)
        => HostedEventStates.OnThePublicSite.Contains(ev.LifecycleState)
        || ev.LifecycleState == HostedEventLifecycleState.Archived;

    private string AbsoluteResultsUrl(HostedEvent ev)
    {
        var path = HostedEventEvidence.ResultsPath(ev, ev.Organization?.UrlName ?? "");
        return path.StartsWith('/') ? _site.AbsoluteUrl(path) : path;
    }
}
