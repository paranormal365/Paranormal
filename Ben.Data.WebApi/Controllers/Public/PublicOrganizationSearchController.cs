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

        // Item 194: whether each group may take private-residence work, resolved once for the
        // whole result set rather than per card — and needed BEFORE ordering, because it is part
        // of the order.
        var canPrivate = await Ben.Data.Source.Services.TierAreaResolution.WithCapabilityAsync(
            db, results.Select(r => r.OrganizationId).ToList(),
            Ben.Data.Common.Enums.TierCapability.PrivateResidenceCases, ct);

        // Ben asked whether paid groups can be promoted over free ones. They are — but only
        // WITHIN a range bucket, never across one, and never over distance's bucket boundary.
        //
        // Promoting globally would put a paid group forty miles away above a free one down the
        // road, which is worse for the person searching and is the pay-to-win shape that makes a
        // directory untrustworthy. Inside a bucket every group is equally reachable, so leading
        // with the ones that can actually take a private-residence case is a service to the
        // searcher rather than a tax on them — most people typing their address here are asking
        // about their own home.
        var ordered = results
            .Select(r => r with { TakesPrivateResidenceCases = canPrivate.Contains(r.OrganizationId) })
            .OrderBy(r => r.IsWithinRange ? 0 : 1)              // reachability first, always
            .ThenBy(r => r.TakesPrivateResidenceCases ? 0 : 1)  // then the paid promotion
            .ThenBy(r => r.SortKey)                             // then distance
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
    /// <param name="page">1-based.</param>
    /// <param name="pageSize">Clamped.</param>
    /// <param name="toursOnly">
    /// Narrow to groups that run public walking tours (2026-08-24). Matches the CAPABILITY,
    /// not the kind, so an investigation group that also runs tours is found here too — which
    /// is the whole reason the capability is separate from the kind.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    [HttpGet("browse")]
    public async Task<ActionResult<OrgBrowsePage>> Browse(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 24,
        [FromQuery] bool toursOnly = false,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        await using var db = await _db.CreateDbContextAsync(ct);

        var query = db.Organizations.AsNoTracking();
        if (toursOnly) query = query.Where(o => o.RunsPublicTours);
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
                    .FirstOrDefault(),
                o.Kind,
                o.RunsPublicTours))
            .ToListAsync(ct);

        // One resolution for the whole page (item 194) — asking per card is the N+1 that turns a
        // browse into forty round trips.
        var canPrivate = await Ben.Data.Source.Services.TierAreaResolution.WithCapabilityAsync(
            db, items.Select(i => i.OrganizationId).ToList(),
            Ben.Data.Common.Enums.TierCapability.PrivateResidenceCases, ct);
        items = items
            .Select(i => i with { TakesPrivateResidenceCases = canPrivate.Contains(i.OrganizationId) })
            // Paid promotion, applied WITHIN the page the database already chose. Ordering by tier
            // in the query instead would change which groups land on page one — and a group's
            // visibility should not depend on how the person paged to it. Accepting-new-cases
            // still leads, because a promoted group that cannot take the case helps nobody.
            .OrderByDescending(i => i.IsAcceptingClients)
            .ThenBy(i => i.TakesPrivateResidenceCases ? 0 : 1)
            .ThenBy(i => i.Name)
            .ToList();

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
/// <remarks>
/// <c>TakesPrivateResidenceCases</c> (item 194) is whether this group's plan lets it take
/// private-residence work. Somebody with a haunted HOME needs it before they choose, not after:
/// the transfer gate already refuses the wrong group politely, but only once they have picked
/// one. Fail-open like every capability — a group with no resolvable tier reads as able.
/// </remarks>
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
    [property: System.Text.Json.Serialization.JsonIgnore] double SortKey,
    bool TakesPrivateResidenceCases = true);

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
/// <remarks>
/// <para><c>Kind</c> is what this group primarily is (2026-08-24) — the badge on its card — and
/// <c>RunsPublicTours</c> is whether it runs public walking tours whatever kind it primarily is.
/// </para>
/// <para><c>TakesPrivateResidenceCases</c> (item 194) is whether its plan lets it take
/// private-residence work.</para>
/// </remarks>
public sealed record OrgBrowseResult(
    Guid OrganizationId,
    string Name,
    string UrlName,
    string? AreaLabel,
    double? RadiusMiles,
    bool IsAcceptingClients,
    Guid? ActiveLogoFileId,
    Ben.Data.Common.Enums.OrganizationKind Kind = Ben.Data.Common.Enums.OrganizationKind.InvestigationGroup,
    bool RunsPublicTours = false,
    bool TakesPrivateResidenceCases = true);

/// <summary>One page of the browse listing, with the total so the caller can page properly.</summary>
public sealed record OrgBrowsePage(
    IReadOnlyList<OrgBrowseResult> Items,
    int TotalCount,
    int Page,
    int PageSize);
