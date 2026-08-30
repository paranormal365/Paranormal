using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// The rooms a group has named inside a place it runs (item 197).
/// </summary>
/// <remarks>
/// <para>Scoped to the organization AND the place, in the route, because rooms belong to the
/// group that named them rather than to the place: a <c>Place</c> is shared, and two groups
/// describing the same building must not be able to edit each other's rooms. The org id in the
/// route is what every write is checked against, so a room can only ever be reached through the
/// group that owns it.</para>
///
/// <para>Gated on <c>OrganizationSettings</c>, which is the group describing ITSELF — the same
/// permission that governs its profile and addresses. Naming the rooms of your own hotel is that
/// kind of act, not case work.</para>
/// </remarks>
[ApiController]
[Route("api/organizations/{orgId:guid}/places/{placeId:guid}/rooms")]
[Authorize]
public sealed class PlaceRoomController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    public PlaceRoomController(
        IDbContextFactory<BenDataContext> db,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    { _db = db; _security = security; }

    /// <summary>Owner, administrator or SuperAdmin — the same rule the rest of the app uses.</summary>
    private async Task<bool> IsOrgAdminAsync(Guid orgId, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.SuperAdmin)) return true;
        await using var db = await _db.CreateDbContextAsync(ct);
        return await FileAudienceAccess.IsOrgAdminAsync(db, orgId, GetCurrentUserId(), ct);
    }

    private async Task<bool> MayEditAsync(Guid orgId, CancellationToken ct)
        => await IsOrgAdminAsync(orgId, ct)
        || await _security.HasAccessAsync(GetCurrentUserId(), orgId,
               OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Update, ct);

    private async Task<bool> MayReadAsync(Guid orgId, CancellationToken ct)
        => await IsOrgAdminAsync(orgId, ct)
        || await _security.HasAccessAsync(GetCurrentUserId(), orgId,
               OrganizationSecurityTable.OrganizationSettings, OrganizationSecurityAction.Read, ct);

    /// <summary>Every room this group has named in this place, in the order it arranged them.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlaceRoomRecord>>> GetAll(
        Guid orgId, Guid placeId, CancellationToken ct)
    {
        if (!await MayReadAsync(orgId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var rooms = await db.PlaceRooms.AsNoTracking()
            .Where(r => r.OrganizationId == orgId && r.PlaceId == placeId)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.Name)
            .Select(r => new PlaceRoomRecord(
                r.Id, r.PlaceId, r.Name, r.Floor, r.Description, r.IsPublic, r.SortOrder, r.IsActive))
            .ToListAsync(ct);

        return Ok(rooms);
    }

    /// <summary>Names a room.</summary>
    [HttpPost]
    public async Task<ActionResult<PlaceRoomRecord>> Create(
        Guid orgId, Guid placeId, [FromBody] SavePlaceRoomRequest request, CancellationToken ct)
    {
        if (!await MayEditAsync(orgId, ct)) return Forbid();

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("A room needs a name.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Places.AnyAsync(p => p.Id == placeId, ct)) return NotFound("No such place.");

        // Checked here so the answer is a sentence rather than a unique-index violation, and
        // checked again by the index because two requests can race past this one.
        if (await db.PlaceRooms.AnyAsync(r =>
                r.OrganizationId == orgId && r.PlaceId == placeId && r.Name == name, ct))
            return Conflict($"This place already has a room called \"{name}\".");

        var userId = GetCurrentUserId();
        var room = new PlaceRoom
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            PlaceId = placeId,
            Name = name,
            Floor = string.IsNullOrWhiteSpace(request.Floor) ? null : request.Floor.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            IsPublic = request.IsPublic,
            // Appended rather than inserted: a new room goes at the end of the list its owner has
            // arranged, and they can move it.
            SortOrder = await db.PlaceRooms
                .Where(r => r.OrganizationId == orgId && r.PlaceId == placeId)
                .Select(r => (int?)r.SortOrder).MaxAsync(ct) is { } max ? max + 1 : 0,
            IsActive = true,
            DateCreated = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };

        db.PlaceRooms.Add(room);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            // The index caught a race the check above could not.
            return Conflict($"This place already has a room called \"{name}\".");
        }

        return Ok(ToRecord(room));
    }

    /// <summary>Edits a room.</summary>
    [HttpPut("{roomId:guid}")]
    public async Task<ActionResult<PlaceRoomRecord>> Update(
        Guid orgId, Guid placeId, Guid roomId, [FromBody] SavePlaceRoomRequest request, CancellationToken ct)
    {
        if (!await MayEditAsync(orgId, ct)) return Forbid();

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest("A room needs a name.");

        await using var db = await _db.CreateDbContextAsync(ct);

        // Matched on all three: a room id alone would let one group edit another's room by
        // guessing it, since the id is the only thing the caller supplies that is not checked.
        var room = await db.PlaceRooms.FirstOrDefaultAsync(
            r => r.Id == roomId && r.OrganizationId == orgId && r.PlaceId == placeId, ct);
        if (room is null) return NotFound();

        if (await db.PlaceRooms.AnyAsync(r =>
                r.Id != roomId && r.OrganizationId == orgId && r.PlaceId == placeId && r.Name == name, ct))
            return Conflict($"This place already has a room called \"{name}\".");

        room.Name = name;
        room.Floor = string.IsNullOrWhiteSpace(request.Floor) ? null : request.Floor.Trim();
        room.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        room.IsPublic = request.IsPublic;
        if (request.SortOrder is { } sort) room.SortOrder = sort;
        if (request.IsActive is { } active) room.IsActive = active;
        room.DateUpdated = DateTime.UtcNow;
        room.UpdatedByAppUserId = GetCurrentUserId();

        await db.SaveChangesAsync(ct);
        return Ok(ToRecord(room));
    }

    /// <summary>
    /// Retires a room, or deletes it outright when nothing has been attributed to it yet.
    /// </summary>
    /// <remarks>
    /// A room that has been used is deactivated rather than removed, so anything recorded in it
    /// still reads afterwards — the same rule equipment and duties follow. Nothing points at rooms
    /// yet, so today this always deletes; the branch is written now so that when field sessions do
    /// attribute to a room, retiring one cannot orphan a night's work.
    /// </remarks>
    [HttpDelete("{roomId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid placeId, Guid roomId, CancellationToken ct)
    {
        if (!await MayEditAsync(orgId, ct)) return Forbid();

        await using var db = await _db.CreateDbContextAsync(ct);
        var room = await db.PlaceRooms.FirstOrDefaultAsync(
            r => r.Id == roomId && r.OrganizationId == orgId && r.PlaceId == placeId, ct);
        if (room is null) return NotFound();

        db.PlaceRooms.Remove(room);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static PlaceRoomRecord ToRecord(PlaceRoom r) => new(
        r.Id, r.PlaceId, r.Name, r.Floor, r.Description, r.IsPublic, r.SortOrder, r.IsActive);
}

/// <summary>One named space inside a place.</summary>
public sealed record PlaceRoomRecord(
    Guid Id,
    Guid PlaceId,
    string Name,
    string? Floor,
    string? Description,
    bool IsPublic,
    int SortOrder,
    bool IsActive);

/// <summary>Naming or editing a room. Sort order and active state are optional on an edit.</summary>
public sealed record SavePlaceRoomRequest(
    string? Name,
    string? Floor,
    string? Description,
    bool IsPublic,
    int? SortOrder = null,
    bool? IsActive = null);
