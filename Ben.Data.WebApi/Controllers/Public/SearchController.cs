using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

[ApiController]
[AllowAnonymous]
[Route("api/public/search")]
public sealed class SearchController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    public SearchController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>
    /// Find organizations with searchable addresses near a lat/lon point.
    /// Respects IsSearchable, SearchVisibility, and SearchRadiusMiles per address.
    /// </summary>
    [HttpGet("nearby")]
    public async Task<ActionResult<IReadOnlyList<NearbyOrgResult>>> Nearby(
        [FromQuery] double lat,
        [FromQuery] double lon,
        [FromQuery] double radiusMiles = 25,
        [FromQuery] string? query = null,
        CancellationToken ct = default)
    {
        var clampedRadius = Math.Clamp(radiusMiles, 0.1, 100);

        await using var db = await _db.CreateDbContextAsync(ct);

        // Bounding-box pre-filter (1 degree lat ≈ 69 miles)
        var latDelta = clampedRadius / 69.0;
        var lonDelta = clampedRadius / (69.0 * Math.Cos(lat * Math.PI / 180.0));
        var latMin = (decimal)(lat - latDelta);
        var latMax = (decimal)(lat + latDelta);
        var lonMin = (decimal)(lon - lonDelta);
        var lonMax = (decimal)(lon + lonDelta);

        var addresses = await db.OrganizationAddresses
            .AsNoTracking()
            .Include(a => a.Organization)
            .Include(a => a.MapConfig)
            .Where(a => a.IsSearchable
                     && a.SearchVisibility == OrganizationAddressVisibility.Public
                     && a.Latitude  >= latMin && a.Latitude  <= latMax
                     && a.Longitude >= lonMin && a.Longitude <= lonMax)
            .ToListAsync(ct);

        // Exact Haversine filter + SearchRadiusMiles check
        var results = new List<NearbyOrgResult>();
        foreach (var addr in addresses)
        {
            if (addr.Latitude is null || addr.Longitude is null) continue;
            var dist = HaversineDistance(lat, lon, (double)addr.Latitude, (double)addr.Longitude);
            if (dist > clampedRadius) continue;
            if (addr.SearchRadiusMiles.HasValue && dist > addr.SearchRadiusMiles.Value) continue;

            var org = addr.Organization;
            if (!string.IsNullOrWhiteSpace(query) &&
                !org.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;

            results.Add(new NearbyOrgResult(
                OrgId:             org.Id,
                OrgName:           org.Name,
                OrgUrlName:        org.UrlName,
                DistanceMiles:     Math.Round(dist, 2),
                Visibility:        addr.Visibility,
                PublicDisplayMode: addr.PublicDisplayMode,
                Latitude:          addr.PublicDisplayMode == OrganizationAddressDisplayMode.RegionOnly
                                       ? null : addr.Latitude,
                Longitude:         addr.PublicDisplayMode == OrganizationAddressDisplayMode.RegionOnly
                                       ? null : addr.Longitude,
                RegionRadiusMiles: addr.MapConfig?.RegionRadiusMiles,
                StreetAddress1:    addr.PublicDisplayMode == OrganizationAddressDisplayMode.FullAddressAndMap ||
                                   addr.PublicDisplayMode == OrganizationAddressDisplayMode.FullAddressOnly
                                       ? addr.StreetAddress1 : null,
                City:              addr.PublicDisplayMode == OrganizationAddressDisplayMode.FullAddressAndMap ||
                                   addr.PublicDisplayMode == OrganizationAddressDisplayMode.FullAddressOnly
                                       ? addr.City : null,
                State:             addr.PublicDisplayMode == OrganizationAddressDisplayMode.FullAddressAndMap ||
                                   addr.PublicDisplayMode == OrganizationAddressDisplayMode.FullAddressOnly
                                       ? addr.State : null));
        }

        return Ok(results.OrderBy(r => r.DistanceMiles).ToList());
    }

    private static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 3958.8; // Earth radius in miles
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

public sealed record NearbyOrgResult(
    Guid     OrgId,
    string   OrgName,
    string   OrgUrlName,
    double   DistanceMiles,
    OrganizationAddressVisibility  Visibility,
    OrganizationAddressDisplayMode PublicDisplayMode,
    decimal? Latitude,
    decimal? Longitude,
    double?  RegionRadiusMiles,
    string?  StreetAddress1,
    string?  City,
    string?  State);
