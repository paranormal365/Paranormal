using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Services.Places;
using Ben.Data.Source.Context;
using Ben.Data.Source.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// The catalogue of places: what exists, what kind it is, and getting rid of one.
/// </summary>
/// <remarks>
/// <para><b>Why this exists (item C8).</b> Anybody signed in can put a public location on the map,
/// and until now nothing could list them, change one's mind about it, or take one away. The only
/// removal path was the duplicate-merge screen, which needs a survivor to merge into — so a place
/// entered once, wrongly, could not be removed at all. A door that only opens one way is how the
/// site ends up with somebody's home standing as a public landmark.</para>
///
/// <para><b>Demote, not just delete.</b> The mistake worth fixing most urgently is a private home
/// entered as a public location: its page is then a public evidence page for an address somebody
/// lives at. Deleting it would destroy whatever has legitimately accumulated there; making it a
/// private residence takes it off the public map and leaves the record intact for the group whose
/// client it belongs to. That is the repair, and deletion is for the emptier case of a place that
/// should never have been typed at all.</para>
///
/// <para><b>Deletion only while nothing holds it.</b> Twelve tables carry a PlaceId and the
/// database would refuse most of them anyway, but a refusal arriving from SQL after the click is
/// not an answer — see <see cref="PlaceUsageCensus"/> for why the count behind the warning and the
/// count behind the rule are the same function. Anything with history attached is merged or
/// demoted, never deleted.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/places")]
public sealed class AdminPlaceCatalogController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<AdminPlaceCatalogController> _log;

    public AdminPlaceCatalogController(
        IDbContextFactory<BenDataContext> db, IAuditLogService auditLog,
        ILogger<AdminPlaceCatalogController> log)
    { _db = db; _auditLog = auditLog; _log = log; }

    /// <summary>Every place, newest first, narrowed by name, town or kind.</summary>
    /// <remarks>
    /// Paged because this table grows with every case and every visit, not with the number of
    /// public locations somebody typed. The counts are what make a row actionable — a place with
    /// nothing against it is one that can be deleted, and the screen should not have to ask a
    /// second time to find that out.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<AdminPlacePage>> List(
        [FromQuery] string? query,
        [FromQuery] PlaceKind? kind,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var places = db.Places.AsNoTracking();

        if (kind is { } k) places = places.Where(p => p.Kind == k);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            places = places.Where(p =>
                (p.Name != null && EF.Functions.Like(p.Name, $"%{q}%"))
                || (p.City != null && EF.Functions.Like(p.City, $"%{q}%"))
                || (p.State != null && EF.Functions.Like(p.State, $"%{q}%"))
                || (p.StreetAddress1 != null && EF.Functions.Like(p.StreetAddress1, $"%{q}%")));
        }

        var total = await places.CountAsync(ct);

        // The three counts on the row are the ones that decide what a person can do with it:
        // whether it is in use at all, and whether anybody has contributed to it in public.
        var rows = await places
            .OrderByDescending(p => p.DateCreated)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new AdminPlaceRow(
                p.Id,
                p.Name,
                p.City,
                p.State,
                p.Kind,
                p.Latitude,
                p.Longitude,
                p.DateCreated,
                p.CreatedByAppUser.DisplayName,
                db.Cases.Count(x => x.PlaceId == p.Id),
                db.Investigations.Count(x => x.PlaceId == p.Id),
                db.PlaceEvidence.Count(x => x.PlaceId == p.Id)))
            .ToListAsync(ct);

        return Ok(new AdminPlacePage(rows, total, page, pageSize));
    }

    /// <summary>Everything holding this place, and whether it could be deleted.</summary>
    /// <remarks>
    /// Asked before the button is pressed, so the screen can say "3 cases and 6 pieces of evidence
    /// point at this place" rather than offering a delete that will be refused.
    /// </remarks>
    [HttpGet("{id:guid}/usage")]
    public async Task<ActionResult<AdminPlaceUsage>> Usage(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await db.Places.AnyAsync(p => p.Id == id, ct)) return NotFound("That place doesn't exist.");

        var usage = await PlaceUsageCensus.ForAsync(db, id, ct);
        return Ok(new AdminPlaceUsage(usage.Total, usage.IsEmpty, usage.WhatHoldsIt()));
    }

    /// <summary>Makes a place public, or takes it off the public map.</summary>
    /// <remarks>
    /// <para>Demoting is the repair for a home entered as a landmark, and it is deliberately not
    /// destructive: everything stays, and the public page stops existing. Promoting is here for the
    /// opposite mistake and because a caretaker sometimes wants a genuine landmark that was first
    /// typed as a residence.</para>
    ///
    /// <para><b>Promotion is refused for anything with a street number.</b> A public location is a
    /// cemetery, a bridge, a ruin — the archive's rule since item 184 is that a private residence
    /// never becomes a public evidence page, and an address that looks residential is the one thing
    /// that must not be promoted by a tired hand on a list of fifty rows.</para>
    /// </remarks>
    [HttpPost("{id:guid}/kind")]
    public async Task<ActionResult<AdminPlaceRow>> SetKind(
        Guid id, [FromBody] SetPlaceKindRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var place = await db.Places.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (place is null) return NotFound("That place doesn't exist.");
        if (place.Kind == request.Kind) return BadRequest("That place is already that kind.");

        if (request.Kind == PlaceKind.PublicLocation && LooksResidential(place))
            return BadRequest(
                "This has a street address on it, so it cannot be made a public location. If it is "
              + "genuinely a landmark, clear the street address first.");

        var before = new { place.Kind };
        place.Kind = request.Kind;
        place.DateUpdated = DateTime.UtcNow;
        place.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        await _auditLog.LogUpdateAsync(nameof(Ben.Data.Source.Entities.Place), id,
            before, new { place.Kind }, userId, AppSources.WebApi);

        _log.LogInformation("Place {PlaceId} set to {Kind} by {UserId}.", id, request.Kind, userId);

        return Ok(new AdminPlaceRow(
            place.Id, place.Name, place.City, place.State, place.Kind,
            place.Latitude, place.Longitude, place.DateCreated, null,
            await db.Cases.CountAsync(x => x.PlaceId == id, ct),
            await db.Investigations.CountAsync(x => x.PlaceId == id, ct),
            await db.PlaceEvidence.CountAsync(x => x.PlaceId == id, ct)));
    }

    /// <summary>Removes a place that nothing points at.</summary>
    /// <remarks>
    /// The refusal names what is in the way, because "this place is in use" leaves somebody with
    /// nowhere to go; "3 cases and a room" tells them what to move.
    /// </remarks>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<bool>> Delete(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserIdOrThrow();
        await using var db = await _db.CreateDbContextAsync(ct);

        var place = await db.Places.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (place is null) return NotFound("That place doesn't exist.");

        var usage = await PlaceUsageCensus.ForAsync(db, id, ct);
        if (!usage.IsEmpty)
            return BadRequest(
                $"This place can't be deleted: {usage.WhatHoldsIt()} still point at it. "
              + "Merge it into the record you are keeping, or make it a private residence.");

        db.Places.Remove(place);
        await db.SaveChangesAsync(ct);

        await _auditLog.LogDeleteAsync(nameof(Ben.Data.Source.Entities.Place), id, place,
            userId, AppSources.WebApi);

        _log.LogInformation("Place {PlaceId} deleted by {UserId}.", id, userId);
        return Ok(true);
    }

    /// <summary>
    /// A street number is the tell that somebody lives there.
    /// </summary>
    /// <remarks>
    /// Deliberately blunt. It refuses some genuine landmarks that happen to carry a street address,
    /// and the cost of that is one sentence asking for the address to be cleared first; the cost of
    /// the opposite mistake is a public evidence page for somebody's home.
    /// </remarks>
    private static bool LooksResidential(Ben.Data.Source.Entities.Place place)
        => !string.IsNullOrWhiteSpace(place.StreetAddress1)
        && place.StreetAddress1.Any(char.IsDigit);
}

/// <summary>One page of the place catalogue.</summary>
public sealed record AdminPlacePage(
    IReadOnlyList<AdminPlaceRow> Places, int Total, int Page, int PageSize);

/// <summary>A place as the catalogue lists it.</summary>
public sealed record AdminPlaceRow(
    Guid Id, string? Name, string? City, string? State, PlaceKind Kind,
    decimal? Latitude, decimal? Longitude, DateTime DateCreated, string? AddedBy,
    int Cases, int Investigations, int Evidence);

/// <summary>What is holding a place.</summary>
/// <param name="Total">How many rows across every table point at it.</param>
/// <param name="CanDelete">True when nothing does.</param>
/// <param name="WhatHoldsIt">Those rows in words, or null when there are none.</param>
public sealed record AdminPlaceUsage(int Total, bool CanDelete, string? WhatHoldsIt);

/// <summary>Which kind a place should be.</summary>
public sealed record SetPlaceKindRequest(PlaceKind Kind);
