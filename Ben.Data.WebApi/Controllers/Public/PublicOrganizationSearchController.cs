using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Public organization discovery — no authentication required.
/// Returns orgs ordered by proximity to a submitted location.
/// CENTER COORDINATES ARE NEVER INCLUDED IN RESPONSES.
/// </summary>
[ApiController]
[Route("api/public/organizations")]
[AllowAnonymous]
public sealed class PublicOrganizationSearchController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicOrganizationSearchController(IDbContextFactory<BenDataContext> db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns organizations that are accepting clients, ordered by proximity to the
    /// submitted location. Results are split into two groups:
    ///   1) Within operating range (ordered by distance, closest first)
    ///   2) Outside range but explicitly accepting outside-range clients (ordered by
    ///      how far past their range edge the search point is)
    ///
    /// The center coordinates of each org are NEVER returned — only the display label,
    /// distance, and whether the search point falls within their declared range.
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<OrgSearchResult>>> Search(
        [FromQuery] double lat,
        [FromQuery] double lon,
        [FromQuery] int maxResults = 20,
        CancellationToken ct = default)
    {
        if (lat < -90 || lat > 90 || lon < -180 || lon > 180)
            return BadRequest("Invalid coordinates.");

        await using var db = await _db.CreateDbContextAsync(ct);

        // Load orgs that are accepting clients AND have an area configured
        var orgs = await db.Organizations
            .AsNoTracking()
            .Where(o => o.IsAcceptingClients && o.AreaOfOperation != null)
            .Select(o => new
            {
                o.Id,
                o.Name,
                o.UrlName,
                o.IsAcceptingClients,
                o.AcceptsClientsOutsideRange,
                o.AreaOfOperation!.RadiusMiles,
                // Private coords — used only for distance math, never returned
                CenterLat = (double)o.AreaOfOperation.CenterLatitude,
                CenterLon = (double)o.AreaOfOperation.CenterLongitude,
                o.AreaOfOperation.DisplayLabel,
                ActiveLogoFileId = o.OrganizationLogos
                    .Where(l => l.IsActive)
                    .Select(l => (Guid?)l.UploadFileId)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var results = new List<OrgSearchResult>();

        foreach (var org in orgs)
        {
            double dist = HaversineDistanceMiles(lat, lon, org.CenterLat, org.CenterLon);
            double radius = (double)org.RadiusMiles;
            bool withinRange = dist <= radius;

            if (!withinRange && !org.AcceptsClientsOutsideRange)
                continue;  // Outside range and not accepting outside — skip

            results.Add(new OrgSearchResult(
                OrganizationId:              org.Id,
                Name:                        org.Name,
                UrlName:                     org.UrlName,
                DisplayLabel:                org.DisplayLabel,
                RadiusMiles:                 (double)org.RadiusMiles,
                DistanceFromSearchMiles:     Math.Round(dist, 1),
                IsWithinRange:               withinRange,
                AcceptsClientsOutsideRange:  org.AcceptsClientsOutsideRange,
                ActiveLogoFileId:            org.ActiveLogoFileId,
                // Sort key: within-range orgs sort by distance; outside-range by miles past edge
                SortKey:                     withinRange ? dist : radius + (dist - radius) * 10000
            ));
        }

        var ordered = results
            .OrderBy(r => r.IsWithinRange ? 0 : 1)   // within-range first
            .ThenBy(r => r.SortKey)
            .Take(maxResults)
            .Select(r => r with { SortKey = 0 })      // strip internal sort key before returning
            .ToList();

        return Ok(ordered);
    }

    /// <summary>
    /// Every organization, newest-relevant first, with no location required.
    /// </summary>
    /// <remarks>
    /// <para>The search above needs coordinates and skips any org without an area of operation
    /// configured, which left the site with no way to see the full list at all — the "Browse All
    /// Groups" button had nowhere to go. This is that list.</para>
    ///
    /// <para>Every organization already has a public page at <c>/o/{urlName}</c> served to anyone
    /// who knows the name, so listing them exposes nothing new; it only makes them findable.
    /// Coordinates are still never returned — only the area's display label, same as search.</para>
    /// </remarks>
    [HttpGet("browse")]
    public async Task<ActionResult<OrgBrowsePage>> Browse(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        await using var db = await _db.CreateDbContextAsync(ct);

        var query = db.Organizations.AsNoTracking();
        var total = await query.CountAsync(ct);

        var items = await query
            // Groups taking new clients come first — that is what someone browsing is looking for.
            // Name breaks the tie so paging is stable rather than at the database's discretion.
            .OrderByDescending(o => o.IsAcceptingClients)
            .ThenBy(o => o.Name)
            .ThenBy(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrgBrowseResult(
                o.Id,
                o.Name,
                o.UrlName,
                o.AreaOfOperation != null ? o.AreaOfOperation.DisplayLabel : null,
                o.AreaOfOperation != null ? (double?)o.AreaOfOperation.RadiusMiles : null,
                o.IsAcceptingClients,
                o.OrganizationLogos
                    .Where(l => l.IsActive)
                    .Select(l => (Guid?)l.UploadFileId)
                    .FirstOrDefault()))
            .ToListAsync(ct);

        return Ok(new OrgBrowsePage(items, total, page, pageSize));
    }

    // Haversine formula — duplicated here since WebApi cannot reference Ben.Web.Library
    private static double HaversineDistanceMiles(
        double lat1, double lon1, double lat2, double lon2)
    {
        const double R     = 3958.8;
        const double toRad = Math.PI / 180.0;
        double dLat = (lat2 - lat1) * toRad;
        double dLon = (lon2 - lon1) * toRad;
        double a    = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                      Math.Cos(lat1 * toRad) * Math.Cos(lat2 * toRad) *
                      Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

/// <summary>
/// Public search result — deliberately excludes CenterLatitude and CenterLongitude.
/// </summary>
public sealed record OrgSearchResult(
    Guid OrganizationId,
    string Name,
    string UrlName,
    string? DisplayLabel,
    double RadiusMiles,
    double DistanceFromSearchMiles,
    bool IsWithinRange,
    bool AcceptsClientsOutsideRange,
    Guid? ActiveLogoFileId,
    [property: System.Text.Json.Serialization.JsonIgnore] double SortKey);

/// <summary>
/// One organization in the location-free browse listing.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="OrgSearchResult"/>: that record carries a distance and a
/// within-range flag, and there is no search point here to measure either against. Reusing it
/// would mean filling two fields with zeroes and hoping nobody reads them.
/// </remarks>
/// AreaLabel is the human-readable area, e.g. "Nashville, TN", and is null when none is set;
/// RadiusMiles is the declared operating radius, null when no area is configured.
public sealed record OrgBrowseResult(
    Guid OrganizationId,
    string Name,
    string UrlName,
    string? AreaLabel,
    double? RadiusMiles,
    bool IsAcceptingClients,
    Guid? ActiveLogoFileId);

/// <summary>One page of the browse listing, with the total so the caller can page properly.</summary>
public sealed record OrgBrowsePage(
    IReadOnlyList<OrgBrowseResult> Items,
    int TotalCount,
    int Page,
    int PageSize);
