using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Places;

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
    /// The posts about this place, and whether this caller may add one.
    /// </summary>
    /// <remarks>
    /// <para><b>Why this exists beside the anonymous copy.</b> The place page reads its published
    /// content through <c>PublicPlaceController</c>, and the website calls that one
    /// <i>anonymously</i> on purpose — published is published, and sending a token would invite a
    /// second idea of who may see what. But "may YOU post here" is a question about the caller, and
    /// an anonymous call can only ever answer no. So a signed-in reader asks here instead, exactly
    /// as they already do for investigations: two endpoints, one rule each side of them.</para>
    ///
    /// <para>The posts themselves come from <see cref="FeedController.LatestForPlaceAsync"/>, the
    /// same method the anonymous page uses, so hidden posts and the block list behave identically.
    /// The only difference is the reader it is asked on behalf of.</para>
    /// </remarks>
    [HttpGet("{id:guid}/posts")]
    public async Task<ActionResult<PlacePostsRecord>> GetPosts(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        var kind = await db.Places.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => (PlaceKind?)p.Kind)
            .FirstOrDefaultAsync(ct);
        if (kind is null) return NotFound();

        // Place posts ARE feed posts and follow the feed's own switch. Asked here rather than left
        // to the page: without it the box is offered, clicked, and the post refused by an endpoint
        // that 404s when the feed is off — a control that looks live and is not, which is the
        // failure the server-guard-needs-a-UI-path rule exists to prevent. Found by driving it.
        var feedOn = await Services.SiteSettingsService.GetBoolAsync(
            db, Services.SiteSettingKeys.FeaturePublicFeed, whenUnset: false, ct);
        if (!feedOn) return Ok(new PlacePostsRecord([], CanPost: false));

        var posts = await FeedController.LatestForPlaceAsync(db, id, userId, PlacePostsShown, ct);

        // Signing in is enough for a public location, which is wider than the feed's front page —
        // see FeedParticipation.PlaceRefusal for why. A residence takes no posts at all.
        return Ok(new PlacePostsRecord(
            posts,
            CanPost: Services.FeedParticipation.PlaceRefusal(userId) is null
                  && kind == PlaceKind.PublicLocation));
    }

    /// <summary>How many of a place's posts one read returns. Matches the anonymous page's.</summary>
    private const int PlacePostsShown = 20;

    /// <summary>
    /// The caller's own groups' cases at this place, whatever their status.
    /// </summary>
    /// <remarks>
    /// <para>The signed-in half of the place page's case list. The public one shows only cases
    /// somebody published; this one answers the question a member actually arrives with — <i>do we
    /// already have a case here?</i> — which the public list cannot, because the answer is usually a
    /// case that is not published and never will be.</para>
    ///
    /// <para><b>Scoped to the caller's own memberships and nothing else.</b> No visibility ladder to
    /// apply and none invented: another group's unpublished case at the same building is their
    /// business, and the fact that they have one is itself something they have not shared.</para>
    ///
    /// <para>Deferred out of the case-names-a-place work on 2026-09-17 and finished here.</para>
    /// </remarks>
    [HttpGet("{id:guid}/my-cases")]
    public async Task<ActionResult<IEnumerable<PlaceCaseRow>>> GetMyCases(Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);

        if (!await db.Places.AsNoTracking().AnyAsync(p => p.Id == id, ct)) return NotFound();

        var myOrgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        if (myOrgIds.Count == 0) return Ok(Array.Empty<PlaceCaseRow>());

        var rows = await db.Cases.AsNoTracking()
            .Where(c => c.PlaceId == id && myOrgIds.Contains(c.OrganizationId))
            .OrderByDescending(c => c.DateCaseOpened)
            .Select(c => new PlaceCaseRow(
                c.Id,
                c.OrganizationId,
                c.Organization.Name,
                $"#{c.CaseYear}-{c.OrgCaseNumber:D3}",
                c.Title,
                c.Status,
                c.DateCaseOpened,
                c.IsPublic && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted)))
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
/// <summary>
/// A place's posts as one signed-in reader sees them, and whether they may add one (2026-09-17).
/// </summary>
public sealed record PlacePostsRecord(
    IReadOnlyList<Ben.Service.Models.Feed.FeedPostRecord> Posts,
    bool CanPost);

/// <summary>
/// One of the caller's own groups' cases at a place (2026-09-17).
/// </summary>
/// <param name="IsPublished">
/// Whether a visitor would see it too — the flag AND a published status, the same pair every other
/// answer on the site means by "public". Shown so a member can tell at a glance which of their
/// cases is already on the place's public list.
/// </param>
public sealed record PlaceCaseRow(
    Guid Id,
    Guid OrganizationId,
    string OrganizationName,
    string CaseReference,
    string Title,
    CaseStatus Status,
    DateTime DateCaseOpened,
    bool IsPublished);

public sealed record PlaceInvestigationRow(
    Guid Id,
    string Title,
    DateTime ScheduledDateTime,
    InvestigationStatus Status,
    InvestigationVisibility Visibility,
    Guid OrganizationId,
    string OrganizationName,
    bool IsMine);
