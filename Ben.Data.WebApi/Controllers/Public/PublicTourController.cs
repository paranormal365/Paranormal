using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services;
using Ben.Data.WebApi.Controllers.Entities;
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

        // What guests took, as the business accepted it. Images only — a slideshow cannot show a
        // sound file, and an audio recording deserves the player on the event's own page rather
        // than a blank frame here. Newest first and capped: a tour that has run for two years has
        // more photographs than anybody scrolls.
        var guestGallery = await db.EventEvidenceSubmissions.AsNoTracking()
            .Where(e => e.OrgCalendarEvent.TourId == tour.Id
                     && e.OrgCalendarEvent.IsPublic
                     && e.Status == EvidenceSubmissionStatus.Accepted
                     && e.UploadFile.ContentType.StartsWith("image/"))
            .OrderByDescending(e => e.DateCreated)
            .Take(40)
            .Select(e => new PublicTourGuestPhoto(
                e.Id, e.OrgCalendarEventId, e.UploadFile.FileName, e.UploadFile.ContentType,
                e.Note,
                e.SubmittedByAppUser.DisplayName ?? e.SubmittedByAppUser.UserName ?? "A guest",
                e.DateCreated))
            .ToListAsync(ct);

        return Ok(new PublicTourRecord(
            tour.Id, tour.Name, tour.UrlName, _sanitizer.SanitizeHtml(tour.Description),
            org.Id, org.Name, org.UrlName,
            MeetingPointOf(address),
            address?.City, address?.State, address?.Latitude, address?.Longitude,
            tour.DurationMinutes, tour.DefaultCapacity, tour.TimeZoneId,
            tour.ContactLine, tour.IsBookable, tour.AllowReviews,
            guides, dates, rating, ratingCount, gallery, guestGallery));
    }

    /// <summary>
    /// What this caller sent in from this tour, whatever the business made of it (item 233).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-10: a participant should be able to find their own photographs from the
    /// tour page. Until this, they could only find them from the page of the particular date they
    /// went on — which is the one thing somebody who walked a tour last month does not remember
    /// the address of.</para>
    ///
    /// <para><b>Every status, not just accepted.</b> A guest handed over their own photograph; a
    /// decline is the business choosing not to publish it, not the business coming to own it. The
    /// same rule already governs the bytes on the evidence route.</para>
    /// </remarks>
    [HttpGet("{tourId:guid}/my-evidence")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<EventEvidenceController.EvidenceSubmissionRecord>>> MyEvidence(
        Guid tourId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var mine = await db.EventEvidenceSubmissions.AsNoTracking()
            .Where(e => e.OrgCalendarEvent.TourId == tourId && e.SubmittedByAppUserId == userId)
            .OrderByDescending(e => e.DateCreated)
            .Select(e => new EventEvidenceController.EvidenceSubmissionRecord(
                e.Id, e.OrgCalendarEventId, e.OrgCalendarEvent.Title,
                e.SubmittedByAppUser.DisplayName ?? e.SubmittedByAppUser.UserName ?? "You",
                e.UploadFileId, e.UploadFile.FileName, e.UploadFile.ContentType,
                e.Note, e.Status, e.RejectionReason, e.DateCreated,
                e.PublishedToPlaceAtUtc,
                e.OrgCalendarEvent.PlaceId != null
                    && e.OrgCalendarEvent.Place!.Kind == PlaceKind.PublicLocation))
            .ToListAsync(ct);

        return Ok(mine);
    }

    // ── Reviews (item 233) ───────────────────────────────────────────────────

    /// <summary>
    /// What people who walked this tour thought of it, and whether the reader may add to it.
    /// </summary>
    /// <remarks>
    /// Anonymous: a review nobody can read before signing in is a review written for nobody.
    /// Hidden ones are absent rather than marked, except to the person who wrote one — they
    /// should not be told their words were taken down by a stranger, but nor should they be left
    /// wondering why their own review has vanished from the page.
    /// </remarks>
    [HttpGet("{tourId:guid}/reviews")]
    [AllowAnonymous]
    public async Task<ActionResult<TourReviewsRecord>> GetReviews(Guid tourId, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var tour = await db.Tours.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tourId, ct);
        if (tour is null) return NotFound();

        var userId = GetCurrentUserId();

        var rows = await db.TourReviews.AsNoTracking()
            .Where(r => r.TourId == tourId && (r.HiddenAtUtc == null || r.AppUserId == userId))
            .OrderByDescending(r => r.DateCreated)
            .Select(r => new TourReviewRecord(
                r.Id,
                r.AppUser.DisplayName ?? r.AppUser.UserName ?? "A guest",
                r.AppUser.Handle,
                r.Stars, r.Comment, r.DateCreated,
                r.AppUserId == userId,
                r.HiddenAtUtc != null))
            .ToListAsync(ct);

        var (average, count) = await PublicEventController.TourRatingAsync(db, tourId, ct);
        var mine = rows.FirstOrDefault(r => r.IsMine);
        var whyNot = await WhyCannotReviewAsync(db, tour, userId, ct);

        return Ok(new TourReviewsRecord(rows, average, count, whyNot is null, whyNot, mine));
    }

    /// <summary>
    /// Leaves or changes this caller's review of a tour.
    /// </summary>
    /// <remarks>
    /// One review per guest per tour, editable: somebody who walks the same tour three times has
    /// one opinion of it, not three. Editing clears a hiding, because a rewritten review is a
    /// different set of words and the business gets to judge it afresh.
    /// </remarks>
    [HttpPut("{tourId:guid}/my-review")]
    [Authorize]
    public async Task<ActionResult<TourReviewsRecord>> UpsertReview(
        Guid tourId, [FromBody] UpsertTourReviewRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (request.Stars is < 1 or > 5) return BadRequest("A rating is one to five stars.");
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();
        if (comment is { Length: > 1000 }) return BadRequest("That is longer than 1,000 characters.");

        await using var db = await _db.CreateDbContextAsync(ct);
        var tour = await db.Tours.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tourId, ct);
        if (tour is null) return NotFound();

        if (await WhyCannotReviewAsync(db, tour, userId, ct) is { } refusal) return BadRequest(refusal);

        var attended = await AttendedDateAsync(db, tourId, userId, ct);
        var now = DateTime.UtcNow;

        var existing = await db.TourReviews
            .FirstOrDefaultAsync(r => r.TourId == tourId && r.AppUserId == userId, ct);

        if (existing is null)
        {
            db.TourReviews.Add(new TourReview
            {
                Id = Guid.NewGuid(), TourId = tourId, OrgCalendarEventId = attended!.Value,
                AppUserId = userId, Stars = request.Stars, Comment = comment,
                DateCreated = now, CreatedByAppUserId = userId,
            });
        }
        else
        {
            existing.Stars = request.Stars;
            existing.Comment = comment;
            // Rewritten words are new words; a business judging the old ones has judged something
            // that is no longer there.
            existing.HiddenAtUtc = null;
            existing.HiddenByAppUserId = null;
            existing.DateUpdated = now;
            existing.UpdatedByAppUserId = userId;
        }

        await db.SaveChangesAsync(ct);
        return await GetReviews(tourId, ct);
    }

    /// <summary>Takes this caller's own review down.</summary>
    [HttpDelete("{tourId:guid}/my-review")]
    [Authorize]
    public async Task<IActionResult> DeleteMyReview(Guid tourId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var mine = await db.TourReviews
            .FirstOrDefaultAsync(r => r.TourId == tourId && r.AppUserId == userId, ct);
        if (mine is null) return NotFound();

        db.TourReviews.Remove(mine);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Why this caller may not review this tour, or null.
    /// </summary>
    /// <remarks>
    /// One method, asked by the page and by the endpoint, so the form a guest is shown and the
    /// answer they get on submitting cannot disagree.
    /// </remarks>
    private static async Task<string?> WhyCannotReviewAsync(
        BenDataContext db, Tour tour, Guid userId, CancellationToken ct)
    {
        if (!tour.AllowReviews) return "This tour isn't taking reviews.";
        if (userId == Guid.Empty) return "Sign in to leave a review.";

        return await AttendedDateAsync(db, tour.Id, userId, ct) is null
            ? "Reviews come from people who came along — yours will open after the walk."
            : null;
    }

    /// <summary>A date of this tour the caller was accepted on and which has finished.</summary>
    private static async Task<Guid?> AttendedDateAsync(
        BenDataContext db, Guid tourId, Guid userId, CancellationToken ct)
    {
        if (userId == Guid.Empty) return null;
        var now = DateTime.UtcNow;

        return await db.OrgCalendarEventAttendees.AsNoTracking()
            .Where(a => a.AppUserId == userId
                     && a.RsvpStatus == RsvpStatus.Accepted
                     && a.OrgCalendarEvent.TourId == tourId
                     && a.OrgCalendarEvent.EndDateTime < now)
            .OrderByDescending(a => a.OrgCalendarEvent.StartDateTime)
            .Select(a => (Guid?)a.OrgCalendarEventId)
            .FirstOrDefaultAsync(ct);
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
