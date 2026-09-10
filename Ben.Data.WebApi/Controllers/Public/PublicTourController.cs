using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Tours as a visitor meets them: on a map, in a search, and on a page (item 233).
/// </summary>
/// <remarks>
/// <para><b>Ben, 2026-09-10:</b> "Tours are public so, they show up on the map and are
/// searchable." That is the whole of the visibility rule — there is no private tour. A business
/// that does not want one advertised retires it or pauses it, and both are visible states rather
/// than a hidden one.</para>
///
/// <para><b>The meeting point is given in full</b>, unlike a case or a private investigation.
/// A tour exists to be turned up to, and an address withheld from somebody deciding whether to
/// come is withheld from exactly the wrong reader.</para>
/// </remarks>
[ApiController]
[Route("api/public/tours")]
[FeatureGated(SiteSettingKeys.FeatureEvents)]
public sealed class PublicTourController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly ICmsMarkupSanitizer _sanitizer;

    public PublicTourController(IDbContextFactory<BenDataContext> db, ICmsMarkupSanitizer sanitizer)
    { _db = db; _sanitizer = sanitizer; }

    /// <summary>Tours a business runs, bookable ones first.</summary>
    [HttpGet("~/api/public/organizations/{orgUrlName}/tours")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PublicTourListItem>>> GetForOrganization(
        string orgUrlName, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var (org, _) = await OrganizationUrlNames.ResolveAsync(db, orgUrlName, ct);
        if (org is null) return Ok(Array.Empty<PublicTourListItem>());

        var tours = await VisibleTours(db).Where(t => t.OrganizationId == org.Id).ToListAsync(ct);
        return Ok(await ToListItemsAsync(db, tours, null, ct));
    }

    /// <summary>One tour's page, with its next dates.</summary>
    [HttpGet("~/api/public/organizations/{orgUrlName}/tours/{tourSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicTourRecord>> GetOne(
        string orgUrlName, string tourSlug, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var (org, _) = await OrganizationUrlNames.ResolveAsync(db, orgUrlName, ct);
        if (org is null) return NotFound();

        var tour = await VisibleTours(db)
            .Include(t => t.StartOrganizationAddress)
            .Include(t => t.Guides).ThenInclude(g => g.AppUser)
            .FirstOrDefaultAsync(t => t.OrganizationId == org.Id && t.UrlName == tourSlug, ct);
        if (tour is null) return NotFound();

        var now = DateTime.UtcNow;
        var dateIds = await PublicEventController.VisibleEvents(db)
            .Where(e => e.TourId == tour.Id && e.EndDateTime >= now)
            .OrderBy(e => e.StartDateTime)
            .Take(50)
            .Select(e => new
            {
                e.Id, e.UrlName, e.Title, e.StartDateTime, e.EndDateTime, e.IsAllDay, e.MeetingUrl,
                e.AttendeeCapacity,
                Attending = e.Attendees.Count(a => a.RsvpStatus == RsvpStatus.Accepted),
            })
            .ToListAsync(ct);

        var address = tour.StartOrganizationAddress;
        var dates = dateIds.Select(d => new PublicEventListItem(
            d.Id, d.UrlName, org.Id, org.Name, org.UrlName, d.Title,
            d.StartDateTime, d.EndDateTime, d.IsAllDay,
            address?.City, address?.State, address?.Latitude, address?.Longitude,
            d.Attending, d.AttendeeCapacity,
            IsOnline: !string.IsNullOrWhiteSpace(d.MeetingUrl),
            TourName: tour.Name, TourUrlName: tour.UrlName)).ToList();

        var guides = await GuidesOfAsync(db, tour, ct);
        var (rating, ratingCount) = await PublicEventController.TourRatingAsync(db, tour.Id, ct);

        var gallery = await db.TourGalleryImages.AsNoTracking()
            .Where(g => g.TourId == tour.Id).OrderBy(g => g.SortOrder)
            .Select(g => new PublicTourImage(g.UploadFileId, g.Caption))
            .ToListAsync(ct);

        return Ok(new PublicTourRecord(
            tour.Id, tour.Name, tour.UrlName, _sanitizer.SanitizeHtml(tour.Description),
            org.Id, org.Name, org.UrlName,
            MeetingPointOf(address),
            address?.City, address?.State, address?.Latitude, address?.Longitude,
            tour.DurationMinutes, tour.DefaultCapacity, tour.TimeZoneId,
            tour.ContactLine, tour.IsBookable, tour.AllowReviews,
            guides, dates, rating, ratingCount, gallery));
    }

    /// <summary>Every tour that can be drawn on a map.</summary>
    /// <remarks>
    /// Deliberately the smallest shape in this file: a pin needs a point, a name and somewhere to
    /// go when it is tapped, and a map that carried descriptions would download a brochure for
    /// every tour in the country to draw forty dots.
    /// </remarks>
    [HttpGet("map")]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PublicTourMapPin>>> GetMapPins(
        [FromQuery] int maxResults = 500, CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var rows = await VisibleTours(db)
            .Where(t => t.StartOrganizationAddress.Latitude != null
                     && t.StartOrganizationAddress.Longitude != null)
            .Take(Math.Clamp(maxResults, 1, 1000))
            .Select(t => new
            {
                t.Id, t.Name, t.UrlName,
                OrgName = t.Organization.Name, OrgUrl = t.Organization.UrlName,
                Lat = t.StartOrganizationAddress.Latitude!.Value,
                Lon = t.StartOrganizationAddress.Longitude!.Value,
                Next = db.OrgCalendarEvents
                    .Where(e => e.TourId == t.Id && e.IsPublic && e.EndDateTime >= now)
                    .OrderBy(e => e.StartDateTime)
                    .Select(e => (DateTime?)e.StartDateTime).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Ok(rows.Select(r => new PublicTourMapPin(
            r.Id, r.Name, r.UrlName, r.OrgName, r.OrgUrl, r.Lat, r.Lon, r.Next)).ToList());
    }

    /// <summary>
    /// Tours by name, by the business that runs them, or near a point.
    /// </summary>
    /// <remarks>
    /// The distance filter is the same bounding box the nearby search uses, for the same reason:
    /// a box is an index seek and a great-circle distance is a table scan, so the box narrows and
    /// the arithmetic decides.
    /// </remarks>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PublicTourListItem>>> Search(
        [FromQuery] string? query = null,
        [FromQuery] double? lat = null, [FromQuery] double? lon = null,
        [FromQuery] double radiusMiles = 25,
        [FromQuery] int maxResults = 50,
        CancellationToken ct = default)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var tours = VisibleTours(db).Include(t => t.StartOrganizationAddress);

        IQueryable<Tour> filtered = tours;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim();
            filtered = filtered.Where(t =>
                t.Name.Contains(text)
                || t.Organization.Name.Contains(text)
                || t.StartOrganizationAddress.City.Contains(text));
        }

        (double Lat, double Lon)? from = null;
        if (lat is double la && lon is double lo)
        {
            var radius = Math.Clamp(radiusMiles, 0.1, 100);
            var latDelta = radius / 69.0;
            var lonDelta = radius / (69.0 * Math.Cos(la * Math.PI / 180.0));
            var latMin = (decimal)(la - latDelta); var latMax = (decimal)(la + latDelta);
            var lonMin = (decimal)(lo - lonDelta); var lonMax = (decimal)(lo + lonDelta);
            filtered = filtered.Where(t =>
                t.StartOrganizationAddress.Latitude >= latMin && t.StartOrganizationAddress.Latitude <= latMax
                && t.StartOrganizationAddress.Longitude >= lonMin && t.StartOrganizationAddress.Longitude <= lonMax);
            from = (la, lo);
        }

        var rows = await filtered.Take(Math.Clamp(maxResults, 1, 200)).ToListAsync(ct);
        var items = await ToListItemsAsync(db, rows, from, ct);

        if (from is not null)
            items = [.. items.Where(i => i.DistanceMiles <= Math.Clamp(radiusMiles, 0.1, 100))
                             .OrderBy(i => i.DistanceMiles)];

        return Ok(items);
    }

    // ── plumbing ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The tours a visitor may see: every tour that has not been retired.
    /// </summary>
    /// <remarks>
    /// A paused tour is still listed, because a guest looking for it should find it and read that
    /// it is not taking sign-ups just now — vanishing reads as "this business no longer exists".
    /// </remarks>
    private static IQueryable<Tour> VisibleTours(BenDataContext db)
        => db.Tours.AsNoTracking()
            .Where(t => t.RetiredAtUtc == null)
            .OrderBy(t => t.IsBookable ? 0 : 1).ThenBy(t => t.Name);

    private static async Task<List<PublicTourListItem>> ToListItemsAsync(
        BenDataContext db, List<Tour> tours, (double Lat, double Lon)? from, CancellationToken ct)
    {
        var ids = tours.Select(t => t.Id).ToList();
        var now = DateTime.UtcNow;

        var dates = await db.OrgCalendarEvents.AsNoTracking()
            .Where(e => e.TourId != null && ids.Contains(e.TourId.Value)
                     && e.IsPublic && e.EndDateTime >= now)
            .Select(e => new { TourId = e.TourId!.Value, e.StartDateTime })
            .ToListAsync(ct);

        var ratings = await db.TourReviews.AsNoTracking()
            .Where(r => ids.Contains(r.TourId) && r.HiddenAtUtc == null)
            .Select(r => new { r.TourId, r.Stars })
            .ToListAsync(ct);

        // The first picture only: a card shows one, and pulling every gallery for a list of
        // forty tours would download a brochure to draw forty thumbnails.
        var covers = await db.TourGalleryImages.AsNoTracking()
            .Where(g => ids.Contains(g.TourId))
            .GroupBy(g => g.TourId)
            .Select(g => new { TourId = g.Key, UploadFileId = g.OrderBy(x => x.SortOrder).First().UploadFileId })
            .ToListAsync(ct);

        var addresses = await db.OrganizationAddresses.AsNoTracking()
            .Where(a => tours.Select(t => t.StartOrganizationAddressId).Contains(a.Id))
            .ToListAsync(ct);
        var orgs = await db.Organizations.AsNoTracking()
            .Where(o => tours.Select(t => t.OrganizationId).Contains(o.Id))
            .Select(o => new { o.Id, o.Name, o.UrlName })
            .ToListAsync(ct);

        return [.. tours.Select(t =>
        {
            var address = t.StartOrganizationAddress ?? addresses.FirstOrDefault(a => a.Id == t.StartOrganizationAddressId);
            var org = orgs.First(o => o.Id == t.OrganizationId);
            var mine = dates.Where(d => d.TourId == t.Id).ToList();
            var stars = ratings.Where(r => r.TourId == t.Id).Select(r => r.Stars).ToList();

            return new PublicTourListItem(
                t.Id, t.Name, t.UrlName, org.Id, org.Name, org.UrlName,
                address?.City, address?.State, address?.Latitude, address?.Longitude,
                t.DurationMinutes,
                mine.Count == 0 ? null : mine.Min(d => d.StartDateTime),
                mine.Count,
                stars.Count == 0 ? null : Math.Round((decimal)stars.Average(), 1, MidpointRounding.AwayFromZero),
                stars.Count,
                from is { } f && address?.Latitude is { } alat && address.Longitude is { } alon
                    ? Distance.Miles(f.Lat, f.Lon, (double)alat, (double)alon)
                    : null,
                covers.FirstOrDefault(c => c.TourId == t.Id)?.UploadFileId);
        })];
    }

    private static async Task<List<PublicGuideRecord>> GuidesOfAsync(
        BenDataContext db, Tour tour, CancellationToken ct)
    {
        var ids = tour.Guides.Select(g => g.AppUserId).ToList();
        var photos = await db.AppUserPhotos.AsNoTracking()
            .Where(p => ids.Contains(p.AppUserId) && p.IsPublic && p.IsActive)
            .Select(p => new { p.AppUserId, p.UploadFileId })
            .ToListAsync(ct);

        return [.. tour.Guides.OrderBy(g => g.SortOrder).Select(g => new PublicGuideRecord(
            g.AppUser.DisplayName ?? "A guide",
            g.AppUser.Handle,
            photos.FirstOrDefault(p => p.AppUserId == g.AppUserId)?.UploadFileId))];
    }

    /// <summary>The meeting point in full, as a guest would read it off a leaflet.</summary>
    internal static string MeetingPointOf(OrganizationAddress? address)
        => address is null
            ? "Ask the business where to meet"
            : string.Join(", ", new[]
            {
                address.StreetAddress1, address.StreetAddress2,
                address.City, address.State, address.ZipCode,
            }.Where(p => !string.IsNullOrWhiteSpace(p)));
}

/// <summary>Great-circle miles between two points.</summary>
internal static class Distance
{
    private const double EarthRadiusMiles = 3958.8;

    public static double Miles(double lat1, double lon1, double lat2, double lon2)
    {
        double Rad(double d) => d * Math.PI / 180.0;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return Math.Round(EarthRadiusMiles * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a)), 1);
    }
}
