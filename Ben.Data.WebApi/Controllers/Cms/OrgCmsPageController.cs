using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Ben.Data.WebApi.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>CRUD for organization CMS pages including hierarchy management.</summary>
[Route("api/organizations/{orgId:guid}/pages")]
public sealed class OrgCmsPageController : OrgCmsControllerBase
{
    private readonly IAuditLogService _auditLog;
    private readonly ICmsMarkupSanitizer _sanitizer;

    public OrgCmsPageController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        IAuditLogService auditLog,
        ICmsMarkupSanitizer sanitizer)
        : base(dbFactory, mapper, security)
    {
        _auditLog = auditLog;
        _sanitizer = sanitizer;
    }

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

        // Drafts are OrganizationPage rows too, so they have to be excluded here or the page list
        // shows a phantom duplicate of everything anyone is part-way through editing.
        var pages = await db.OrganizationPages
            .AsNoTracking()
            .Where(p => p.OrganizationId == orgId && p.DraftOfOrganizationPageId == null)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.PageTitle)
            .ToListAsync(ct);

        var isSuperAdmin = User.IsInRole(Data.Common.Constants.RoleNames.SuperAdmin);
        bool canEdit, canDelete;
        if (isSuperAdmin)
        {
            canEdit = canDelete = true;
        }
        else
        {
            // userId/orgId/table are loop-invariant — these were previously re-checked once per page.
            canEdit   = await Security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Update, ct);
            canDelete = await Security.HasAccessAsync(userId.Value, orgId, OrganizationSecurityTable.OrganizationPage, OrganizationSecurityAction.Delete, ct);
        }

        var pageIds = pages.Select(p => p.Id).ToList();
        var sectionCounts = await db.CmsSections
            .Where(s => pageIds.Contains(s.OrganizationPageId))
            .GroupBy(s => s.OrganizationPageId)
            .Select(g => new { PageId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PageId, x => x.Count, ct);

        var result = pages.Select(p => new CmsPageListItemResponse(
            p.Id, p.OrganizationId, p.ParentPageId, p.PageTitle, p.UrlName, p.IsHome, p.IsPublished, p.IsPublic,
            p.SortOrder, sectionCounts.GetValueOrDefault(p.Id), canEdit, canDelete, p.DateCreated,
            IsUnreachable: Ben.Data.Common.CmsReservedSlugs.IsReserved(p.UrlName))).ToList();

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

        var urlName = Ben.Data.Common.SlugText.NormalizeOrEmpty(request.UrlName);

        // Refused before it can be saved. A page at a routed word saves happily and is then
        // unreachable for ever, with nothing to tell the person who made it — see CmsReservedSlugs.
        if (Ben.Data.Common.CmsReservedSlugs.RefusalFor(urlName) is string reserved)
            return BadRequest(reserved);

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
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(OrganizationPage), page.Id, page, userId.Value, AppSources.WebApi));

        var sections = request.FromTemplateId is Guid templateId
            ? await ApplyPageTemplateAsync(db, orgId, page.Id, templateId, userId.Value, ct)
            : [];

        return CreatedAtAction(nameof(GetById), new { orgId, pageId = page.Id },
            new CmsPageDetailResponse(page.Id, page.OrganizationId, page.ParentPageId,
                page.PageTitle, page.UrlName, page.PageHtml, page.IsHome,
                page.IsPublished, page.IsPublic, page.SortOrder,
                page.DateCreated, page.DateUpdated, sections));
    }

    /// <summary>
    /// Copies a page template's sections onto a newly created page.
    /// </summary>
    /// <remarks>
    /// <para><b>Server-side, so the copy happens once and in one place.</b> Doing it in the browser
    /// would mean the sanitizer never saw the markup on its way in — a template's content was
    /// cleaned when it was saved, but "cleaned then" is not the same as "clean now", and a rule
    /// enforced only by the client is not a rule.</para>
    ///
    /// <para><b>Scoped to this organization, and to page templates.</b> Another group's template and
    /// a section-scoped one are both ignored, which leaves a bare page — the thing the caller
    /// actually asked for — rather than failing the create over a template that has since been
    /// deleted or belongs to somebody else.</para>
    ///
    /// <para>Content that will not parse yields no sections rather than a broken page. The template
    /// was sanitized on save, so this is a corrupt-data path, not a hostile one.</para>
    /// </remarks>
    private async Task<List<Ben.Service.Models.Entities.CmsSectionRecord>> ApplyPageTemplateAsync(
        BenDataContext db, Guid orgId, Guid pageId, Guid templateId, Guid userId, CancellationToken ct)
    {
        var template = await db.OrganizationCmsTemplates.AsNoTracking().FirstOrDefaultAsync(
            t => t.Id == templateId
              && t.OrganizationId == orgId
              && t.Scope == CmsTemplateScope.Page, ct);

        if (template is null) return [];

        List<CmsTemplateSectionRecord>? templateSections;
        try
        {
            templateSections = JsonSerializer.Deserialize<List<CmsTemplateSectionRecord>>(template.ContentJson);
        }
        catch (JsonException)
        {
            return [];
        }

        if (templateSections is null || templateSections.Count == 0) return [];

        var created = new List<CmsSection>();
        var order = 0;

        foreach (var s in templateSections.OrderBy(s => s.SortOrder))
        {
            var section = new CmsSection
            {
                Id                 = Guid.NewGuid(),
                OrganizationPageId = pageId,
                SectionType        = s.SectionType,
                Title              = s.Title,
                // Sanitized on the way in, not trusted from storage.
                ContentJson        = _sanitizer.SanitizeContentJson(s.ContentJson) ?? "{}",
                SortOrder          = order++,
                IsActive           = true,
                DateCreated        = DateTime.UtcNow,
                CreatedByAppUserId = userId,
            };
            created.Add(section);
            db.CmsSections.Add(section);
        }

        await db.SaveChangesAsync(ct);

        return Mapper.Map<IReadOnlyList<Ben.Service.Models.Entities.CmsSectionRecord>>(created).ToList();
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

        var before = await db.OrganizationPages.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct);
        if (before is null) return NotFound();

        var page = await db.OrganizationPages
            .FirstOrDefaultAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct);

        var urlName = Ben.Data.Common.SlugText.NormalizeOrEmpty(request.UrlName);

        // Checked on the way in as well as on create: a page renamed onto a routed word would
        // vanish just as completely as one created there.
        if (Ben.Data.Common.CmsReservedSlugs.RefusalFor(urlName) is string renamedOnto)
            return BadRequest(renamedOnto);

        if (page!.UrlName != urlName && await db.OrganizationPages.AnyAsync(
                p => p.OrganizationId == orgId && p.UrlName == urlName, ct))
            return BadRequest($"UrlName '{urlName}' is already in use for this organization.");

        // Prevent a page from becoming its own ancestor
        if (request.ParentPageId == pageId)
            return BadRequest("A page cannot be its own parent.");

        page!.PageTitle          = request.PageTitle.Trim();
        page.UrlName            = urlName;
        page.PageHtml           = request.PageHtml ?? string.Empty;
        page.IsPublished        = request.IsPublished;
        page.IsPublic           = request.IsPublic;
        page.ParentPageId       = request.ParentPageId;
        page.SortOrder          = request.SortOrder;
        page.DateUpdated        = DateTime.UtcNow;
        page.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(OrganizationPage), pageId, before, page!, userId.Value, AppSources.WebApi));

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
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(OrganizationPage), pageId, page, userId.Value, AppSources.WebApi));
        return NoContent();
    }
}

