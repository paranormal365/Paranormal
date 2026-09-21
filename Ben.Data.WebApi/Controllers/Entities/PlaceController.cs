using Ben.Data.Common.Enums;
using Ben.Data.WebApi.Controllers.Public;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ben.Data.WebApi.Services.Access;
using Ben.Data.WebApi.Services.Places;
using Ben.Service.Models.Entities;

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

    /// <summary>
    /// Creates a public location — a landmark, a business, a cemetery (item 250).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-21: <i>"I would like to be able to create public locations like Cragfont
    /// in Castillian Springs, TN."</i> Until this, there was <b>no way to create a place at all</b>.
    /// They appeared only sideways — a case bound one, an investigation bound one, publishing a
    /// session made one — so a landmark nobody had yet investigated could not be named, and the
    /// page that gathers what has been found there could not be brought into existence on purpose.
    /// </para>
    ///
    /// <para><b>Public locations only, and the caller cannot choose otherwise.</b> A private
    /// residence is somebody's home; the routes that make one all run through a client
    /// relationship — a case somebody asked for, a visit somebody booked — and this door has none.
    /// Letting anybody type a home address and publish a page about it is the one thing this must
    /// never be, so the kind is set here rather than accepted.</para>
    ///
    /// <para><b>An existing place is returned rather than a second one made.</b> Ben's dedup rule
    /// is the address (item 88), and two rows for one building split its evidence in half and make
    /// the merge screen somebody's afternoon. A caller who names an address that already exists
    /// gets that place back and the answer says so, which is a better outcome than a refusal: they
    /// wanted a page for Cragfont, and Cragfont is what they get.</para>
    /// </remarks>
    [HttpPost("public-location")]
    public async Task<ActionResult<PlaceCreated>> CreatePublicLocation(
        [FromBody] NewPublicPlaceRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var name = PlaceFactory.Trimmed(request?.Name);
        if (name is null || name.Length < 2)
            return BadRequest("Give the place a name — what people call it.");

        if (PlaceFactory.Trimmed(request!.City) is null || PlaceFactory.Trimmed(request.State) is null)
            return BadRequest("A town and a state, so people can find it and it lands on the map.");

        await using var db = await _db.CreateDbContextAsync(ct);

        // The address is the identity, per item 88's own rule. Matched on the parts somebody
        // types rather than on the geocode, because two people describing one building agree on
        // its street long before they agree on its coordinates.
        var street = PlaceFactory.Trimmed(request.StreetAddress1);
        var city = PlaceFactory.Trimmed(request.City)!;
        var state = PlaceFactory.Trimmed(request.State)!;

        var existing = await db.Places.AsNoTracking()
            .Where(p => p.City == city && p.State == state
                     && (street != null ? p.StreetAddress1 == street : p.Name == name))
            .Select(p => new { p.Id, p.Kind })
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            // A match that is somebody's home is NOT handed back as a public location. Saying so
            // plainly beats silently returning a row whose page would then refuse everything.
            if (existing.Kind != PlaceKind.PublicLocation)
            {
                return BadRequest(
                    "There is already a place at that address, and it is recorded as somebody's "
                  + "home. If that is wrong, ask a site administrator to correct it.");
            }

            return Ok(new PlaceCreated(existing.Id, AlreadyExisted: true,
                "That place is already here — this is its page."));
        }

        var place = await PlaceFactory.CreateAsync(
            new NewPlaceRequest(
                name, street, PlaceFactory.Trimmed(request.StreetAddress2), city, state,
                PlaceFactory.Trimmed(request.ZipCode), PlaceFactory.Trimmed(request.Country),
                request.Latitude, request.Longitude,
                // Never the caller's to choose. See the remarks.
                PlaceKind.PublicLocation),
            userId, ct);

        db.Places.Add(place);
        await db.SaveChangesAsync(ct);

        return Ok(new PlaceCreated(place.Id, AlreadyExisted: false, "Added."));
    }

    /// <summary>
    /// Writes what this place is, for somebody who has never been (item 250).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-21: <i>"we can create a page with information about it."</i> A list of
    /// evidence with no account of what the building IS tells a reader nothing they can weigh it
    /// against.</para>
    ///
    /// <para><b>Anybody signed in, at a public location only</b> — the same door as adding
    /// evidence, and for the same reason: the person who knows what Cragfont is is rarely a member
    /// of a paranormal group. A private residence has no public page to describe and is refused in
    /// words.</para>
    ///
    /// <para><b>Plain text, and the server enforces it.</b> Whatever arrives is stripped of
    /// markup: this is written by whoever gets there first, edited by anybody after them, and
    /// rendered on a page any stranger reads. A field like that must not be able to carry a link,
    /// a script or a layout, and refusing is worse than cleaning — somebody describing a house
    /// should not have to know what an angle bracket does.</para>
    /// </remarks>
    [HttpPut("{id:guid}/description")]
    public async Task<ActionResult<PlaceRecord>> SetDescription(
        Guid id, [FromBody] SetPlaceDescriptionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var place = await db.Places.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (place is null) return NotFound();

        if (place.Kind != PlaceKind.PublicLocation)
        {
            return BadRequest(
                "Only a public location has a page to describe. This place is somebody's home.");
        }

        // Script and style ELEMENTS go whole, content and all, before the tags are stripped.
        // PlainTextHtml.ToText removes tags and keeps what was between them, which is right for
        // prose — <b>bold</b> should leave "bold" behind — and wrong for these two, where the
        // content is never something a person meant to write. Pasting a paragraph off a web page
        // otherwise drops the page's scripts into the description as words. Not a security hole
        // (this is rendered as text, never as markup) but nobody typed "bad()".
        //
        // Done here rather than in the shared helper, which a dozen other callers depend on
        // behaving exactly as it does.
        var raw = System.Text.RegularExpressions.Regex.Replace(
            request?.Description ?? string.Empty,
            "<(script|style)[^>]*>.*?</\\1>",
            " ",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
          | System.Text.RegularExpressions.RegexOptions.Singleline);

        var text = Ben.Data.Common.Text.PlainTextHtml.ToText(raw).Trim();
        if (text.Length > 4000) text = text[..4000];

        place.Description = text.Length == 0 ? null : text;
        place.DateUpdated = DateTime.UtcNow;
        place.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        return Ok(PlaceDisclosure.Public(
            place.Id, place.Name, place.StreetAddress1, place.City, place.State, place.ZipCode,
            place.Country, place.Latitude, place.Longitude, place.GeocodeNote, place.Kind,
            place.Description));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PlaceRecord>> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var stored = await db.Places.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new
            {
                p.Id, p.Name, p.StreetAddress1, p.City, p.State, p.ZipCode, p.Country,
                p.Latitude, p.Longitude, p.GeocodeNote, p.Kind,
            })
            .FirstOrDefaultAsync(ct);

        if (stored is null) return NotFound();

        // Being signed in is not a reason to know where somebody lives — accounts are free and
        // self-service, so this endpoint asks for standing and otherwise serves the same shape the
        // anonymous one would (2026-09-17 audit).
        var inFull = stored.Kind != PlaceKind.PrivateResidence
                  || await PlaceDisclosure.MaySeeInFullAsync(
                         db, id, GetCurrentUserId(), await CallerIsSuperAdminAsync(), ct);

        return Ok(inFull
            ? new PlaceRecord(
                stored.Id, stored.Name, stored.StreetAddress1, stored.City, stored.State,
                stored.ZipCode, stored.Country, stored.Latitude, stored.Longitude,
                stored.GeocodeNote, stored.Kind)
            : PlaceDisclosure.Public(
                stored.Id, stored.Name, stored.StreetAddress1, stored.City, stored.State,
                stored.ZipCode, stored.Country, stored.Latitude, stored.Longitude,
                stored.GeocodeNote, stored.Kind));
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
    PlaceKind Kind,
    /// <summary>
    /// What this place is, for somebody who has never been (item 250). Null on a private
    /// residence — somebody's home has no public page to describe.
    /// </summary>
    string? Description = null);

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
