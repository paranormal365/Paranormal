using Ben.Data.Source.Context;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Which room of its place a field session was recorded in, chosen by the person who sent it (11/21/2026).
/// </summary>
/// <remarks>
/// <para>Ben: "I recorded on the Paranormal Platform and want to attach it to my room at the site. Is there a
/// way to tag it or attach it to the location and specify a room?" The phone stamps a typed room name on each
/// reading as it goes; this is the session's room as a whole, set or fixed afterwards, on the website or on
/// the phone after publishing, so the place's page can say which nights were in which room.</para>
///
/// <para><b>Which rooms are offered.</b> Rooms belong to the group that named them (see
/// <see cref="Ben.Data.Source.Entities.PlaceRoom"/>). The ones offered are the place's public rooms and the
/// rooms of the sender's own groups there, active only. A room a group keeps off its page is never offered to
/// a stranger, so naming a session after it can't reveal it.</para>
/// </remarks>
[ApiController]
[Route("api/field-sessions/{id:guid}/room")]
[Authorize]
public sealed class FieldSessionRoomController : BenControllerBase
{
    /// <summary>The longest room name, the same as a room's own.</summary>
    public const int MaxRoomNameLength = 120;

    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IOrganizationSecurityService _security;

    public FieldSessionRoomController(IDbContextFactory<BenDataContext> db, IOrganizationSecurityService security)
    {
        _db = db;
        _security = security;
    }

    /// <summary>The session's room now, and the rooms it may be put in.</summary>
    [HttpGet]
    public async Task<ActionResult<SessionRoomChoices>> Get(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.AsNoTracking()
            .Where(s => s.Id == id && s.SubmittedByAppUserId == userId)
            .Select(s => new { s.PlaceId, PlaceName = s.Place != null ? s.Place.Name : null, s.PlaceRoomId, s.RoomName })
            .FirstOrDefaultAsync(ct);
        if (session is null) return NotFound();

        var rooms = session.PlaceId is Guid placeId ? await OfferedAsync(db, placeId, userId, ct) : [];
        return Ok(new SessionRoomChoices(session.PlaceId, session.PlaceName, session.PlaceRoomId, session.RoomName, rooms));
    }

    /// <summary>Puts the session in a room, by picking one or typing its name, or clears it when both are empty.</summary>
    [HttpPut]
    public async Task<ActionResult<SessionRoomChoices>> Set(Guid id, [FromBody] SetSessionRoomRequest? request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var session = await db.FieldSessionUploads.FirstOrDefaultAsync(s => s.Id == id && s.SubmittedByAppUserId == userId, ct);
        if (session is null) return NotFound();

        var offered = session.PlaceId is Guid placeId ? await OfferedAsync(db, placeId, userId, ct) : [];
        var name = request?.Name?.Trim();
        if (request?.PlaceRoomId is Guid roomId)
        {
            var room = offered.FirstOrDefault(r => r.Id == roomId);
            if (room is null) return BadRequest("That room isn't one of this place's rooms you can pick.");
            session.PlaceRoomId = room.Id;
            session.RoomName = room.Name;
        }
        else if (name is { Length: > 0 })
        {
            if (name.Length > MaxRoomNameLength)
                return BadRequest($"A room's name can be at most {MaxRoomNameLength} characters.");
            // A typed name that is one of the place's rooms is that room, so the place's page counts it there.
            var same = offered.FirstOrDefault(r => string.Equals(r.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            session.PlaceRoomId = same?.Id;
            session.RoomName = same?.Name ?? name;
        }
        else
        {
            session.PlaceRoomId = null;
            session.RoomName = null;
        }

        session.DateUpdated = DateTime.UtcNow;
        session.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        var placeName = session.PlaceId is Guid pid
            ? await db.Places.AsNoTracking().Where(p => p.Id == pid).Select(p => p.Name).FirstOrDefaultAsync(ct)
            : null;
        return Ok(new SessionRoomChoices(session.PlaceId, placeName, session.PlaceRoomId, session.RoomName, offered));
    }

    /// <summary>The place's active rooms that are public or belong to one of this person's groups, in the groups' own order.</summary>
    private async Task<List<SessionRoomChoice>> OfferedAsync(BenDataContext db, Guid placeId, Guid userId, CancellationToken ct)
    {
        var mine = (await _security.GetMembershipOrganizationsAsync(userId, ct)).Select(o => o.Id).ToList();
        return await db.PlaceRooms.AsNoTracking()
            .Where(r => r.PlaceId == placeId && r.IsActive && (r.IsPublic || mine.Contains(r.OrganizationId)))
            .OrderBy(r => r.Organization.Name).ThenBy(r => r.SortOrder).ThenBy(r => r.Name)
            .Select(r => new SessionRoomChoice(r.Id, r.Name, r.Floor, r.Organization.Name))
            .ToListAsync(ct);
    }
}

/// <param name="PlaceRoomId">One of the rooms offered.</param>
/// <param name="Name">A room's name, typed, when the place has no room for it. Both empty clears the room.</param>
public sealed record SetSessionRoomRequest(Guid? PlaceRoomId, string? Name);

/// <summary>The session's room, and the rooms it may be put in.</summary>
/// <param name="PlaceId">The place the session was recorded at. With none, only a typed name can be given.</param>
public sealed record SessionRoomChoices(
    Guid? PlaceId, string? PlaceName, Guid? PlaceRoomId, string? RoomName, IReadOnlyList<SessionRoomChoice> Rooms);

/// <summary>One room a session may be put in, with the group that named it.</summary>
public sealed record SessionRoomChoice(Guid Id, string Name, string? Floor, string GroupName);