// ── Request / response records ────────────────────────────────────────────────

/// <param name="PageTitle">Heading and navigation label for the page.</param>
/// <param name="UrlName">The readable address segment under /o/{org}/.</param>
/// <param name="PageHtml">Legacy whole-page markup; sections are the current model.</param>
/// <param name="IsPublic">Whether visitors may see it once published.</param>
/// <param name="ParentPageId">Optional parent, for nested navigation.</param>
/// <param name="SortOrder">Position among its siblings.</param>
/// <param name="FromTemplateId">
/// Optional page template to start from. Its sections are copied onto the new page — copied, not
/// referenced, so tidying the template later leaves this page alone. A template belonging to
/// another organization, or a section-scoped one, is ignored rather than refused: the page is what
/// was asked for, and failing the whole create over a stale template id would be the worse answer.
/// </param>
public sealed record CreateCmsPageRequest(
    string PageTitle,
    string UrlName,
    string? PageHtml,
    bool IsPublic,
    Guid? ParentPageId,
    int SortOrder,
    Guid? FromTemplateId = null);

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
    DateTime DateCreated,
    // True when this page's address is one the site itself routes, so the page cannot be opened.
    // Only possible for a page saved before the reserved-word check existed. Carried so the editor
    // can say so: the page looks perfectly fine in the list, and the only symptom is that following
    // its link lands somewhere else. Computed server-side and rendered as given, like every other
    // verdict here.
    bool IsUnreachable = false);

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
