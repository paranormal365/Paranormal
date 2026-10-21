using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// A guest's own conversation with the people running an event, on the website and in the app
/// (event-scenario-gaps). See <see cref="EventGuestThreads"/>.
/// </summary>
/// <remarks>
/// Somebody with no booking at the event is told nothing (204), so the event page can ask for every reader
/// and draw the conversation only for its guests.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/public/hosted-events/{eventId:guid}/organizer-thread")]
public sealed class PublicEventGuestThreadController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly Services.Access.HostedEventAccess _access;

    public PublicEventGuestThreadController(IDbContextFactory<BenDataContext> dbFactory, Services.Access.HostedEventAccess access)
    { _dbFactory = dbFactory; _access = access; }

    /// <summary>The guest's conversation (empty before their first message), or nothing for anybody else.</summary>
    [HttpGet]
    public async Task<ActionResult<EventGuestThreadRecord>> Get(Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null) return NotFound();
        if (userId == Guid.Empty || !await EventGuestThreads.IsGuestAsync(db, eventId, userId, ct)) return NoContent();

        var root = await EventGuestThreads.RootAsync(db, eventId, userId, ct);
        if (root is not null) await EventGuestThreads.MarkReadAsync(db, root, userId, DateTime.UtcNow, ct);
        return Ok(await EventGuestThreads.RecordAsync(db, ev, userId, root, userId, null, ct));
    }

    /// <summary>Writes to the organizers.</summary>
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Services.RateLimiting.HostedBookingPolicy)]
    [HttpPost]
    public async Task<ActionResult<EventGuestThreadRecord>> Post(Guid eventId, [FromBody] PostEventGuestThreadRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var ev = await db.HostedEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == eventId, ct);
        if (ev is null || userId == Guid.Empty || !await EventGuestThreads.IsGuestAsync(db, eventId, userId, ct)) return NotFound();

        var now = DateTime.UtcNow;
        if (EventGuestThreads.WhyClosed(ev, now) is { } closed) return Conflict(closed);
        var (body, refusal) = EventGuestThreads.Clean(request.Body);
        if (body is null) return BadRequest(refusal);

        var guestName = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => u.DisplayName).FirstOrDefaultAsync(ct) ?? "A guest";
        var audience = (await EventBookingRecipients.ForAsync(db, _access, ev, ct)).Select(r => r.AppUserId).ToList();
        var root = await EventGuestThreads.RootAsync(db, eventId, userId, ct);
        root = await EventGuestThreads.WriteAsync(db, ev, userId, guestName, root, userId, body, audience, now, ct);

        return Ok(await EventGuestThreads.RecordAsync(db, ev, userId, root, userId,
            $"Sent. {(await db.Organizations.AsNoTracking().Where(o => o.Id == ev.OrganizationId).Select(o => o.Name).FirstOrDefaultAsync(ct)) ?? "The organizers"} will answer here.", ct));
    }
}
