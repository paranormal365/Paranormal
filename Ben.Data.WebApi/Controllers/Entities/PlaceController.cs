using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// A place, and what the caller is allowed to know about what has happened there.
/// </summary>
/// <remarks>
/// <para>The point of the whole Place idea: several organizations visit the same building over
/// years, and comparing notes is useful. What each caller sees is decided entirely by
/// <see cref="InvestigationVisibilityFilter"/> — one predicate, so the sharing rules cannot drift
/// between this endpoint and any later one.</para>
///
/// <para>Signed-in only for now. The anonymous view of genuinely public investigations belongs with
/// the rest of the public surface (P7) and is not smuggled in here.</para>
/// </remarks>
[ApiController]
[Route("api/places")]
[Authorize]
public sealed class PlaceController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PlaceController(IDbContextFactory<BenDataContext> db) => _db = db;

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlaceRecord>> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var place = await db.Places.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PlaceRecord(
                p.Id, p.Name, p.StreetAddress1, p.City, p.State, p.ZipCode, p.Country,
                p.Latitude, p.Longitude, p.GeocodeNote, p.Kind))
            .FirstOrDefaultAsync(ct);

        return place is null ? NotFound() : Ok(place);
    }

    /// <summary>
    /// Investigations at this place that the caller may see.
    /// </summary>
    /// <remarks>
    /// Their own group's work always; anything marked public; and anything shared with people who
    /// have investigated this place, provided one of their groups has. That last rule is
    /// deliberately not reciprocal — see <see cref="InvestigationVisibility.PlaceInvestigators"/>.
    /// </remarks>
    [HttpGet("{id:guid}/investigations")]
    public async Task<ActionResult<IEnumerable<PlaceInvestigationRow>>> GetInvestigations(
        Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await db.Places.AsNoTracking().AnyAsync(p => p.Id == id, ct)) return NotFound();

        var myOrgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        // Precomputed rather than worked out per row — the difference between one query and one
        // per investigation.
        var myPlaces = await InvestigationVisibilityFilter.PlacesInvestigatedByAsync(db, myOrgIds, ct);

        var rows = await db.Investigations.AsNoTracking()
            .Where(i => i.PlaceId == id)
            .Where(InvestigationVisibilityFilter.VisibleTo(myOrgIds, myPlaces))
            .OrderByDescending(i => i.ScheduledDateTime)
            .Select(i => new PlaceInvestigationRow(
                i.Id,
                i.Title,
                i.ScheduledDateTime,
                i.Status,
                i.Visibility,
                i.OrganizationId,
                i.Organization.Name,
                myOrgIds.Contains(i.OrganizationId)))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>
    /// Places that are probably the one being described — "did you mean this?" before a second row
    /// exists.
    /// </summary>
    /// <remarks>
    /// <para>Reads only. It never creates, merges or alters anything; the caller is offered
    /// candidates and decides. Offering a match is undone by ignoring it, whereas applying one is
    /// not, and only the person entering the place knows whether it is really the same building.</para>
    ///
    /// <para>The rule lives in <see cref="PlaceMatcher"/> — the same address <i>and</i> within a
    /// tenth of a mile. See the design note in <c>ProjectNotes/specs</c> for why the conjunction.</para>
    ///
    /// <para>Candidates are filtered in memory after a coarse city/state narrowing rather than in
    /// SQL, because the normalisation and the distance maths are not things EF can translate. The
    /// narrowing keeps that from meaning "load every place".</para>
    /// </remarks>
    [HttpGet("candidates")]
    public async Task<ActionResult<IEnumerable<PlaceCandidate>>> FindCandidates(
        [FromQuery] string? street,
        [FromQuery] string? city,
        [FromQuery] string? state,
        [FromQuery] string? zip,
        [FromQuery] string? name,
        [FromQuery] decimal? latitude,
        [FromQuery] decimal? longitude,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(street) && string.IsNullOrWhiteSpace(name))
            return Ok(Array.Empty<PlaceCandidate>());

        await using var db = await _db.CreateDbContextAsync(ct);

        var query = db.Places.AsNoTracking();

        // Coarse narrowing only — the real decision happens below. Skipped when no city is given
        // so that a landmark with coordinates and a name still finds its twin.
        if (!string.IsNullOrWhiteSpace(city))
            query = query.Where(p => p.City == null || p.City == city);

        var nearby = await query.ToListAsync(ct);

        var matches = nearby
            .Where(p => PlaceMatcher.IsProbableMatch(p, street, city, state, zip, name, latitude, longitude))
            .Select(p => new PlaceCandidate(
                p.Id,
                p.Name,
                p.StreetAddress1,
                p.City,
                p.State,
                p.Kind,
                DistanceMiles(p, latitude, longitude),
                // Counted so the caller can tell an established place from a stray row, which is
                // usually the difference between "yes, that one" and "no, mine is new".
                db.Investigations.Count(i => i.PlaceId == p.Id)))
            .OrderBy(c => c.DistanceMiles ?? double.MaxValue)
            .ToList();

        return Ok(matches);
    }

    private static double? DistanceMiles(Place place, decimal? latitude, decimal? longitude)
        => place.Latitude is null || place.Longitude is null || latitude is null || longitude is null
            ? null
            : PlaceMatcher.DistanceMiles(
                (double)place.Latitude.Value, (double)place.Longitude.Value,
                (double)latitude.Value, (double)longitude.Value);

    /// <summary>
    /// "N investigations by M groups since Y", counted over what this caller may actually see.
    /// </summary>
    /// <remarks>
    /// Computed from the same filtered set as the list rather than from the raw table. A summary
    /// that counted everything would tell a visitor how much they are not being shown, which is
    /// its own small leak.
    /// </remarks>
    [HttpGet("{id:guid}/summary")]
    public async Task<ActionResult<PlaceSummary>> GetSummary(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await db.Places.AsNoTracking().AnyAsync(p => p.Id == id, ct)) return NotFound();

        var myOrgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);
        var myPlaces = await InvestigationVisibilityFilter.PlacesInvestigatedByAsync(db, myOrgIds, ct);

        var visible = await db.Investigations.AsNoTracking()
            .Where(i => i.PlaceId == id)
            .Where(InvestigationVisibilityFilter.VisibleTo(myOrgIds, myPlaces))
            .Select(i => new { i.OrganizationId, i.ScheduledDateTime })
            .ToListAsync(ct);

        // Same record as the public endpoint returns, so both pages phrase the history
        // identically instead of two near-identical shapes drifting apart.
        return Ok(new PlaceSummary(
            visible.Count,
            visible.Select(v => v.OrganizationId).Distinct().Count(),
            visible.Count == 0 ? null : visible.Min(v => v.ScheduledDateTime).Year));
    }
}

