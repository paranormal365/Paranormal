using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>CRUD for organization CMS pages including hierarchy management.</summary>
[Route("api/organizations/{orgId:guid}/pages")]
public sealed class OrgCmsPageController : OrgCmsControllerBase
{
    public OrgCmsPageController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security)
        : base(dbFactory, mapper, security) { }

    // ── GET /api/organizations/{orgId}/pages ─────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CmsPageListItemResponse>>> GetAll(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var pages = await db.OrganizationPages
            .AsNoTracking()
            .Where(p => p.OrganizationId == orgId)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.PageTitle)
            .ToListAsync(ct);

        var isSuperAdmin = User.IsInRole(Data.Common.Constants.RoleNames.SuperAdmin);
        var result = new List<CmsPageListItemResponse>(pages.Count);

        foreach (var p in pages)
        {
            bool canEdit, canDelete;
            if (isSuperAdmin)
            {
                canEdit = canDelete = true;
            }
            else
            {
                canEdit   = await Security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Update, ct);
                canDelete = await Security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Delete, ct);
            }
            var sectionCount = await db.CmsSections.CountAsync(s => s.OrganizationPageId == p.Id, ct);
            result.Add(new CmsPageListItemResponse(p.Id, p.OrganizationId, p.ParentPageId, p.PageTitle, p.UrlName, p.IsHome, p.IsPublished, p.IsPublic, p.SortOrder, sectionCount, canEdit, canDelete, p.DateCreated));
        }

        return Ok(result);
    }

    // ── GET /api/organizations/{orgId}/pages/{pageId} ────────────────────────

    [HttpGet("{pageId:guid}")]
    public async Task<ActionResult<CmsPageDetailResponse>> GetById(
        Guid orgId, Guid pageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var page = await db.OrganizationPages
            .AsNoTracking()
            .Include(p => p.CmsSections.OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct);

        if (page is null) return NotFound();

        return Ok(new CmsPageDetailResponse(
            page.Id, page.OrganizationId, page.ParentPageId,
            page.PageTitle, page.UrlName, page.PageHtml,
            page.IsHome, page.IsPublished, page.IsPublic, page.SortOrder,
            page.DateCreated, page.DateUpdated,
            Mapper.Map<IReadOnlyList<CmsSectionRecord>>(page.CmsSections.ToList())));
    }

    // ── POST /api/organizations/{orgId}/pages ────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<CmsPageDetailResponse>> Create(
        Guid orgId, [FromBody] CreateCmsPageRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Create, ct))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.PageTitle)) return BadRequest("PageTitle is required.");
        if (string.IsNullOrWhiteSpace(request.UrlName))   return BadRequest("UrlName is required.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var urlName = request.UrlName.Trim().ToLowerInvariant();
        if (await db.OrganizationPages.AnyAsync(p => p.OrganizationId == orgId && p.UrlName == urlName, ct))
            return BadRequest($"UrlName '{urlName}' is already in use for this organization.");

        var page = new OrganizationPage
        {
            OrganizationId     = orgId,
            PageTitle          = request.PageTitle.Trim(),
            UrlName            = urlName,
            PageHtml           = request.PageHtml ?? string.Empty,
            IsPublished        = false,
            IsPublic           = request.IsPublic,
            ParentPageId       = request.ParentPageId,
            SortOrder          = request.SortOrder,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value
        };

        db.OrganizationPages.Add(page);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetById), new { orgId, pageId = page.Id },
            new CmsPageDetailResponse(page.Id, page.OrganizationId, page.ParentPageId,
                page.PageTitle, page.UrlName, page.PageHtml, page.IsHome,
                page.IsPublished, page.IsPublic, page.SortOrder,
                page.DateCreated, page.DateUpdated, []));
    }

    // ── PUT /api/organizations/{orgId}/pages/{pageId} ────────────────────────

    [HttpPut("{pageId:guid}")]
    public async Task<ActionResult<CmsPageDetailResponse>> Update(
        Guid orgId, Guid pageId, [FromBody] UpdateCmsPageRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Update, ct))
            return Forbid();

        if (string.IsNullOrWhiteSpace(request.PageTitle)) return BadRequest("PageTitle is required.");
        if (string.IsNullOrWhiteSpace(request.UrlName))   return BadRequest("UrlName is required.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var page = await db.OrganizationPages
            .FirstOrDefaultAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct);
        if (page is null) return NotFound();

        var urlName = request.UrlName.Trim().ToLowerInvariant();
        if (page.UrlName != urlName && await db.OrganizationPages.AnyAsync(
                p => p.OrganizationId == orgId && p.UrlName == urlName, ct))
            return BadRequest($"UrlName '{urlName}' is already in use for this organization.");

        // Prevent a page from becoming its own ancestor
        if (request.ParentPageId == pageId)
            return BadRequest("A page cannot be its own parent.");

        page.PageTitle          = request.PageTitle.Trim();
        page.UrlName            = urlName;
        page.PageHtml           = request.PageHtml ?? string.Empty;
        page.IsPublished        = request.IsPublished;
        page.IsPublic           = request.IsPublic;
        page.ParentPageId       = request.ParentPageId;
        page.SortOrder          = request.SortOrder;
        page.DateUpdated        = DateTime.UtcNow;
        page.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);

        var sectionCount = await db.CmsSections.CountAsync(s => s.OrganizationPageId == pageId, ct);
        return Ok(new CmsPageDetailResponse(page.Id, page.OrganizationId, page.ParentPageId,
            page.PageTitle, page.UrlName, page.PageHtml, page.IsHome,
            page.IsPublished, page.IsPublic, page.SortOrder,
            page.DateCreated, page.DateUpdated, []));
    }

    // ── DELETE /api/organizations/{orgId}/pages/{pageId} ─────────────────────

    [HttpDelete("{pageId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid pageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Delete, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var page = await db.OrganizationPages
            .Include(p => p.CmsSections)
            .Include(p => p.PagePermissions)
            .FirstOrDefaultAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct);
        if (page is null) return NotFound();

        // Re-parent any children to this page's parent (avoid orphaning)
        var children = await db.OrganizationPages.Where(p => p.ParentPageId == pageId).ToListAsync(ct);
        foreach (var child in children) child.ParentPageId = page.ParentPageId;

        db.OrganizationPages.Remove(page);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

// ── Request / response records ────────────────────────────────────────────────

public sealed record CreateCmsPageRequest(
    string PageTitle,
    string UrlName,
    string? PageHtml,
    bool IsPublic,
    Guid? ParentPageId,
    int SortOrder);

public sealed record UpdateCmsPageRequest(
    string PageTitle,
    string UrlName,
    string? PageHtml,
    bool IsPublished,
    bool IsPublic,
    Guid? ParentPageId,
    int SortOrder);

public sealed record CmsPageListItemResponse(
    Guid Id,
    Guid OrganizationId,
    Guid? ParentPageId,
    string PageTitle,
    string UrlName,
    bool IsHome,
    bool IsPublished,
    bool IsPublic,
    int SortOrder,
    int SectionCount,
    bool CanEdit,
    bool CanDelete,
    DateTime DateCreated);

public sealed record CmsPageDetailResponse(
    Guid Id,
    Guid OrganizationId,
    Guid? ParentPageId,
    string PageTitle,
    string UrlName,
    string PageHtml,
    bool IsHome,
    bool IsPublished,
    bool IsPublic,
    int SortOrder,
    DateTime DateCreated,
    DateTime? DateUpdated,
    IReadOnlyList<Ben.Service.Models.Entities.CmsSectionRecord> Sections);
