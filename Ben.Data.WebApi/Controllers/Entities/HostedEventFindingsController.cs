using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Controllers.Cms;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The organizer's say over an event's findings (Ben, 10/25/2026): whether they're public, and what the event's
/// location shows. See <see cref="EventFindings"/> for who may see them.
/// </summary>
/// <remarks>
/// <para><b>The location's page.</b> The organizer picks pieces, or shares everything public. A guest's piece is
/// shown a week after it was chosen, and the guest is told the day it's chosen: "the guest is given a week to opt
/// out or can choose to opt out before or during the event". A guest's no wins at any time.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/after/findings")]
public sealed class HostedEventFindingsController : OrgCmsControllerBase
{
    /// <summary>How long a guest has, once their piece is chosen, before it shows on the location's page.</summary>
    public static readonly TimeSpan NoticeWeek = TimeSpan.FromDays(7);

    private readonly Services.Access.HostedEventAccess _access;

    public HostedEventFindingsController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security,
        Services.Access.HostedEventAccess access)
        : base(dbFactory, mapper, security)
    { _access = access; }

    [HttpGet]
    public async Task<ActionResult<EventFindingsRecord>> Get(Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await LoadAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();
        return Ok(await DescribeAsync(db, ev, ct));
    }

    /// <summary>Makes the findings public, or takes them back to the people who were there.</summary>
    [HttpPost("{verb:regex(^(publish|unpublish)$)}")]
    public async Task<ActionResult<EventFindingsRecord>> Publish(Guid orgId, Guid eventId, string verb, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await LoadAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();

        var now = DateTime.UtcNow;
        var publish = verb == "publish";
        if (publish && WhyNot(ev, now) is { } why) return BadRequest(why);

        await EventFindings.SetPublishedAsync(db, ev, publish, userId.Value, now, ct);
        // Audited (10/25/2026): who made a night's findings public, or took them back, is worth a record.
        await SaveAndAuditAsync(db, ev, ev.Id, userId.Value, ct);
        return Ok(await DescribeAsync(db, ev, ct));
    }

    /// <summary>What the location's page shows: nothing, the pieces chosen, or everything public.</summary>
    [HttpPut("place")]
    public async Task<ActionResult<EventFindingsRecord>> SetPlace(
        Guid orgId, Guid eventId, [FromQuery] FindingsOnPlace mode, [FromServices] PlatformMessageService messages, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await LoadAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();
        if (mode != FindingsOnPlace.None && WhyNotOnPlace(ev) is { } why) return BadRequest(why);

        var now = DateTime.UtcNow;
        ev.FindingsOnPlace = mode;
        ev.DateUpdated = now;
        ev.UpdatedByAppUserId = userId.Value;
        // Everything public: every piece is chosen now, and each guest gets their week.
        var told = mode == FindingsOnPlace.AllPublic ? await ChooseAllAsync(db, ev, now, ct) : [];
        await SaveAndAuditAsync(db, ev, ev.Id, userId.Value, ct);
        await TellAsync(messages, ev, told, userId.Value, now, ct);
        return Ok(await DescribeAsync(db, ev, ct));
    }

    /// <summary>Chooses one piece for the location's page, or takes it off.</summary>
    [HttpPut("place/{kind:regex(^(evidence|session)$)}/{pieceId:guid}")]
    public async Task<ActionResult<EventFindingsRecord>> ChoosePiece(
        Guid orgId, Guid eventId, string kind, Guid pieceId, [FromQuery] bool show,
        [FromServices] PlatformMessageService messages, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await _access.CanEditEventAsync(userId.Value, orgId, ct)) return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        var ev = await LoadAsync(db, orgId, eventId, ct);
        if (ev is null) return NotFound();
        if (show && WhyNotOnPlace(ev) is { } why) return BadRequest(why);

        var now = DateTime.UtcNow;
        Guid? guest = null;
        if (show && ev.FindingsOnPlace == FindingsOnPlace.None) ev.FindingsOnPlace = FindingsOnPlace.Chosen;
        if (kind == "evidence")
        {
            var piece = await db.EventEvidenceSubmissions.FirstOrDefaultAsync(
                s => s.Id == pieceId && s.OrgCalendarEvent.HostedEventId == ev.Id && s.Status == EvidenceSubmissionStatus.Accepted, ct);
            if (piece is null) return NotFound();
            if (show && piece.OnPlaceChosenUtc is null) { piece.OnPlaceChosenUtc = now; guest = piece.SubmittedByAppUserId; }
            if (!show) piece.OnPlaceChosenUtc = null;
            await SaveAndAuditAsync(db, piece, piece.Id, userId.Value, ct);
        }
        else
        {
            var piece = await db.FieldSessionUploads.FirstOrDefaultAsync(s => s.Id == pieceId && s.HostedEventId == ev.Id, ct);
            if (piece is null) return NotFound();
            if (show && piece.OnPlaceChosenUtc is null) { piece.OnPlaceChosenUtc = now; guest = piece.SubmittedByAppUserId; }
            if (!show) piece.OnPlaceChosenUtc = null;
            await SaveAndAuditAsync(db, piece, piece.Id, userId.Value, ct);
        }
        if (guest is { } g) await TellAsync(messages, ev, [g], userId.Value, now, ct);
        return Ok(await DescribeAsync(db, ev, ct));
    }

    // ── shared ──────────────────────────────────────────────────────────────

    private static Task<HostedEvent?> LoadAsync(BenDataContext db, Guid orgId, Guid eventId, CancellationToken ct)
        => db.HostedEvents.Include(e => e.Place).Include(e => e.Nights)
            .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct);

    private static string? WhyNot(HostedEvent ev, DateTime now)
        => EventFindings.WhyNotPublish(ev, ev.Place?.Kind,
            HostedEventCalendarSync.Window(ev, [.. ev.Nights.OrderBy(n => n.Date)]).StartUtc, now);

    private static string? WhyNotOnPlace(HostedEvent ev)
    {
        if (ev.Place?.Kind != PlaceKind.PublicLocation)
            return "Only an event at a public place can show its findings on the place's page.";
        if (ev.FindingsPublishedUtc is null)
            return "Make the findings public first. The place's page only shows what anybody may see.";
        return null;
    }

    /// <summary>Chooses every accepted piece and every shown session not yet chosen; returns whose they were.</summary>
    private static async Task<List<Guid>> ChooseAllAsync(BenDataContext db, HostedEvent ev, DateTime now, CancellationToken ct)
    {
        var guests = new List<Guid>();
        foreach (var s in await db.EventEvidenceSubmissions.Where(s => s.OrgCalendarEvent.HostedEventId == ev.Id
                     && s.Status == EvidenceSubmissionStatus.Accepted && s.OnPlaceChosenUtc == null).ToListAsync(ct))
        { s.OnPlaceChosenUtc = now; guests.Add(s.SubmittedByAppUserId); }
        foreach (var u in await db.FieldSessionUploads.Where(u => u.HostedEventId == ev.Id
                     && u.ShownWithEventResultsAtUtc != null && u.OnPlaceChosenUtc == null).ToListAsync(ct))
        { u.OnPlaceChosenUtc = now; guests.Add(u.SubmittedByAppUserId); }
        return guests;
    }

    /// <summary>Tells each guest, once, that their findings are to be shown on the location's page, and how to say no.</summary>
    internal static async Task TellAsync(PlatformMessageService messages, HostedEvent ev, IReadOnlyCollection<Guid> guests,
        Guid senderId, DateTime now, CancellationToken ct)
    {
        var to = guests.Distinct().ToList();
        if (to.Count == 0) return;
        var place = ev.Place?.Name ?? "the event's place";
        await messages.SendAsync(
            $"Your findings from {ev.Name} on {place}'s page",
            $"The organizers would like to show what you sent from {ev.Name} on {place}'s page, from "
            + $"{now.Add(NoticeWeek):MM/dd/yyyy}. If you'd rather they didn't, open your evidence and sessions on the "
            + "website and choose Keep it off — any time, before or after it shows.",
            to, senderId, ct);
    }

    private static async Task<EventFindingsRecord> DescribeAsync(BenDataContext db, HostedEvent ev, CancellationToken ct)
    {
        var bookingsOff = await db.HostedEventBookings.AsNoTracking()
            .Where(b => b.HostedEventId == ev.Id && b.FindingsOffPlaceUtc != null)
            .Select(b => b.LeadAppUserId).ToListAsync(ct);

        var evidence = await db.EventEvidenceSubmissions.AsNoTracking()
            .Where(s => s.OrgCalendarEvent.HostedEventId == ev.Id && s.Status == EvidenceSubmissionStatus.Accepted)
            .Select(s => new { s.Id, s.UploadFile.FileName, Guest = s.SubmittedByAppUser.DisplayName, s.SubmittedByAppUserId, s.OnPlaceChosenUtc, s.PlaceOptOutUtc })
            .ToListAsync(ct);
        var sessions = await db.FieldSessionUploads.AsNoTracking()
            .Where(u => u.HostedEventId == ev.Id && u.ShownWithEventResultsAtUtc != null)
            .Select(u => new { u.Id, Title = u.LocationLabel, Guest = u.SubmittedByAppUser.DisplayName, u.SubmittedByAppUserId, u.OnPlaceChosenUtc, u.PlaceOptOutUtc })
            .ToListAsync(ct);

        var pieces = evidence.Select(e => new EventFindingPieceRecord(e.Id, "evidence", e.FileName, e.Guest, e.OnPlaceChosenUtc,
                         e.OnPlaceChosenUtc?.Add(NoticeWeek), e.PlaceOptOutUtc != null || bookingsOff.Contains(e.SubmittedByAppUserId)))
            .Concat(sessions.Select(u => new EventFindingPieceRecord(u.Id, "session", u.Title ?? "Field session", u.Guest, u.OnPlaceChosenUtc,
                         u.OnPlaceChosenUtc?.Add(NoticeWeek), u.PlaceOptOutUtc != null || bookingsOff.Contains(u.SubmittedByAppUserId))))
            .ToList();

        return new EventFindingsRecord(ev.FindingsPublishedUtc, ev.FindingsOnPlace, ev.Place?.Name,
            ev.Place?.Kind == PlaceKind.PublicLocation, WhyNot(ev, DateTime.UtcNow), pieces);
    }
}
