using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Which sections of the site are switched on. Anonymous, because the pages that need the answer
/// include ones nobody has signed in to yet.
/// </summary>
/// <remarks>
/// <para>Narrow in the same way <see cref="PublicSiteContactController"/> is narrow: it walks
/// <see cref="SiteSettingKeys.FeatureDefaults"/> and returns those keys only. A setting that is
/// not a declared feature cannot appear here however the settings table changes, so the "adding a
/// setting can never accidentally publish it" property survives.</para>
///
/// <para>Publishing which features are on is not a disclosure worth guarding: every one of them
/// is visible from the navigation of any signed-in account, and several from the anonymous home
/// page. What it buys is a website that can hide a section's links AND refuse its URLs from the
/// first render, rather than discovering the answer after the page has already drawn.</para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("api/public/site-features")]
public sealed class PublicSiteFeaturesController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicSiteFeaturesController(IDbContextFactory<BenDataContext> db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<SiteFeaturesInfo>> Get(CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        // One query, then resolve in memory. The alternative — a round trip per flag — is ten
        // queries on a path the website polls, for a table with ten rows in it.
        var stored = await db.SiteSettings
            .AsNoTracking()
            .Where(s => s.Value != null)
            .ToDictionaryAsync(s => s.Key, s => s.Value!, ct);

        var features = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (key, defaultWhenUnset) in SiteSettingKeys.FeatureDefaults)
        {
            features[key] = stored.TryGetValue(key, out var raw) && bool.TryParse(raw, out var on)
                ? on
                : defaultWhenUnset;
        }

        // The one non-feature value published here, on purpose: a site-wide announcement is only
        // useful if the people it warns — including anonymous visitors — can read it.
        stored.TryGetValue(SiteSettingKeys.SiteAnnouncement, out var announcement);

        // Same reasoning: a policy the website must apply while rendering, not a secret. Unset
        // reads as on, matching the enforcement in OrganizationMembershipController.
        var allowSelfRegistration =
            !stored.TryGetValue(SiteSettingKeys.AllowOrganizationSelfRegistration, out var rawAllow)
            || !bool.TryParse(rawAllow, out var parsedAllow)
            || parsedAllow;

        return Ok(new SiteFeaturesInfo(features, announcement, allowSelfRegistration));
    }
}
