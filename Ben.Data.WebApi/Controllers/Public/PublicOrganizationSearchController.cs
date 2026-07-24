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
