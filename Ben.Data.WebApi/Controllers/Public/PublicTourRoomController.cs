using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Tours;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// A tour date's room: the people checked in on the walk and its guides (event-scenario-gaps). See
/// <see cref="TourRoom"/> for who is in it.
/// </summary>
/// <remarks>
/// Anybody not in the room is told nothing: the read is a 404 indistinguishable from a date that does
/// not exist, so the room cannot be used to find out who was on a walk.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/public/tour-dates/{dateId:guid}/room")]
public sealed class PublicTourRoomController : BenControllerBase
{
    private const int Page = 100;

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IOrganizationSecurityService _security;

    public PublicTourRoomController(IDbContextFactory<BenDataContext> dbFactory, IOrganizationSecurityService security)
    { _dbFactory = dbFactory; _security = security; }

    /// <summary>The room, or nothing (204) for somebody who isn't in it.</summary>
    /// <remarks>
    /// Nothing rather than a refusal, so the tour date's page can ask for every reader and draw the
    /// room only for the people in it; the answer is the same whoever else was on the walk.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<TourRoomRecord>> Get(Guid dateId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (date, standing) = await LoadAsync(db, dateId, ct);
        if (date is null) return NotFound();
        if (!standing.IsMember) return NoContent();
        return Ok(await RoomAsync(db, date, standing, null, ct));
    }

    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(Services.RateLimiting.HostedBookingPolicy)]
    [HttpPost]
    public async Task<ActionResult<TourRoomRecord>> Post(Guid dateId, [FromBody] PostTourRoomMessageRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (date, standing) = await LoadAsync(db, dateId, ct);
        if (date is null || !standing.IsMember) return NotFound();

        if (TourRoom.WhyClosed(date, DateTime.UtcNow) is { } closed) return Conflict(closed);

        var body = request.Body?.Trim();
        if (string.IsNullOrEmpty(body)) return BadRequest("Write something first.");
        if (body.Length > TourRoom.MaxBody) return BadRequest($"Keep it under {TourRoom.MaxBody:N0} characters.");

        db.OrgMessages.Add(new OrgMessage
        {
            Id = Guid.NewGuid(),
            ChannelType = OrgMessageChannel.TourRoom,
            OrgCalendarEventId = date.Id,
            AuthorAppUserId = userId,
            Body = body,
            IsPublic = false,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync(ct);
        return Ok(await RoomAsync(db, date, standing, null, ct));
    }

    /// <summary>Takes a post down: its author, or the people running the walk.</summary>
    [HttpDelete("messages/{messageId:guid}")]
    public async Task<ActionResult<TourRoomRecord>> Remove(Guid dateId, Guid messageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var (date, standing) = await LoadAsync(db, dateId, ct);
        if (date is null || !standing.IsMember) return NotFound();

        var message = await db.OrgMessages.FirstOrDefaultAsync(
            m => m.Id == messageId && m.OrgCalendarEventId == dateId && m.ChannelType == OrgMessageChannel.TourRoom, ct);
        if (message is null) return NotFound();
        if (message.AuthorAppUserId != userId && !standing.IsTeam) return Conflict("You can only take down your own posts.");

        db.OrgMessages.Remove(message);
        await db.SaveChangesAsync(ct);
        return Ok(await RoomAsync(db, date, standing, "Taken down.", ct));
    }

    private async Task<(OrgCalendarEvent? Date, TourRoom.Standing Standing)> LoadAsync(
        BenDataContext db, Guid dateId, CancellationToken ct)
    {
        var date = await db.OrgCalendarEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == dateId && e.TourId != null, ct);
        if (date is null) return (null, TourRoom.Standing.Nobody);
        return (date, await TourRoom.StandingAsync(db, _security, date, GetCurrentUserId(), ct));
    }

    private async Task<TourRoomRecord> RoomAsync(
        BenDataContext db, OrgCalendarEvent date, TourRoom.Standing standing, string? note, CancellationToken ct)
    {
        var viewerId = GetCurrentUserId();
        var rows = await db.OrgMessages.AsNoTracking()
            .Where(m => m.OrgCalendarEventId == date.Id && m.ChannelType == OrgMessageChannel.TourRoom)
            .OrderByDescending(m => m.DateCreated)
            .Take(Page)
            .Select(m => new { m.Id, m.AuthorAppUserId, AuthorName = m.AuthorAppUser.DisplayName ?? "A guest", m.Body, m.DateCreated })
            .ToListAsync(ct);

        var roles = await TourRoom.RolesAsync(db, date, [.. rows.Select(r => r.AuthorAppUserId).Distinct()], ct);
        var whyClosed = TourRoom.WhyClosed(date, DateTime.UtcNow);

        return new TourRoomRecord(
            date.Id, date.Title, whyClosed is null, whyClosed, standing.Role,
            [.. rows.Select(r => new TourRoomMessageRecord(
                r.Id, r.AuthorAppUserId, r.AuthorName, r.Body, r.DateCreated,
                r.AuthorAppUserId == viewerId, roles.GetValueOrDefault(r.AuthorAppUserId)))],
            note);
    }
}
