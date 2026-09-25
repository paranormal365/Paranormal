using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

[ApiController]
[AllowAnonymous]
[Route("api/public/search")]
[Ben.Data.WebApi.Services.FeatureGated(Ben.Data.WebApi.Services.SiteSettingKeys.FeatureDiscovery)]
public sealed class SearchController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    public SearchController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>
    /// What is around a point — the groups that serve it, and the public events near it.
    /// </summary>
    /// <remarks>
    /// <para>Backlog item #88. This endpoint already existed, already honoured every per-address
    /// privacy setting, and <b>nothing called it</b>. Ben's "let people see what is local" is very
    /// nearly a parameter that was built and never exposed, so it was extended rather than replaced —
    /// writing a third nearby implementation was the one outcome to avoid.</para>
    ///
    /// <para><b>The two tiers do not share a privacy rule, and must not.</b> An organization that
    /// ticked "searchable" is a business listing and appears exactly as precisely as it chose;
    /// grid-snapping it would break the feature rather than protect anybody. A public event is an
    /// invitation, and its location is approximate until somebody is actually attending.</para>
    ///
    /// <para><b>For events, the distance is measured to the snapped point, not the real one.</b>
    /// Publishing a true distance alongside an approximate position would hand back the position: a
    /// caller could query from three points and trilaterate. Everything reported here derives from
    /// the grid cell, so there is nothing to solve for. The cost is that an event within a mile or
    /// two of the radius edge may fall on the wrong side of it, which does not matter for browsing.</para>
    /// </remarks>
    [HttpGet("nearby")]
    public async Task<ActionResult<NearbyResults>> Nearby(
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

        var events = await NearbyEventsAsync(db, lat, lon, clampedRadius, query, ct);
        var places = await NearbyPlacesAsync(
            db, lat, lon, clampedRadius, latMin, latMax, lonMin, lonMax, query, ct);

        return Ok(new NearbyResults(
            [.. results.OrderBy(r => r.DistanceMiles)],
            events,
            places));
    }

    /// <summary>
    /// Public locations near a point that have something published at them.
    /// </summary>
    /// <remarks>
    /// <para>Item #88's third toggle, built 2026-09-17. The place hub is described as the public
    /// front door for a location and nothing let a stranger find one: no /places index, /find
    /// searches groups only, and the nav has no Places entry.</para>
    ///
    /// <para><b>Exact position, like an organization's and unlike an event's.</b> A public
    /// location is a landmark; there is nobody's home to protect, and snapping a cave to a
    /// six-mile grid would break browsing rather than protect anybody. A private residence cannot
    /// appear here at all.</para>
    ///
    /// <para><b>A place has to have earned its listing.</b> A row is created by the first group to
    /// work somewhere, so listing all of them would hand back a directory of addresses somebody
    /// once typed into a form. Counted the same way the place page counts: investigations the
    /// shared visibility predicate lets a stranger see, and published field sessions.</para>
    /// </remarks>
    private static async Task<List<NearbyPlaceResult>> NearbyPlacesAsync(
        BenDataContext db, double lat, double lon, double clampedRadius,
        decimal latMin, decimal latMax, decimal lonMin, decimal lonMax,
        string? query, CancellationToken ct)
    {
        var candidates = await db.Places.AsNoTracking()
            .Where(p => p.Kind == PlaceKind.PublicLocation
                     && p.Latitude >= latMin && p.Latitude <= latMax
                     && p.Longitude >= lonMin && p.Longitude <= lonMax)
            .Select(p => new { p.Id, p.Name, p.City, p.State, p.Latitude, p.Longitude })
            .ToListAsync(ct);

        if (candidates.Count == 0) return [];

        var placeIds = candidates.Select(p => p.Id).ToList();

        // Counted through the shared predicate, passed no organizations — the same "public only"
        // resolution the anonymous place page gets, rather than a second copy of the rules. As two
        // grouped queries rather than correlated subqueries, because the predicate is an
        // expression tree and inlining it per row would fall back to client evaluation.
        var investigationCounts = (await db.Investigations.AsNoTracking()
                .Where(InvestigationVisibilityFilter.VisibleTo([], []))
                .Where(i => i.PlaceId != null && placeIds.Contains(i.PlaceId.Value))
                .GroupBy(i => i.PlaceId!.Value)
                .Select(g => new { PlaceId = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.PlaceId, x => x.Count);

        var sessionCounts = (await db.FieldSessionUploads.AsNoTracking()
                .Where(fs => fs.PlaceId != null
                          && fs.PublishedAtUtc != null
                          && placeIds.Contains(fs.PlaceId.Value))
                .GroupBy(fs => fs.PlaceId!.Value)
                .Select(g => new { PlaceId = g.Key, Count = g.Count() })
                .ToListAsync(ct))
            .ToDictionary(x => x.PlaceId, x => x.Count);

        var places = new List<NearbyPlaceResult>();
        foreach (var candidate in candidates)
        {
            var investigationCount = investigationCounts.GetValueOrDefault(candidate.Id);
            var sessionCount = sessionCounts.GetValueOrDefault(candidate.Id);

            var p = new
            {
                candidate.Id, candidate.Name, candidate.City, candidate.State,
                candidate.Latitude, candidate.Longitude,
                InvestigationCount = investigationCount,
                SessionCount = sessionCount,
            };

            if (p.Latitude is null || p.Longitude is null) continue;
            if (p.InvestigationCount == 0 && p.SessionCount == 0) continue;

            var dist = HaversineDistance(lat, lon, (double)p.Latitude, (double)p.Longitude);
            if (dist > clampedRadius) continue;

            if (!string.IsNullOrWhiteSpace(query)
                && !(p.Name?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                && !(p.City?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)) continue;

            places.Add(new NearbyPlaceResult(
                PlaceId:            p.Id,
                Name:               p.Name,
                City:               p.City,
                State:              p.State,
                Latitude:           p.Latitude,
                Longitude:          p.Longitude,
                DistanceMiles:      Math.Round(dist, 2),
                InvestigationCount: p.InvestigationCount,
                SessionCount:       p.SessionCount));
        }

        return [.. places.OrderBy(p => p.DistanceMiles)];
    }

    /// <summary>
    /// Upcoming public events whose approximate location falls within the radius.
    /// </summary>
    /// <remarks>
    /// Visibility comes from <see cref="PublicEventController.VisibleEvents"/> — the same predicate
    /// the events pages use, so an event hidden there cannot surface here. Only future events: a
    /// discovery map exists to answer "what could I go to", and last year's walk is not that.
    /// </remarks>
    private static async Task<IReadOnlyList<NearbyEventResult>> NearbyEventsAsync(
        BenDataContext db, double lat, double lon, double radiusMiles, string? query, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var candidates = await PublicEventController.VisibleEvents(db)
            .Include(e => e.Organization)
            .Include(e => e.Place)
            .Include(e => e.OrganizationAddress)
            // For its zone: a walk's time is the walk's, not the reader's. Without this the
            // navigation is simply null and every card would have quietly fallen back to UTC.
            .Include(e => e.Tour)
            .Where(e => e.StartDateTime >= now)
            .ToListAsync(ct);

        var results = new List<NearbyEventResult>();

        foreach (var ev in candidates)
        {
            if (!string.IsNullOrWhiteSpace(query)
                && !ev.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                && !ev.Organization.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;

            // Snapped first, then measured. See the remarks on Nearby: the published distance must
            // not be a more precise fact than the published position.
            var (approxLat, approxLon) = PublicCoordinates.Approximate(
                ev.Place?.Latitude ?? ev.OrganizationAddress?.Latitude,
                ev.Place?.Longitude ?? ev.OrganizationAddress?.Longitude);

            if (approxLat is null || approxLon is null) continue;

            var dist = HaversineDistance(lat, lon, (double)approxLat.Value, (double)approxLon.Value);
            if (dist > radiusMiles) continue;

            results.Add(new NearbyEventResult(
                EventId:       ev.Id,
                Title:         ev.Title,
                UrlName:       ev.UrlName,
                OrgName:       ev.Organization.Name,
                OrgUrlName:    ev.Organization.UrlName,
                StartDateTime: ev.StartDateTime,
                City:          ev.Place?.City ?? ev.OrganizationAddress?.City,
                State:         ev.Place?.State ?? ev.OrganizationAddress?.State,
                Latitude:      approxLat,
                Longitude:     approxLon,
                DistanceMiles: Math.Round(dist, 1),
                // The night's own clock, so a card says the time it actually starts rather than
                // the time where the reader is sitting (Ben, 2026-09-10). The event's own zone
                // first, then its tour's for the dates scheduled before events had one.
                TimeZoneId:    ev.TimeZoneId ?? ev.Tour?.TimeZoneId));
        }

        return [.. results.OrderBy(r => r.StartDateTime)];
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