/// <summary>
/// A place that might be the one somebody is about to create.
/// </summary>
/// <remarks>
/// <c>DistanceMiles</c> is null when either side has no coordinates — unknown, not zero.
/// <c>InvestigationCount</c> is there so a caller can tell an established place from a stray row.
/// </remarks>
public sealed record PlaceCandidate(
    Guid Id,
    string? Name,
    string? StreetAddress1,
    string? City,
    string? State,
    PlaceKind Kind,
    double? DistanceMiles,
    int InvestigationCount);

/// <summary>A place as the place page shows it.</summary>
public sealed record PlaceRecord(
    Guid Id,
    string? Name,
    string? StreetAddress1,
    string? City,
    string? State,
    string? ZipCode,
    string? Country,
    decimal? Latitude,
    decimal? Longitude,
    string? GeocodeNote,
    PlaceKind Kind);

/// <summary>
/// One investigation at a place, as seen by somebody who may or may not be in the group that ran it.
/// </summary>
/// <remarks>
/// <c>IsMine</c> says whether the viewer's own organization ran it, so the page can separate "our
/// visits" from "what others have shared" without the client guessing from ids.
/// </remarks>
public sealed record PlaceInvestigationRow(
    Guid Id,
    string Title,
    DateTime ScheduledDateTime,
    InvestigationStatus Status,
    InvestigationVisibility Visibility,
    Guid OrganizationId,
    string OrganizationName,
    bool IsMine);
