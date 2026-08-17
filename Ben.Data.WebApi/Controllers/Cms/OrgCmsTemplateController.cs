using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>
/// A group's own saved building blocks: a section it wants to reuse, or a whole page's layout.
/// </summary>
/// <remarks>
/// <para>The user half of the template library. We ship the blocks — card, collapsible list,
/// carousel — and a group assembles them into something of its own and saves it here to start from
/// next time.</para>
///
/// <para><b>Sanitized on save, exactly like a page.</b> That a block came from our palette says
/// nothing about what was typed into it afterwards, and a template is worse than a page in one
/// respect: it is inserted by colleagues, so one member's markup ends up in everybody's browser.
/// The realistic case is somebody pasting a widget they found online, not an attacker.</para>
///
/// <para><b>Inserting copies.</b> A page built from a template does not track it, so editing the
/// template later leaves existing pages alone — nobody expects tidying a template to rewrite a page
/// that has been live for a year.</para>
/// </remarks>
[Route("api/organizations/{orgId:guid}/cms-templates")]
public sealed class OrgCmsTemplateController : OrgCmsControllerBase
{
    private readonly ICmsMarkupSanitizer _sanitizer;

    public OrgCmsTemplateController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper,
        IOrganizationSecurityService security, ICmsMarkupSanitizer sanitizer)
        : base(dbFactory, mapper, security) => _sanitizer = sanitizer;

    /// <summary>Everything this group has saved, optionally narrowed to one kind.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CmsTemplateRecord>>> GetAll(
        Guid orgId, [FromQuery] CmsTemplateScope? scope, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection,
                                        OrganizationSecurityAction.Read, ct))
            return NotFound();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var query = db.OrganizationCmsTemplates.AsNoTracking().Where(t => t.OrganizationId == orgId);
        if (scope is CmsTemplateScope s) query = query.Where(t => t.Scope == s);

        var templates = await query
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name)
            .Select(t => new CmsTemplateRecord(
                t.Id, t.Name, t.Description, t.Scope, t.SectionType, t.ContentJson, t.SortOrder, t.DateCreated))
            .ToListAsync(ct);

        return Ok(templates);
    }

    /// <summary>Saves a section, or a page's worth of sections, as a reusable template.</summary>
    [HttpPost]
    public async Task<ActionResult<CmsTemplateRecord>> Create(
        Guid orgId, [FromBody] SaveCmsTemplateRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection,
                                        OrganizationSecurityAction.Create, ct))
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("A name is needed.");

        var content = Clean(request.Scope, request.ContentJson);
        if (content is null) return BadRequest("That content could not be read.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var name = request.Name.Trim();
        if (await db.OrganizationCmsTemplates.AnyAsync(
                t => t.OrganizationId == orgId && t.Scope == request.Scope && t.Name == name, ct))
            return Conflict($"This group already has a template called '{name}'.");

        var template = new OrganizationCmsTemplate
        {
            Id                 = Guid.NewGuid(),
            OrganizationId     = orgId,
            Name               = name,
            Description        = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Scope              = request.Scope,
            SectionType        = request.SectionType,
            ContentJson        = content,
            SortOrder          = request.SortOrder,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId.Value,
        };

        db.OrganizationCmsTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        return Ok(new CmsTemplateRecord(
            template.Id, template.Name, template.Description, template.Scope,
            template.SectionType, template.ContentJson, template.SortOrder, template.DateCreated));
    }

    /// <summary>Renames a template or replaces what it contains.</summary>
    [HttpPut("{templateId:guid}")]
    public async Task<ActionResult<CmsTemplateRecord>> Update(
        Guid orgId, Guid templateId, [FromBody] SaveCmsTemplateRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection,
                                        OrganizationSecurityAction.Update, ct))
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("A name is needed.");

        var content = Clean(request.Scope, request.ContentJson);
        if (content is null) return BadRequest("That content could not be read.");

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var template = await db.OrganizationCmsTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.OrganizationId == orgId, ct);
        if (template is null) return NotFound();

        var name = request.Name.Trim();
        if (await db.OrganizationCmsTemplates.AnyAsync(
                t => t.OrganizationId == orgId && t.Scope == request.Scope
                  && t.Name == name && t.Id != templateId, ct))
            return Conflict($"This group already has a template called '{name}'.");

        template.Name               = name;
        template.Description        = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        template.SectionType        = request.SectionType;
        template.ContentJson        = content;
        template.SortOrder          = request.SortOrder;
        template.DateUpdated        = DateTime.UtcNow;
        template.UpdatedByAppUserId = userId.Value;

        await db.SaveChangesAsync(ct);

        return Ok(new CmsTemplateRecord(
            template.Id, template.Name, template.Description, template.Scope,
            template.SectionType, template.ContentJson, template.SortOrder, template.DateCreated));
    }

    /// <summary>
    /// Removes a template. Pages already built from it are untouched, because inserting copied it.
    /// </summary>
    [HttpDelete("{templateId:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid templateId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection,
                                        OrganizationSecurityAction.Delete, ct))
            return NotFound();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var template = await db.OrganizationCmsTemplates
            .FirstOrDefaultAsync(t => t.Id == templateId && t.OrganizationId == orgId, ct);
        if (template is null) return NotFound();

        db.OrganizationCmsTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Cleans a template's content, or returns null when it cannot be read.
    /// </summary>
    /// <remarks>
    /// A page template is an array of sections, so every element's markup is cleaned rather than
    /// only the outermost object — a sanitizer that only looked at the top level would pass a page
    /// template through untouched, which is the failure that would be hardest to notice.
    /// </remarks>
    private string? Clean(CmsTemplateScope scope, string? contentJson)
    {
        if (string.IsNullOrWhiteSpace(contentJson)) return "{}";

        if (scope == CmsTemplateScope.Section)
            return _sanitizer.SanitizeContentJson(contentJson);

        List<CmsTemplateSectionRecord>? sections;
        try
        {
            sections = JsonSerializer.Deserialize<List<CmsTemplateSectionRecord>>(contentJson);
        }
        catch (JsonException)
        {
            return null;
        }

        if (sections is null) return null;

        var cleaned = sections
            .Select(s => s with { ContentJson = _sanitizer.SanitizeContentJson(s.ContentJson) })
            .ToList();

        return JsonSerializer.Serialize(cleaned);
    }
}
