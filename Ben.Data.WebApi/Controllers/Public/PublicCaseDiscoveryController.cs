using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Cross-org public case discovery. No authentication required.
/// Returns all public/haunted cases with city-level coordinates and vote aggregates.
/// </summary>
[ApiController]
[Route("api/public/cases")]
[AllowAnonymous]
public sealed class PublicCaseDiscoveryController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMemoryCache _cache;

    public PublicCaseDiscoveryController(IDbContextFactory<BenDataContext> db, IMemoryCache cache)
    { _db = db; _cache = cache; }

    /// <summary>
    /// Returns paginated public cases across all organizations.
    /// sort: "votes" (default) | "date"
    /// City-level coordinates are geocoded once and cached for 24 h.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PublicCaseDiscoveryPagedResponse>> GetAll(
        [FromQuery] int    page     = 1,
        [FromQuery] int    pageSize = 20,
        [FromQuery] string sort     = "votes",
        CancellationToken  ct       = default)
    {
        if (page < 1) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 100);

        await using var db = await _db.CreateDbContextAsync(ct);

        var cases = await db.Cases.AsNoTracking()
            .Include(c => c.Organization)
            .Where(c => c.IsPublic
                     && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted))
            .ToListAsync(ct);

        if (cases.Count == 0)
            return Ok(new PublicCaseDiscoveryPagedResponse([], 0, page, pageSize));

        var caseIds = cases.Select(c => c.Id).ToList();

        // Aggregate evidence vote counts for all cases in one query
        var voteCounts = await db.EvidenceVotes.AsNoTracking()
            .Join(db.CaseTimelineEntryFiles,
                  ev => ev.UploadFileId,
                  f  => f.UploadFileId,
                  (ev, f) => new { ev, f.CaseTimelineEntryId })
            .Join(db.CaseTimelineEntries,
                  x  => x.CaseTimelineEntryId,
                  e  => e.Id,
                  (x, e) => new { x.ev, e.CaseId })
            .Where(x => caseIds.Contains(x.CaseId))
            .GroupBy(x => x.CaseId)
            .Select(g => new
            {
                CaseId       = g.Key,
                Total        = g.Count(),
                Confirms     = g.Count(x => x.ev.VoteType == EvidenceVoteType.Confirms),
                Disputes     = g.Count(x => x.ev.VoteType == EvidenceVoteType.Disputes),
                Inconclusive = g.Count(x => x.ev.VoteType == EvidenceVoteType.Inconclusive),
            })
            .ToDictionaryAsync(x => x.CaseId, ct);

        // Geocode each unique city/state combination (cached 24 h per city)
        var geoCache = new Dictionary<(string City, string State, string Country), (decimal? Lat, decimal? Lon)>();
        foreach (var loc in cases.Select(c => (c.City, c.State, c.Country)).Distinct())
        {
            var cacheKey = $"geo:{loc.City}|{loc.State}|{loc.Country}";
            if (!_cache.TryGetValue(cacheKey, out (decimal? Lat, decimal? Lon) cached))
            {
                var q   = string.IsNullOrWhiteSpace(loc.City)
                    ? $"{loc.State}, {loc.Country}"
                    : $"{loc.City}, {loc.State}, {loc.Country}";
                var geo = await AddressGeocodingService.TryResolveFromQueryAsync(q, ct);
                cached  = (geo.Latitude.HasValue  ? (decimal)geo.Latitude.Value  : null,
                           geo.Longitude.HasValue ? (decimal)geo.Longitude.Value : null);
                _cache.Set(cacheKey, cached, TimeSpan.FromHours(24));
            }
            geoCache[(loc.City, loc.State, loc.Country)] = cached;
        }

        // Build response items
        var items = cases.Select(c =>
        {
            voteCounts.TryGetValue(c.Id, out var vc);
            geoCache.TryGetValue((c.City, c.State, c.Country), out var geo);
            return new PublicCaseDiscoveryItem(
                CaseId:            c.Id,
                CaseReference:     $"#{c.CaseYear}-{c.OrgCaseNumber:D3}",
                Title:             c.Title,
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
                TotalVotes:        vc?.Total        ?? 0,
                ApproxLatitude:    geo.Lat,
                ApproxLongitude:   geo.Lon,
                ClientName:        string.IsNullOrWhiteSpace(c.PublicPseudonym) ? null : c.PublicPseudonym);
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
    decimal? ApproxLatitude,
    decimal? ApproxLongitude,
    string?  ClientName);
