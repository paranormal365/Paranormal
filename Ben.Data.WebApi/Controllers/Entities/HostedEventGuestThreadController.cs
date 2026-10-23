using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Events;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The organizers' side of guests writing to them about an event (event-scenario-gaps). See
/// <see cref="EventGuestThreads"/>.
/// </summary>
/// <remarks>
/// Read and answered by whoever may decide the event's bookings, the same people the guest's message is
/// addressed to, so an answer can never come from somebody the guest's question never reached.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/organizations/{orgId:guid}/events/{eventId:guid}/guest-messages")]
public sealed class HostedEventGuestThreadController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly Services.Access.HostedEventAccess _access;

    public HostedEventGuestThreadController(IDbContextFactory<BenDataContext> dbFactory, Services.Access.HostedEventAccess access)
    { _dbFactory = dbFactory; _access = access; }

    /// <summary>Every guest's conversation, the newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EventGuestThreadSummaryRecord>>> List(Guid orgId, Guid eventId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (await GateAsync(db, orgId, eventId, ct) is { } refused) return refused;

        var threads = await db.OrgMessages.AsNoTracking()
            .Where(m => m.HostedEventId == eventId && m.ParentMessageId == null && m.ChannelType == OrgMessageChannel.EventGuestThread)
            .Select(m => new
            {
                m.Id, m.AuthorAppUserId, Name = m.AuthorAppUser.DisplayName,
                Last = db.OrgMessages.Where(r => r.ParentMessageId == m.Id).OrderByDescending(r => r.DateCreated)
                    .Select(r => new { r.Body, r.DateCreated, r.AuthorAppUserId }).FirstOrDefault(),
                Count = db.OrgMessages.Count(r => r.ParentMessageId == m.Id),
            })
            .ToListAsync(ct);

        var standings = await EventGuestThreads.StandingsAsync(db, eventId, [.. threads.Select(t => t.AuthorAppUserId)], ct);
        return Ok(threads.Where(t => t.Last is not null)
            .OrderByDescending(t => t.Last!.DateCreated)
            .Select(t => new EventGuestThreadSummaryRecord(
                t.Id, t.AuthorAppUserId, t.Name ?? "A guest", t.Last!.Body, t.Last.DateCreated,
                t.Last.AuthorAppUserId == t.AuthorAppUserId, t.Count, standings[t.AuthorAppUserId]))
            .ToList());
    }

    /// <summary>One guest's conversation, marked read for the reader.</summary>
    [HttpGet("{guestId:guid}")]
    public async Task<ActionResult<EventGuestThreadRecord>> Get(Guid orgId, Guid eventId, Guid guestId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (await GateAsync(db, orgId, eventId, ct) is { } refused) return refused;
        var ev = await db.HostedEvents.AsNoTracking().FirstAsync(e => e.Id == eventId, ct);

        var root = await EventGuestThreads.RootAsync(db, eventId, guestId, ct);
        if (root is null) return NotFound();
        var userId = GetCurrentUserId();
        await EventGuestThreads.MarkReadAsync(db, root, userId, DateTime.UtcNow, ct);
        return Ok(await EventGuestThreads.RecordAsync(db, ev, guestId, root, userId, null, ct,
            standing: await StandingAsync(db, eventId, guestId, ct)));
    }

    /// <summary>Answers a guest.</summary>
    [HttpPost("{guestId:guid}")]
    public async Task<ActionResult<EventGuestThreadRecord>> Answer(
        Guid orgId, Guid eventId, Guid guestId, [FromBody] PostEventGuestThreadRequest request, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        if (await GateAsync(db, orgId, eventId, ct) is { } refused) return refused;
        var ev = await db.HostedEvents.AsNoTracking().FirstAsync(e => e.Id == eventId, ct);

        var root = await EventGuestThreads.RootAsync(db, eventId, guestId, ct);
        if (root is null) return NotFound();
        var now = DateTime.UtcNow;
        if (EventGuestThreads.WhyClosed(ev, now) is { } closed) return Conflict(closed);
        var (body, why) = EventGuestThreads.Clean(request.Body);
        if (body is null) return BadRequest(why);

        var userId = GetCurrentUserId();
        var audience = (await EventBookingRecipients.ForAsync(db, _access, ev, ct)).Select(r => r.AppUserId).ToList();
        root = await EventGuestThreads.WriteAsync(db, ev, guestId, guestName: "", root, userId, body, audience, now, ct);
        return Ok(await EventGuestThreads.RecordAsync(db, ev, guestId, root, userId, "Sent. They'll see it on the event's page and in the app.", ct,
            standing: await StandingAsync(db, eventId, guestId, ct)));
    }

    private static async Task<string> StandingAsync(BenDataContext db, Guid eventId, Guid guestId, CancellationToken ct)
        => (await EventGuestThreads.StandingsAsync(db, eventId, [guestId], ct))[guestId];

    private async Task<ActionResult?> GateAsync(BenDataContext db, Guid orgId, Guid eventId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (!await db.HostedEvents.AnyAsync(e => e.Id == eventId && e.OrganizationId == orgId, ct)) return NotFound();
        if (!await _access.CanDecideBookingsAsync(userId, orgId, eventId, db, ct)) return Forbid();
        return null;
    }
}
