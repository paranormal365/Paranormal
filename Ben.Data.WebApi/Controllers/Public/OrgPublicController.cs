using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.Source.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Unauthenticated endpoints for publicly-viewable organization pages.
/// Accessible at /o/{urlName} in the web app.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/public/organizations")]
public sealed class OrgPublicController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public OrgPublicController(IDbContextFactory<BenDataContext> db) => _db = db;

    // ── GET /api/public/organizations/{urlName} ───────────────────────────────

    /// <summary>Returns the org header and its published public home page.</summary>
    [HttpGet("{urlName}")]
    public async Task<ActionResult<OrgPublicHomeResponse>> GetHome(
        string urlName, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        // Resolves a retired address too, so a link printed before a rename still opens. The
        // response carries the current UrlName, which is how the page knows to correct the address.
        var (org, _) = await OrganizationUrlNames.ResolveAsync(db, urlName, ct);

        if (org is null) return NotFound();

        var logos    = await BuildLogosAsync(db, org.Id, ct);
        var homePage = await BuildPageAsync(db, org.Id, isHome: true, pageSlug: null, ct);
        var navPages = await BuildNavPagesAsync(db, org.Id, homePageId: homePage?.Id, ct);

        return Ok(new OrgPublicHomeResponse(
            org.Id, org.Name, org.UrlName,
            logos, homePage, navPages,
            org.PublicPhone, org.PublicEmail, org.PublicWebsite));
    }

    // ── GET /api/public/organizations/{urlName}/pages/{pageSlug} ─────────────

    /// <summary>Returns a specific published public CMS page for the org.</summary>
    [HttpGet("{urlName}/pages/{pageSlug}")]
    public async Task<ActionResult<OrgPublicPageResponse>> GetPage(
        string urlName, string pageSlug, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        // Resolves a retired address too, so a link printed before a rename still opens. The
        // response carries the current UrlName, which is how the page knows to correct the address.
        var (org, _) = await OrganizationUrlNames.ResolveAsync(db, urlName, ct);

        if (org is null) return NotFound();

        var page = await BuildPageAsync(db, org.Id, isHome: false, pageSlug: Ben.Data.Common.SlugText.NormalizeOrEmpty(pageSlug), ct);
        if (page is null) return NotFound();

        var logos    = await BuildLogosAsync(db, org.Id, ct);
        var navPages = await BuildNavPagesAsync(db, org.Id, homePageId: null, ct);

        return Ok(new OrgPublicPageResponse(
            org.Id, org.Name, org.UrlName,
            logos, page, navPages));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<IReadOnlyList<OrgPublicLogoItem>> BuildLogosAsync(
        BenDataContext db, Guid orgId, CancellationToken ct)
    {
        var logos = await db.OrganizationLogos.AsNoTracking()
            .Where(l => l.OrganizationId == orgId && l.IsActive)
            .OrderBy(l => l.SortOrder)
            .ToListAsync(ct);

        return logos.Select(l => new OrgPublicLogoItem(l.Id, l.UploadFileId, l.AltText, l.SortOrder))
                    .ToList();
    }

    private static async Task<OrgPublicPageItem?> BuildPageAsync(
        BenDataContext db, Guid orgId, bool isHome, string? pageSlug, CancellationToken ct)
    {
        IQueryable<OrganizationPage> query = db.OrganizationPages.AsNoTracking()
            .Where(p => p.OrganizationId == orgId
                     && p.IsPublished
                     && p.IsPublic);

        if (isHome)
            query = query.Where(p => p.IsHome);
        else if (pageSlug is not null)
            query = query.Where(p => p.UrlName == pageSlug);
        else
            return null;

        var page = await query
            .Include(p => p.CmsSections.Where(s => s.IsActive).OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(ct);

        if (page is null) return null;

        var sections = page.CmsSections
            .Select(s => new OrgPublicSectionItem(s.Id, s.SectionType, s.Title, s.ContentJson, s.SortOrder))
            .ToList();

        return new OrgPublicPageItem(page.Id, page.PageTitle, page.UrlName, page.IsHome, sections);
    }

    private static async Task<IReadOnlyList<OrgPublicNavItem>> BuildNavPagesAsync(
        BenDataContext db, Guid orgId, Guid? homePageId, CancellationToken ct)
    {
        var pages = await db.OrganizationPages.AsNoTracking()
            .Where(p => p.OrganizationId == orgId
                     && p.IsPublished
                     && p.IsPublic
                     && (homePageId == null || p.Id != homePageId))
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.PageTitle)
            .ToListAsync(ct);

        return pages.Select(p => new OrgPublicNavItem(p.Id, p.PageTitle, p.UrlName, p.ParentPageId, p.SortOrder))
                    .ToList();
    }
}

// ── Response records ─────────────────────────────────────────────────────────

public sealed record OrgPublicHomeResponse(
    Guid OrgId,
    string OrgName,
    string OrgUrlName,
    IReadOnlyList<OrgPublicLogoItem> Logos,
    OrgPublicPageItem? HomePage,
    IReadOnlyList<OrgPublicNavItem> NavPages,
    string? PublicPhone = null,
    string? PublicEmail = null,
    string? PublicWebsite = null);

public sealed record OrgPublicPageResponse(
    Guid OrgId,
    string OrgName,
    string OrgUrlName,
    IReadOnlyList<OrgPublicLogoItem> Logos,
    OrgPublicPageItem Page,
    IReadOnlyList<OrgPublicNavItem> NavPages);

public sealed record OrgPublicLogoItem(
    Guid LogoId,
    Guid UploadFileId,
    string? AltText,
    int SortOrder);

public sealed record OrgPublicPageItem(
    Guid Id,
    string PageTitle,
    string UrlName,
    bool IsHome,
    IReadOnlyList<OrgPublicSectionItem> Sections);

public sealed record OrgPublicSectionItem(
    Guid Id,
    CmsSectionType SectionType,
    string? Title,
    string ContentJson,
    int SortOrder);

public sealed record OrgPublicNavItem(
    Guid Id,
    string PageTitle,
    string UrlName,
    Guid? ParentPageId,
    int SortOrder);
