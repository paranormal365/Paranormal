using Ben.Data.Common.Helpers;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// An event's published programme, and a guest signing up for its sessions (item 235 phase 10).
/// </summary>
/// <remarks>
/// <para><b>Anybody may read it</b> once it is published, because what is on is part of what an
/// event is. Who has signed up is nobody's business but the host's: a visitor sees "12 of 15" and
/// never a name.</para>
///
/// <para><b>Signing up</b> is for a guest with a confirmed place, for up to their party's size, or
/// somebody helping at the event. See <see cref="SessionSignUps"/>.</para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/public/hosted-events/{eventId:guid}")]
public sealed class PublicHostedEventProgrammeController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;

    public PublicHostedEventProgrammeController(IDbContextFactory<BenDataContext> dbFactory) => _dbFactory = dbFactory;

    [AllowAnonymous]
    [HttpGet("programme")]
    public async Task<ActionResult<PublicProgrammeRecord>> Get(Guid eventId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var record = await ProgrammeAsync(db, eventId, GetCurrentUserId(), ct);
        return record is null ? NotFound() : Ok(record);
    }

    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Services.RateLimiting.HostedBookingPolicy)]
    [HttpPost("sessions/{sessionId:guid}/sign-up")]
    public async Task<ActionResult<PublicProgrammeRecord>> SignUp(
        Guid eventId, Guid sessionId, [FromBody] SessionSignUpRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            if (!await IsOnTheProgrammeAsync(db, eventId, sessionId, ct)) return NotFound();

            var (bookingId, max, refusal) = await SessionSignUps.EntitlementAsync(db, eventId, userId, ct);
            if (refusal is not null) return Conflict(refusal);
            if (request.People < 1 || request.People > max)
                return BadRequest(max == 1 ? "A sign-up here is for one person." : $"Sign up between 1 and {max} of your party.");

            var outcome = await SessionSignUps.SignUpAsync(_dbFactory, sessionId, userId, bookingId, request.People, DateTime.UtcNow, ct);
            if (outcome.Refusal is not null) return Conflict(outcome.Refusal);
        }

        await using var read = await _dbFactory.CreateDbContextAsync(ct);
        return Ok(await ProgrammeAsync(read, eventId, userId, ct));
    }

    [HttpDelete("sessions/{sessionId:guid}/sign-up")]
    public async Task<ActionResult<PublicProgrammeRecord>> Leave(
        Guid eventId, Guid sessionId, [FromServices] EventGuestMailer mail, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        Guid signUpId;
        await using (var db = await _dbFactory.CreateDbContextAsync(ct))
        {
            if (!await IsOnTheProgrammeAsync(db, eventId, sessionId, ct)) return NotFound();
            signUpId = await db.HostedEventSessionSignUps
                .Where(s => s.HostedEventSessionId == sessionId && s.AppUserId == userId)
                .Select(s => s.Id).FirstOrDefaultAsync(ct);
            if (signUpId == Guid.Empty) return Conflict("You weren't signed up for that one.");
        }

        var (_, promoted) = await SessionSignUps.LeaveAsync(_dbFactory, sessionId, signUpId, userId, DateTime.UtcNow, ct);

        await using var read = await _dbFactory.CreateDbContextAsync(ct);
        if (promoted.Count > 0) await mail.SendSessionPromotedAsync(read, promoted, ct);
        return Ok(await ProgrammeAsync(read, eventId, userId, ct));
    }

    /// <summary>The guest has looked at the programme, so the bell's "it changed" row clears.</summary>
    [HttpPost("programme/seen")]
    public async Task<IActionResult> Seen(Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await db.HostedEventBookings
            .Where(b => b.HostedEventId == eventId && b.LeadAppUserId == userId)
            .ExecuteUpdateAsync(u => u.SetProperty(b => b.ProgrammeSeenUtc, DateTime.UtcNow), ct);
        return NoContent();
    }

    /// <summary>One session as a calendar file, in UTC, so a calendar anywhere shows the venue's time.</summary>
    [AllowAnonymous]
    [HttpGet("sessions/{sessionId:guid}/calendar.ics")]
    public async Task<IActionResult> SessionCalendar(Guid eventId, Guid sessionId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var sessions = await CalendarRowsAsync(db, eventId, sessionId, ct);
        if (sessions.Count == 0) return NotFound();
        return File(IcsBuilder.BuildBytes(sessions), IcsBuilder.ContentType, "session.ics");
    }

    /// <summary>Every session on the programme, as one calendar file.</summary>
    [AllowAnonymous]
    [HttpGet("programme.ics")]
    public async Task<IActionResult> ProgrammeCalendar(Guid eventId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var sessions = await CalendarRowsAsync(db, eventId, null, ct);
        if (sessions.Count == 0) return NotFound();
        return File(IcsBuilder.BuildBytes(sessions), IcsBuilder.ContentType, "programme.ics");
    }

    // ── the plumbing ─────────────────────────────────────────────────────────

    private static Task<bool> IsOnTheProgrammeAsync(BenDataContext db, Guid eventId, Guid sessionId, CancellationToken ct)
        => db.HostedEventSessions.AnyAsync(s => s.Id == sessionId && s.HostedEventId == eventId
            && s.HostedEvent.ProgrammePublishedUtc != null
            && HostedEventStates.OnThePublicSite.Contains(s.HostedEvent.LifecycleState), ct);

    private static async Task<List<IcsBuilder.IcsEvent>> CalendarRowsAsync(BenDataContext db, Guid eventId, Guid? sessionId, CancellationToken ct)
        => (await db.HostedEventSessions.AsNoTracking()
                .Where(s => s.HostedEventId == eventId && s.CalledOffUtc == null
                         && (sessionId == null || s.Id == sessionId)
                         && s.HostedEvent.ProgrammePublishedUtc != null
                         && HostedEventStates.OnThePublicSite.Contains(s.HostedEvent.LifecycleState))
                .OrderBy(s => s.StartsAtUtc)
                .Select(s => new
                {
                    s.Id, s.Title, s.Description, s.StartsAtUtc, s.EndsAtUtc, s.ChangedUtc,
                    Where = s.PlaceRoom != null ? s.PlaceRoom.Name : s.LocationText,
                    EventName = s.HostedEvent.Name, Venue = s.HostedEvent.Place.Name,
                })
                .ToListAsync(ct))
            .Select(s => new IcsBuilder.IcsEvent(
                $"session-{s.Id}@ishaunted", s.StartsAtUtc, s.EndsAtUtc, $"{s.Title} — {s.EventName}",
                Description: s.Description,
                Location: string.Join(", ", new[] { s.Where, s.Venue }.Where(x => !string.IsNullOrWhiteSpace(x))),
                // A later file for the same session replaces the entry rather than adding a second one.
                Sequence: s.ChangedUtc is { } changed ? (int)(changed - DateTime.UnixEpoch).TotalMinutes : 0))
            .ToList();

    private static async Task<PublicProgrammeRecord?> ProgrammeAsync(BenDataContext db, Guid eventId, Guid viewerId, CancellationToken ct)
    {
        var hosted = await db.HostedEvents.AsNoTracking()
            .Include(e => e.Nights)
            .Where(e => e.Id == eventId && e.ProgrammePublishedUtc != null
                     && HostedEventStates.OnThePublicSite.Contains(e.LifecycleState))
            .FirstOrDefaultAsync(ct);
        if (hosted is null) return null;

        var sessions = await db.HostedEventSessions.AsNoTracking()
            .Include(s => s.PlaceRoom)
            .Include(s => s.SignUps)
            .Where(s => s.HostedEventId == eventId)
            .OrderBy(s => s.StartsAtUtc).ThenBy(s => s.SortOrder)
            .ToListAsync(ct);

        var canSignUp = false;
        string? why = null;
        var max = 0;
        DateTime? seen = null;
        if (viewerId != Guid.Empty)
        {
            var (_, maxPeople, refusal) = await SessionSignUps.EntitlementAsync(db, eventId, viewerId, ct);
            canSignUp = refusal is null;
            why = refusal;
            max = maxPeople;
            seen = await db.HostedEventBookings.AsNoTracking()
                .Where(b => b.HostedEventId == eventId && b.LeadAppUserId == viewerId)
                .Select(b => b.ProgrammeSeenUtc).FirstOrDefaultAsync(ct);
        }

        var lastChange = sessions.Max(s => s.ChangedUtc);
        var changedSinceSeen = viewerId != Guid.Empty && canSignUp && lastChange is { } c && (seen is null || c > seen);

        return new PublicProgrammeRecord(
            eventId, hosted.TimeZoneId,
            [.. hosted.Nights.Select(n => n.Date.Date).OrderBy(d => d)],
            [.. sessions.Select(s =>
            {
                var mine = viewerId == Guid.Empty ? null : s.SignUps.FirstOrDefault(u => u.AppUserId == viewerId);
                return new PublicSessionRecord(
                    s.Id, s.Title, s.Description, s.StartsAtUtc, s.EndsAtUtc,
                    s.PlaceRoom?.Name ?? s.LocationText, s.LedBy, s.Capacity, s.RequiresSignUp, s.PlacesTaken,
                    s.CalledOffUtc is not null, s.CancelledReason,
                    Changed: s.ChangedUtc is { } ch && (seen is null || ch > seen),
                    Mine: mine is null ? null : new MySessionPlaceRecord(mine.Id, mine.People, mine.WaitlistedUtc is not null, SessionSignUps.PositionOf(s, mine)));
            })],
            canSignUp, viewerId == Guid.Empty ? null : why, max, changedSinceSeen);
    }
}
