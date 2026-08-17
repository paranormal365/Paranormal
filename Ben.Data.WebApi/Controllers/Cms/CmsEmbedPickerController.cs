using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Cms;

/// <summary>
/// What a group may choose from when embedding its own work in a page.
/// </summary>
/// <remarks>
/// <para>Offers only this organization's own cases and investigations, and says of each whether it
/// is <b>already public</b>. That flag is what the editor turns into Ben's warning: adding work
/// that is not already public is a decision, and the person making it should be told before rather
/// than after.</para>
///
/// <para><b>This list is a convenience, not a control.</b> Nothing here is what stops a group
/// embedding somebody else's investigation — <see cref="CmsEmbed.ResolveAsync"/> re-checks
/// ownership when the page is read, because a request can say whatever it likes and a picker that
/// only offers the right options is not the same as a rule.</para>
/// </remarks>
[ApiController]
[Route("api/organizations/{orgId:guid}/cms/embeddable")]
public sealed class CmsEmbedPickerController : OrgCmsControllerBase
{
    public CmsEmbedPickerController(
        IDbContextFactory<BenDataContext> dbFactory, IMapper mapper, IOrganizationSecurityService security)
        : base(dbFactory, mapper, security) { }

    /// <summary>The group's investigations, most recent first.</summary>
    [HttpGet("investigations")]
    public async Task<ActionResult<IReadOnlyList<EmbeddableRecord>>> GetInvestigations(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        // Read on sections, matching the preview endpoint: this is a list of things you could put on
        // a page, so whoever may open the editor may see it.
        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection,
                                        OrganizationSecurityAction.Read, ct))
            return NotFound();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var rows = await db.Investigations.AsNoTracking()
            .Where(i => i.OrganizationId == orgId)
            .OrderByDescending(i => i.ScheduledDateTime)
            .Select(i => new EmbeddableRecord(
                i.Id,
                i.Title,
                i.ScheduledDateTime,
                i.Visibility == InvestigationVisibility.Public,
                i.Place != null ? i.Place.Name : null))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>The group's cases, most recent first.</summary>
    [HttpGet("cases")]
    public async Task<ActionResult<IReadOnlyList<EmbeddableRecord>>> GetCases(
        Guid orgId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null) return Unauthorized();

        if (!await IsCmsAuthorizedAsync(userId.Value, orgId, OrganizationSecurityTable.CmsSection,
                                        OrganizationSecurityAction.Read, ct))
            return NotFound();

        await using var db = await DbFactory.CreateDbContextAsync(ct);

        var rows = await db.Cases.AsNoTracking()
            .Where(c => c.OrganizationId == orgId)
            .OrderByDescending(c => c.DateCreated)
            .Select(c => new EmbeddableRecord(
                c.Id,
                c.Title,
                c.DateCreated,
                // The same two conditions the case's own public page uses.
                c.IsPublic && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted),
                c.City))
            .ToListAsync(ct);

        return Ok(rows);
    }
}

// EmbeddableRecord lives in Ben.Service.Models — the Blazor picker needs the same shape, and a
// hand-mirrored copy is how two definitions of "is this already public" start disagreeing.
