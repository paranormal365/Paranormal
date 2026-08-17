using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Public;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>
/// A CMS page as a visitor would see it, before it is published.
/// </summary>
/// <remarks>
/// <para>Ben asked to see a page before making it live. Until now the only way to know what a page
/// looked like was to publish it and look — which is a strange thing to have to do to find out
/// whether you want to.</para>
///
/// <para>This returns exactly the shape the public endpoint returns, so the same renderer draws it
/// and a preview cannot quietly diverge from the real thing. The <b>only</b> difference is that
/// <c>IsPublished</c> and <c>IsPublic</c> are not required — everything else, including which
/// sections are active and in what order, is the public rule.</para>
///
/// <para>It previews the <b>saved</b> page, not unsaved edits in the browser. Previewing unsaved
/// work needs somewhere to put a draft, which is a storage decision rather than a rendering one and
/// is tracked separately in item #80. Most of the value is here, and it needed no schema at all.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/cms/pages/{pageId:guid}/preview")]
public sealed class CmsPagePreviewController : OrgCmsControllerBase
{
    public CmsPagePreviewController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security)
        : base(dbFactory, mapper, security) { }

    [HttpGet]
    public async Task<ActionResult<OrgPublicPageResponse>> GetPreview(
        Guid orgId, Guid pageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        // Read on sections is the right gate: it is what lets somebody open the editor, and a
        // preview shows strictly less than the editor already does.
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection,
                                        OrganizationSecurityAction.Read, ct))
            return NotFound();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var org = await db.Organizations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orgId, ct);
        if (org is null) return NotFound();

        var page = await db.OrganizationPages.AsNoTracking()
            .Include(p => p.CmsSections.Where(s => s.IsActive).OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct);
        if (page is null) return NotFound();

        var sections = page.CmsSections
            .Select(s => new OrgPublicSectionItem(s.Id, s.SectionType, s.Title, s.ContentJson, s.SortOrder))
            .ToList();

        var logos = await db.OrganizationLogos.AsNoTracking()
            .Where(l => l.OrganizationId == orgId && l.IsActive)
            .OrderBy(l => l.SortOrder)
            .Select(l => new OrgPublicLogoItem(l.Id, l.UploadFileId, l.AltText, l.SortOrder))
            .ToListAsync(ct);

        // Navigation shows what a visitor would see — the published pages — so a previewer can tell
        // whether this page will appear in the menu once it goes live, rather than seeing a menu
        // that only exists for them.
        var navPages = await db.OrganizationPages.AsNoTracking()
            .Where(p => p.OrganizationId == orgId && p.IsPublished && p.IsPublic)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.PageTitle)
            .Select(p => new OrgPublicNavItem(p.Id, p.PageTitle, p.UrlName, p.ParentPageId, p.SortOrder))
            .ToListAsync(ct);

        return Ok(new OrgPublicPageResponse(
            org.Id, org.Name, org.UrlName,
            logos,
            new OrgPublicPageItem(page.Id, page.PageTitle, page.UrlName, page.IsHome, sections),
            navPages));
    }
}
