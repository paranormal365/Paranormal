using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Redaction;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Cross-org case discovery: every public case, plus any this caller may already see.
/// </summary>
/// <remarks>
/// <para><b>Anonymous is still the default.</b> The route stays <c>[AllowAnonymous]</c> and a
/// visitor with no token gets exactly what it always returned. A signed-in caller additionally
/// gets the cases they could already open elsewhere, because a discovery map that is empty for a
/// member whose group has work on it is a poor front door.</para>
///
/// <para><b>Coordinates are approximated for everybody, including for your own cases.</b> One code
/// path, so there is no branch on which a real address could escape. A member who needs the
/// address has the case page; a map is for finding, not for navigating to somebody's door.</para>
/// </remarks>
[ApiController]
[Route("api/public/cases")]
[AllowAnonymous]
public sealed class PublicCaseDiscoveryController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    public PublicCaseDiscoveryController(
        IDbContextFactory<BenDataContext> db,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security)
    { _db = db; _security = security; }

    /// <summary>
    /// The cases this caller may see beyond the public ones, or an empty set for a visitor.
    /// </summary>
    /// <remarks>
    /// Three doors, and each is the one the owning screen already uses rather than a looser
    /// restatement: the org-side gate is <c>HasAccessAsync(Case, Read)</c> — the same call
    /// <c>CaseController</c> makes, so a member without the cases grant gains nothing here — and
    /// the client-side pair is the union <c>MyCaseController</c> builds, the originating request
    /// and any co-client access row.
    /// </remarks>
    private async Task<HashSet<Guid>> AlreadyVisibleToCallerAsync(BenDataContext db, CancellationToken ct)
    {
        var visible = new HashSet<Guid>();

        var userId = Guid.TryParse(
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var parsed)
            ? parsed : Guid.Empty;
        if (userId == Guid.Empty) return visible;

        if (User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin))
        {
            visible.UnionWith(await db.Cases.AsNoTracking().Select(c => c.Id).ToListAsync(ct));
            return visible;
        }

        // Their own memberships first, so HasAccessAsync is asked about a handful of orgs rather
        // than every organization on the site.
        var orgIds = await db.OrganizationUserMemberships.AsNoTracking()
            .Where(m => m.AppUserId == userId && m.IsActive)
            .Select(m => m.OrganizationId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var orgId in orgIds)
        {
            if (!await _security.HasAccessAsync(userId, orgId,
                    OrganizationSecurityTable.Case, OrganizationSecurityAction.Read, ct))
                continue;

            visible.UnionWith(await db.Cases.AsNoTracking()
                .Where(c => c.OrganizationId == orgId)
                .Select(c => c.Id).ToListAsync(ct));
        }

        visible.UnionWith(await db.Cases.AsNoTracking()
            .Where(c => c.ClientRequest != null && c.ClientRequest.AppUserId == userId)
            .Select(c => c.Id).ToListAsync(ct));
        visible.UnionWith(await db.CaseClientAccesses.AsNoTracking()
            .Where(a => a.AppUserId == userId)
            .Select(a => a.CaseId).ToListAsync(ct));

        return visible;
    }

    /// <summary>
    /// Returns paginated public cases across all organizations.
    /// sort: "votes" (default) | "date"
    /// Coordinates are whatever was already resolved onto the Case (e.g. at intake) —
    /// this endpoint is unauthenticated and public, so it must never geocode on the
    /// request path; a case with no resolved coordinates just omits them.
    /// </summary>
    /// <remarks>
    /// <para><b>The four bounds are optional and all-or-nothing.</b> Without them this answers the
    /// whole map, which is what a first load wants; with them it answers only what is in view,
    /// which is what every pan afterwards wants. Shaped after
    /// <c>FieldSessionUploadController.GetMyMapPoints</c> rather than inventing a second
    /// convention — corners normalised, because map libraries disagree about which one they hand
    /// over first and a reversed box reads as "nothing here" rather than as a mistake.</para>
    ///
    /// <para>A case with no resolved coordinates is <b>kept</b> when unbounded and dropped when
    /// bounded. It cannot be inside a box nobody can place it in, and silently keeping it would
    /// make the count disagree with the pins.</para>
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<PublicCaseDiscoveryPagedResponse>> GetAll(
        [FromQuery] int    page     = 1,
        [FromQuery] int    pageSize = 20,
        [FromQuery] string sort     = "votes",
        [FromQuery] double? north   = null, [FromQuery] double? south = null,
        [FromQuery] double? east    = null, [FromQuery] double? west  = null,
        CancellationToken  ct       = default)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 100);

        var bounded = north is not null || south is not null || east is not null || west is not null;
        if (bounded && (north is null || south is null || east is null || west is null))
            return BadRequest("Give all four bounds, or none.");

        await using var db = await _db.CreateDbContextAsync(ct);

        // Public, plus whatever this caller could already open. Proposed is excluded on both
        // sides: a case nobody has agreed to yet is not a place, and MyCases hides it too.
        var mine = await AlreadyVisibleToCallerAsync(db, ct);

        var query = db.Cases.AsNoTracking()
            .Include(c => c.Organization)
            .Where(c => c.Status != CaseStatus.Proposed
                     && ((c.IsPublic && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted))
                         || mine.Contains(c.Id)));

        if (bounded)
        {
            var n  = (decimal)Math.Max(north!.Value, south!.Value);
            var so = (decimal)Math.Min(north!.Value, south!.Value);
            var e  = (decimal)Math.Max(east!.Value,  west!.Value);
            var w  = (decimal)Math.Min(east!.Value,  west!.Value);
            query = query.Where(c => c.Latitude != null && c.Longitude != null
                                  && c.Latitude <= n && c.Latitude >= so
                                  && c.Longitude <= e && c.Longitude >= w);
        }

        var cases = await query.ToListAsync(ct);

        if (cases.Count == 0)
            return Ok(new PublicCaseDiscoveryPagedResponse([], 0, page, pageSize));

        var caseIds = cases.Select(c => c.Id).ToList();

        // Item 184: titles of private-engagement cases substitute real names at display time.
        var rosters = await CaseRedactionRoster.ForCasesAsync(db, caseIds, ct);

        // ── The card's tally ─────────────────────────────────────────────────
        // CaseVotes — a vote on the CASE — because that is what the card's own buttons cast and
        // what /vote-summaries reads back.
        //
        // W-H1 of the 2026-09-06 evaluation: a card printed "No votes yet" beside "✓ 3 ✗ 0 ? 0 ·
        // 3 votes". Neither number was wrong. This aggregate counted EvidenceVotes — votes on
        // individual files, reached through the timeline — while the widget below it counted
        // CaseVotes, and the card labelled both "votes". A case can easily have three people
        // saying "yes, haunted" and nobody yet arguing about a particular photo, which is exactly
        // what that card was reporting, twice, in contradictory words.
        //
        // Evidence votes are a real thing and they belong on the piece of evidence. They are not
        // the number to put beside a button that casts something else: a visitor who voted here
        // watched the tally above their click stay at zero.
        var voteCounts = await db.CaseVotes.AsNoTracking()
            .Where(v => caseIds.Contains(v.CaseId))
            .GroupBy(v => v.CaseId)
            .Select(g => new
            {
                CaseId       = g.Key,
                Total        = g.Count(),
                Confirms     = g.Count(v => v.VoteType == EvidenceVoteType.Confirms),
                Disputes     = g.Count(v => v.VoteType == EvidenceVoteType.Disputes),
                Inconclusive = g.Count(v => v.VoteType == EvidenceVoteType.Inconclusive),
            })
            .ToDictionaryAsync(x => x.CaseId, ct);

        // Build response items
        var items = cases.Select(c =>
        {
            voteCounts.TryGetValue(c.Id, out var vc);

            // The field names have promised this since the endpoint was written; until now nothing
            // performed it. Redacted here, in the projection, so the exact coordinates are never in
            // the response for a client to ignore.
            var (approxLat, approxLon) = PublicCoordinates.Approximate(c.Latitude, c.Longitude);

            return new PublicCaseDiscoveryItem(
                CaseId:            c.Id,
                CaseReference:     $"#{c.CaseYear}-{c.OrgCaseNumber:D3}",
                Title:             CaseProseRedactor.RedactFor(rosters, c.Id, c.Title)!,
                City:              c.City,
                State:             c.State,
                Country:           c.Country,
                Status:            c.Status,
                IsHaunted:         c.Status == CaseStatus.Haunted,
                DateCaseOpened:    c.DateCaseOpened,
                DateCaseClosed:    c.DateCaseClosed,
                OrgName:           c.Organization.Name,
                OrgUrlName:        c.Organization.UrlName,
                ConfirmsCount:     vc?.Confirms     ?? 0,
                DisputesCount:     vc?.Disputes     ?? 0,
                InconclusiveCount: vc?.Inconclusive ?? 0,
                Score:             EvidenceVoteScore.FromCounts(
                                       vc?.Confirms ?? 0, vc?.Disputes ?? 0, vc?.Inconclusive ?? 0),
                TotalVotes:        vc?.Total        ?? 0,
                ApproxLatitude:    approxLat,
                ApproxLongitude:   approxLon,
                ClientName:        PublicClientName.For(c));
        }).ToList();

        // Sort
        items = sort == "date"
            ? [.. items.OrderByDescending(x => x.DateCaseOpened)]
            : [.. items.OrderByDescending(x => x.TotalVotes).ThenByDescending(x => x.DateCaseOpened)];

        var total = items.Count;
        var paged = items.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Ok(new PublicCaseDiscoveryPagedResponse(paged, total, page, pageSize));
    }

    /// <summary>
    /// Returns vote summaries for a set of case IDs in one round-trip.
    /// Used by <c>PublicCaseDiscovery.razor</c> to pre-load summaries for all
    /// visible list-cards without firing one request per card.
    /// </summary>
    [HttpGet("vote-summaries")]
    public async Task<ActionResult<IReadOnlyList<CaseVoteSummary>>> GetVoteSummaries(
        [FromQuery] Guid[] caseIds, CancellationToken ct)
    {
        if (caseIds.Length == 0) return Ok(Array.Empty<CaseVoteSummary>());

        await using var db = await _db.CreateDbContextAsync(ct);

        var votes = await db.CaseVotes.AsNoTracking()
            .Where(v => caseIds.Contains(v.CaseId))
            .ToListAsync(ct);

        // Resolve the authenticated user's ID (null when anonymous)
        Guid? userId = null;
        if (HttpContext.User.Identity?.IsAuthenticated == true)
        {
            var claim = HttpContext.User.FindFirst("app_user_id")
                     ?? HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (claim is not null && Guid.TryParse(claim.Value, out var parsed))
                userId = parsed;
        }

        var result = caseIds.Select(caseId =>
        {
            var caseVotes = votes.Where(v => v.CaseId == caseId).ToList();
            var myVote    = userId.HasValue
                ? caseVotes.FirstOrDefault(v => v.VoterAppUserId == userId.Value)?.VoteType
                : null;
            return new CaseVoteSummary(
                CaseId:            caseId,
                ConfirmsCount:     caseVotes.Count(v => v.VoteType == EvidenceVoteType.Confirms),
                DisputesCount:     caseVotes.Count(v => v.VoteType == EvidenceVoteType.Disputes),
                InconclusiveCount: caseVotes.Count(v => v.VoteType == EvidenceVoteType.Inconclusive),
                Score:             EvidenceVoteScore.Score(caseVotes.Select(v => v.VoteType)),
                TotalVotes:        caseVotes.Count,
                CurrentUserVote:   myVote);
        }).ToList();

        return Ok(result);
    }
}

// ── Response records ─────────────────────────────────────────────────────────

public sealed record PublicCaseDiscoveryPagedResponse(
    IReadOnlyList<PublicCaseDiscoveryItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed record PublicCaseDiscoveryItem(
    Guid     CaseId,
    string   CaseReference,
    string   Title,
    string   City,
    string   State,
    string   Country,
    Ben.Data.Common.Enums.CaseStatus Status,
    bool     IsHaunted,
    DateTime DateCaseOpened,
    DateTime? DateCaseClosed,
    string   OrgName,
    string   OrgUrlName,
    int      ConfirmsCount,
    int      DisputesCount,
    int      InconclusiveCount,
    int      TotalVotes,
    // Signed total: +1 confirms, 0 inconclusive, -1 disputes. From EvidenceVoteScore, never
    // recomputed by a reader — the counts beside it are what make it trustworthy, not a substitute.
    int      Score,
    decimal? ApproxLatitude,
    decimal? ApproxLongitude,
    string?  ClientName);
