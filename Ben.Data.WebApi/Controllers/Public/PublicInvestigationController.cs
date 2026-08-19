using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Services;
using Ben.Data.WebApi.Services.Access;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// One investigation a group has published, at its own shareable address.
/// </summary>
/// <remarks>
/// <para>Until now a published investigation could only be reached through the page of the place it
/// happened at — which is a fine way to browse and a poor way to share. An organization writing up a
/// night's work wants a link to put in a post, and <c>/o/{org}/investigations/{slug}</c> is it.</para>
///
/// <para><b>Flat under the organization, not nested under a case.</b> <c>Investigation.CaseId</c> is
/// nullable — a group can investigate a landmark with no client — so an address assuming the case
/// would have no form for those at all.</para>
///
/// <para><b>Visibility goes through <see cref="InvestigationVisibilityFilter.VisibleTo"/></b> with
/// no organizations, exactly as the place page does. Writing <c>Visibility == Public</c> here would
/// be a second copy of a rule that already exists, and the copy is the one that drifts.</para>
/// </remarks>
[ApiController]
[Route("api/public/organizations/{orgUrlName}/investigations")]
public sealed class PublicInvestigationController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicInvestigationController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>Everything this organization has published, most recent first.</summary>
    [HttpGet]
    [AllowAnonymous]
    public async Task<ActionResult<IReadOnlyList<PublicInvestigationListItem>>> GetPublished(
        string orgUrlName, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var (organization, _) = await OrganizationUrlNames.ResolveAsync(db, orgUrlName, ct);
        if (organization is null) return Ok(Array.Empty<PublicInvestigationListItem>());

        var rows = await db.Investigations.AsNoTracking()
            .Where(i => i.OrganizationId == organization.Id)
            .Where(InvestigationVisibilityFilter.VisibleTo([], []))
            .OrderByDescending(i => i.ScheduledDateTime)
            .Select(i => new PublicInvestigationListItem(
                i.Id, i.UrlName, i.Title, i.ScheduledDateTime, i.Status,
                i.Organization.Name, i.Organization.UrlName,
                i.Place != null ? i.Place.Name : null,
                i.Place != null ? i.Place.City : null,
                i.Place != null ? i.Place.State : null))
            .ToListAsync(ct);

        return Ok(rows);
    }

    /// <summary>One published investigation, by the address people share.</summary>
    [HttpGet("{investigationSlug}")]
    [AllowAnonymous]
    public async Task<ActionResult<PublicInvestigationDetail>> GetPublished(
        string orgUrlName, string investigationSlug, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var slug = Ben.Data.Common.SlugText.NormalizeOrEmpty(investigationSlug);

        var (organization, _) = await OrganizationUrlNames.ResolveAsync(db, orgUrlName, ct);
        if (organization is null) return NotFound();

        var i = await db.Investigations.AsNoTracking()
            .Include(x => x.Organization)
            .Include(x => x.Place)
            .Where(x => x.OrganizationId == organization.Id && x.UrlName == slug)
            .Where(InvestigationVisibilityFilter.VisibleTo([], []))
            .FirstOrDefaultAsync(ct);

        if (i is null) return NotFound();

        // Approximate, always. A published write-up says a group was somewhere; it does not have to
        // say precisely where, and the same grid protects a landmark's neighbours as a client's.
        var (lat, lon) = PublicCoordinates.Approximate(i.Place?.Latitude, i.Place?.Longitude);

        return Ok(new PublicInvestigationDetail(
            i.Id,
            i.UrlName,
            i.Title,
            // Notes are the write-up. Description is the plan, which is internal.
            i.Notes,
            i.ScheduledDateTime,
            i.EndDateTime,
            i.Status,
            i.Organization.Name,
            i.Organization.UrlName,
            i.Place?.Id,
            i.Place?.Name,
            i.Place?.City,
            i.Place?.State,
            lat,
            lon));
    }
}
