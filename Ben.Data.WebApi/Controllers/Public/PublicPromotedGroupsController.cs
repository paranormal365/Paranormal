using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The promoted-group cards for the public placements (item 166 W3) — the group finder's
/// "Featured groups" and the home page's discovery section.
/// </summary>
/// <remarks>
/// <para><b>Approved only, everywhere in this controller.</b> The one invariant the whole
/// review chain exists for: nothing a group wrote reaches an anonymous visitor until a
/// SuperAdmin approved exactly that text. The image route repeats the check rather than
/// trusting the id — an unapproved ad's image is as unpublished as its words.</para>
///
/// <para>Random order per request gives the even rotation the spec asks for; every approved
/// ad gets the same chance on every load. Anonymous, and traced on the anonymous path.</para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/promoted-groups")]
public sealed class PublicPromotedGroupsController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IFileStorageService _storage;

    public PublicPromotedGroupsController(
        IDbContextFactory<BenDataContext> dbFactory, IFileStorageService storage)
    {
        _dbFactory = dbFactory;
        _storage   = storage;
    }

    /// <param name="take">How many cards, capped.</param>
    /// <param name="lat">The viewer's consented latitude (item 186 F8). Optional, session-only
    /// on the client, never stored here — it orders this one response and is gone.</param>
    /// <param name="lon">Its longitude. Both or neither.</param>
    /// <param name="ct">Cancellation.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PromotedGroupCard>>> Get(
        [FromQuery] int take = 3, [FromQuery] double? lat = null, [FromQuery] double? lon = null,
        CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 10);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ads = await db.OrganizationAds.AsNoTracking()
            .Where(a => a.Status == OrganizationAdStatus.Approved)
            // Belt as well as braces: a personal organization should never have an approved ad,
            // because nobody would approve one — but "a human would have caught it" is not a
            // rule, and this card is the most prominent placement on the site.
            .Where(Services.PersonalOrganizations.DiscoverableVia<Ben.Data.Source.Entities.OrganizationAd>(
                a => a.Organization))
            .Select(a => new
            {
                a.Id, a.Headline, a.Body,
                OrgId = a.OrganizationId,
                OrgName = a.Organization.Name,
                OrgUrlName = a.Organization.UrlName,
                a.TargetKind,
                HasImage = a.ImageUploadFileId != null,
            })
            .ToListAsync(ct);
        if (ads.Count == 0) return Ok(Array.Empty<PromotedGroupCard>());

        // ── Geo feeding (item 186 F8) ────────────────────────────────────────
        // Distance comes from the group's nearest PUBLIC, searchable address — the same rows the
        // nearby search shows anyone. An AreaOfOperation deliberately yields NO distance: its
        // centre exists to hide where a home-based group actually is, and measuring to it would
        // publish a number derived from the very point the feature conceals.
        Dictionary<Guid, double> distances = [];
        if (lat is { } viewerLat && lon is { } viewerLon)
        {
            var orgIds = ads.Select(a => a.OrgId).Distinct().ToList();
            var addresses = await db.OrganizationAddresses.AsNoTracking()
                .Where(a => orgIds.Contains(a.OrganizationId)
                         && a.IsSearchable
                         && a.SearchVisibility == OrganizationAddressVisibility.Public
                         && a.Latitude != null && a.Longitude != null)
                .Select(a => new { a.OrganizationId, a.Latitude, a.Longitude })
                .ToListAsync(ct);
            foreach (var address in addresses)
            {
                var miles = HaversineMiles(viewerLat, viewerLon,
                    (double)address.Latitude!, (double)address.Longitude!);
                if (!distances.TryGetValue(address.OrganizationId, out var best) || miles < best)
                    distances[address.OrganizationId] = miles;
            }
        }

        // Located groups nearest-first; unlocated after them in random rotation — being
        // unlocatable must not mean unseen, it just means unranked.
        var random = new Random();
        var served = ads
            .Select(a => new { Ad = a, Distance = distances.TryGetValue(a.OrgId, out var d) ? (double?)Math.Round(d, 1) : null })
            .OrderBy(x => x.Distance is null)
            .ThenBy(x => x.Distance)
            .ThenBy(_ => random.Next())
            .Take(take)
            .ToList();

        // Impressions, batched: one UPDATE for the whole page's worth of cards. A count of
        // serves, deliberately — eyeballs are not something this endpoint can honestly claim.
        var servedIds = served.Select(s => s.Ad.Id).ToList();
        await BumpCountersAsync(db, servedIds, impressions: true, ct);

        return Ok(served.Select(s => new PromotedGroupCard(
            s.Ad.Id, s.Ad.Headline, s.Ad.Body, s.Ad.OrgName, s.Ad.OrgUrlName,
            s.Ad.TargetKind, s.Ad.HasImage, s.Distance)).ToList());
    }

    /// <summary>
    /// Counts one click and answers where the card leads (item 186 F8). The WEBSITE's /go route
    /// calls this then issues the redirect itself — the API cannot 302 to the website without
    /// knowing the website's origin, and the two are separate hosts in every deployment shape.
    /// The closed set of targets is enforced by the caller rendering ONLY from TargetKind.
    /// </summary>
    [HttpPost("{adId:guid}/click")]
    public async Task<ActionResult<PromotedClickTarget>> Click(Guid adId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ad = await db.OrganizationAds.AsNoTracking()
            .Where(a => a.Id == adId && a.Status == OrganizationAdStatus.Approved)
            .Select(a => new { a.TargetKind, a.Organization.UrlName })
            .FirstOrDefaultAsync(ct);
        if (ad is null) return NotFound();

        await BumpCountersAsync(db, [adId], impressions: false, ct);
        return Ok(new PromotedClickTarget(ad.TargetKind, ad.UrlName));
    }

    /// <summary>Batched counter bump. ExecuteUpdate where the provider supports it (SQL Server);
    /// tracked fallback for the in-memory provider the tests run on.</summary>
    private static async Task BumpCountersAsync(
        BenDataContext db, IReadOnlyList<Guid> adIds, bool impressions, CancellationToken ct)
    {
        if (adIds.Count == 0) return;
        if (db.Database.IsRelational())
        {
            if (impressions)
                await db.OrganizationAds.Where(a => adIds.Contains(a.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Impressions, a => a.Impressions + 1), ct);
            else
                await db.OrganizationAds.Where(a => adIds.Contains(a.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.Clicks, a => a.Clicks + 1), ct);
            return;
        }
        var rows = await db.OrganizationAds.Where(a => adIds.Contains(a.Id)).ToListAsync(ct);
        foreach (var row in rows)
        {
            if (impressions) row.Impressions++; else row.Clicks++;
        }
        await db.SaveChangesAsync(ct);
    }

    private static double HaversineMiles(double lat1, double lon1, double lat2, double lon2)
    {
        const double earthRadiusMiles = 3958.8;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return earthRadiusMiles * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    /// <summary>The ad's image, served only while the ad is approved — never through the
    /// general file routes, whose audience rules know nothing about ad review.</summary>
    [HttpGet("{adId:guid}/image")]
    public async Task<IActionResult> Image(Guid adId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var ad = await db.OrganizationAds.AsNoTracking()
            .Include(a => a.ImageUploadFile)
            .FirstOrDefaultAsync(a => a.Id == adId, ct);
        if (ad is null || ad.Status != OrganizationAdStatus.Approved
            || ad.ImageUploadFile is null)
            return NotFound();

        var file = ad.ImageUploadFile;
        if (!string.IsNullOrEmpty(file.StoragePath) && _storage.Exists(file.StoragePath))
            return File(await _storage.OpenReadAsync(file.StoragePath, ct), file.ContentType);
        if (file.FileData is { Length: > 0 })
            return File(file.FileData, file.ContentType);
        return NotFound();
    }
}
