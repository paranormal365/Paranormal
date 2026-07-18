using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
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
    public CmsSectionController(
        IDbContextFactory<BenDataContext> dbFactory,
        IMapper mapper,
        IOrganizationSecurityService security)
        : base(dbFactory, mapper, security) { }

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
            ContentJson        = string.IsNullOrWhiteSpace(request.ContentJson) ? "{}" : request.ContentJson,
            SortOrder          = request.SortOrder,
            IsActive           = request.IsActive,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value
        };

        db.CmsSections.Add(section);
        await db.SaveChangesAsync(ct);

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
        var section = await db.CmsSections
            .FirstOrDefaultAsync(s => s.Id == sectionId && s.OrganizationPageId == pageId, ct);
        if (section is null) return NotFound();

        section.Title              = request.Title?.Trim();
        section.ContentJson        = string.IsNullOrWhiteSpace(request.ContentJson) ? "{}" : request.ContentJson;
        section.IsActive           = request.IsActive;
        section.DateUpdated        = DateTime.UtcNow;
        section.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);
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

        var sections = await db.CmsSections
            .Where(s => s.OrganizationPageId == pageId)
            .ToListAsync(ct);

        for (var i = 0; i < request.OrderedSectionIds.Count; i++)
        {
            var section = sections.FirstOrDefault(s => s.Id == request.OrderedSectionIds[i]);
            if (section is not null) section.SortOrder = i + 1;
        }

        await db.SaveChangesAsync(ct);
        return NoContent();
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
        var section = await db.CmsSections
            .FirstOrDefaultAsync(s => s.Id == sectionId && s.OrganizationPageId == pageId, ct);
        if (section is null) return NotFound();

        db.CmsSections.Remove(section);
        await db.SaveChangesAsync(ct);
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
