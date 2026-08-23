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

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PromotedGroupCard>>> Get(
        [FromQuery] int take = 3, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 10);
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var cards = await db.OrganizationAds.AsNoTracking()
            .Where(a => a.Status == OrganizationAdStatus.Approved)
            .OrderBy(_ => Guid.NewGuid())
            .Take(take)
            .Select(a => new PromotedGroupCard(
                a.Id, a.Headline, a.Body,
                a.Organization.Name, a.Organization.UrlName,
                a.TargetKind, a.ImageUploadFileId != null))
            .ToListAsync(ct);
        return Ok(cards);
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
