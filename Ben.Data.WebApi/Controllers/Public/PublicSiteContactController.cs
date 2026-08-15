using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// The site's own contact details, for the public contact page.
/// </summary>
/// <remarks>
/// A narrow endpoint rather than opening the settings table: it returns four named fields and
/// nothing else, so adding a setting can never accidentally publish it. The admin settings endpoint
/// stays SuperAdmin-only.
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/site-contact")]
public sealed class PublicSiteContactController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicSiteContactController(IDbContextFactory<BenDataContext> db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<SiteContactInfo>> Get(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        return Ok(new SiteContactInfo(
            Email: await SiteSettingsService.GetAsync(db, SiteSettingKeys.PublicContactEmail, ct),
            PostalAddress: await SiteSettingsService.GetAsync(db, SiteSettingKeys.ContactPostalAddress, ct),
            Phone: await SiteSettingsService.GetAsync(db, SiteSettingKeys.ContactPhone, ct),
            Hours: await SiteSettingsService.GetAsync(db, SiteSettingKeys.ContactHours, ct)));
    }
}
