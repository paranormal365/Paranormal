using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>CRUD and reorder for sections within an organization CMS page.</summary>
[Route("api/organizations/{orgId:guid}/pages/{pageId:guid}/sections")]
public sealed class CmsSectionController : OrgCmsControllerBase
{
    private readonly IAuditLogService _auditLog;
    private readonly ICmsMarkupSanitizer _sanitizer;

    public CmsSectionController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security,
        IAuditLogService auditLog,
        ICmsMarkupSanitizer sanitizer)
        : base(dbFactory, mapper, security)
    {
        _auditLog  = auditLog;
        _sanitizer = sanitizer;
    }

    // ── GET all sections for a page ──────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CmsSectionRecord>>> GetAll(
        Guid orgId, Guid pageId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationPages.AnyAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct))
            return NotFound();

        var sections = await db.CmsSections
            .AsNoTracking()
            .Where(s => s.OrganizationPageId == pageId)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

        return Ok(Mapper.Map<IEnumerable<CmsSectionRecord>>(sections));
    }

    // ── POST — create section ────────────────────────────────────────────────

    [HttpPost]
    public async Task<ActionResult<CmsSectionRecord>> Create(
        Guid orgId, Guid pageId, [FromBody] CreateCmsSectionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection, OrganizationSecurityAction.Create, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationPages.AnyAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct))
            return NotFound();

        var section = new CmsSection
        {
            OrganizationPageId = pageId,
            SectionType        = request.SectionType,
            Title              = request.Title?.Trim(),
            ContentJson        = _sanitizer.SanitizeContentJson(request.ContentJson),
            SortOrder          = request.SortOrder,
            IsActive           = request.IsActive,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value
        };

        db.CmsSections.Add(section);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(CmsSection), section.Id, section, userId.Value, AppSources.WebApi));

        return CreatedAtAction(nameof(GetAll), new { orgId, pageId },
            Mapper.Map<CmsSectionRecord>(section));
    }

    // ── PUT — update section content ─────────────────────────────────────────

    [HttpPut("{sectionId:guid}")]
    public async Task<ActionResult<CmsSectionRecord>> Update(
        Guid orgId, Guid pageId, Guid sectionId,
        [FromBody] UpdateCmsSectionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        // The page must be this group's, as Create, Reorder and the reads already asked. Update and Delete
        // checked only that the section was on the page, so an editor in one group could rewrite another
        // group's public page (site audit, 10/09/2026).
        if (!await db.OrganizationPages.AnyAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct))
            return NotFound();
        var before = await db.CmsSections.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sectionId && s.OrganizationPageId == pageId, ct);
        if (before is null) return NotFound();
        var section = await db.CmsSections
            .FirstOrDefaultAsync(s => s.Id == sectionId && s.OrganizationPageId == pageId, ct);

        section!.Title              = request.Title?.Trim();
        section.ContentJson        = _sanitizer.SanitizeContentJson(request.ContentJson);
        section.IsActive           = request.IsActive;
        section.DateUpdated        = DateTime.UtcNow;
        section.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(CmsSection), sectionId, before, section!, userId.Value, AppSources.WebApi));
        return Ok(Mapper.Map<CmsSectionRecord>(section));
    }

    // ── PUT reorder — apply new sort order ───────────────────────────────────

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder(
        Guid orgId, Guid pageId, [FromBody] ReorderCmsSectionsRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection, OrganizationSecurityAction.Update, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        // The page has to be this group's. Without this, permission to reorder one group's sections
        // reordered any page's whose id somebody knew (found 10/09/2026, adding the Ordering window).
        if (!await db.OrganizationPages.AnyAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct))
            return NotFound();

        var sections = await db.CmsSections
            .Where(s => s.OrganizationPageId == pageId)
            .ToListAsync(ct);
        var before = new { Order = string.Join(", ", sections.OrderBy(s => s.SortOrder).Select(s => s.Id)) };

        for (var i = 0; i < request.OrderedSectionIds.Count; i++)
        {
            var section = sections.FirstOrDefault(s => s.Id == request.OrderedSectionIds[i]);
            if (section is not null) section.SortOrder = i + 1;
        }

        await db.SaveChangesAsync(ct);
        // Before and after are the same shape: the audit compares them field by field and throws on two
        // different types. It used to be given `new { }` and the request, so every reorder was saved and then
        // answered 500 — which the page editor's arrows ignored, and the Ordering window would not (10/09/2026).
        var after = new { Order = string.Join(", ", sections.OrderBy(s => s.SortOrder).Select(s => s.Id)) };
        _ = TryAuditAsync(_auditLog.LogUpdateAsync("CmsSectionReorder", pageId, before, after, userId.Value, AppSources.WebApi));
        return NoContent();
    }

    // ── POST preview — a section as a visitor would see it, before it is saved ──

    /// <summary>
    /// What a section being written would look like on the public page, without saving it.
    /// </summary>
    /// <remarks>
    /// <para>Ben asked for a Preview button beside Cancel (10/09/2026): somebody filling in a section
    /// should see it the way a visitor will before deciding to keep it. The page preview only shows
    /// what is saved, so this takes the section as it stands in the editor.</para>
    ///
    /// <para>Cleaned and resolved exactly as the public page does it: markup through the sanitizer,
    /// an embed's references through <see cref="CmsEmbed"/> with its redaction rules. A preview that
    /// showed raw ids, or markup the save would strip, would promise a page that never appears.</para>
    ///
    /// <para>Nothing is written. Read access is enough, the same gate the page preview uses.</para>
    /// </remarks>
    [HttpPost("preview")]
    public async Task<ActionResult<Ben.Data.WebApi.Controllers.Public.OrgPublicSectionItem>> Preview(
        Guid orgId, Guid pageId, [FromBody] CreateCmsSectionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection, OrganizationSecurityAction.Read, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationPages.AnyAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct))
            return NotFound();

        var clean = _sanitizer.SanitizeContentJson(request.ContentJson);
        var content = CmsEmbed.IsEmbed(request.SectionType)
            ? await CmsEmbed.ResolveAsync(db, orgId, request.SectionType, clean, ct)
            : clean;

        return Ok(new Ben.Data.WebApi.Controllers.Public.OrgPublicSectionItem(
            Guid.Empty, request.SectionType, request.Title?.Trim(), content, request.SortOrder));
    }

    // ── DELETE section ───────────────────────────────────────────────────────

    [HttpDelete("{sectionId:guid}")]
    public async Task<IActionResult> Delete(
        Guid orgId, Guid pageId, Guid sectionId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection, OrganizationSecurityAction.Delete, ct))
            return Forbid();

        await using var db = await DbFactory.CreateDbContextAsync(ct);
        if (!await db.OrganizationPages.AnyAsync(p => p.Id == pageId && p.OrganizationId == orgId, ct))
            return NotFound();
        var section = await db.CmsSections
            .FirstOrDefaultAsync(s => s.Id == sectionId && s.OrganizationPageId == pageId, ct);
        if (section is null) return NotFound();

        db.CmsSections.Remove(section);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(CmsSection), sectionId, section, userId.Value, AppSources.WebApi));
        return NoContent();
    }
}

// ── Request records ───────────────────────────────────────────────────────────

public sealed record CreateCmsSectionRequest(
    CmsSectionType SectionType,
    string? Title,
    string ContentJson,
    int SortOrder,
    bool IsActive);

public sealed record UpdateCmsSectionRequest(
    string? Title,
    string ContentJson,
    bool IsActive);

public sealed record ReorderCmsSectionsRequest(IList<Guid> OrderedSectionIds);
