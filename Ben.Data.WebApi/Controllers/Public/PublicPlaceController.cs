using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Controllers.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// A place as a visitor sees it: only investigations somebody deliberately published.
/// </summary>
/// <remarks>
/// <para>Separate from the signed-in <see cref="PlaceController"/> rather than one endpoint with a
/// branch on whether there is a user, because the two answer different questions and the anonymous
/// one is the dangerous one to get wrong.</para>
///
/// <para><b>It still goes through <see cref="InvestigationVisibilityFilter.VisibleTo"/></b>, passed
/// an empty set of organizations. Writing <c>Where(i => i.Visibility == Public)</c> here would be
/// shorter and would be a second copy of the sharing rules — which is exactly how the rule that
/// holds in one place stops holding in another.</para>
/// </remarks>
[ApiController]
[Route("api/public/places")]
[AllowAnonymous]
public sealed class PublicPlaceController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public PublicPlaceController(IDbContextFactory<BenDataContext> db) => _db = db;

    /// <summary>The place itself, and everything published about it.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PublicPlaceResponse>> GetById(Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);

        var place = await db.Places.AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PlaceRecord(
                p.Id, p.Name, p.StreetAddress1, p.City, p.State, p.ZipCode, p.Country,
                p.Latitude, p.Longitude, p.GeocodeNote, p.Kind))
            .FirstOrDefaultAsync(ct);

        if (place is null) return NotFound();

        // An anonymous caller belongs to no organizations and has investigated nowhere, so the
        // shared predicate resolves to "public only" on its own. No second rule to keep in step.
        var rows = await db.Investigations.AsNoTracking()
            .Where(i => i.PlaceId == id)
            .Where(InvestigationVisibilityFilter.VisibleTo([], []))
            .OrderByDescending(i => i.ScheduledDateTime)
            .Select(i => new PublicPlaceInvestigationRow(
                i.Id,
                i.UrlName,
                i.Title,
                i.ScheduledDateTime,
                i.Status,
                i.Organization.Name,
                i.Organization.UrlName))
            .ToListAsync(ct);

        return Ok(new PublicPlaceResponse(place, rows, PlaceSummary.From(rows)));
    }
}

/// <summary>What a visitor gets for one place.</summary>
public sealed record PublicPlaceResponse(
    PlaceRecord Place,
    IReadOnlyList<PublicPlaceInvestigationRow> Investigations,
    PlaceSummary Summary);

/// <summary>
/// One published investigation. Deliberately thinner than the signed-in row: no visibility (every
/// row here is public by definition) and no organization id, since a visitor gets the group's
/// public URL name instead.
/// </summary>
public sealed record PublicPlaceInvestigationRow(
    Guid Id,
    // The readable address of this investigation's own page, or null for one published before
    // slugs existed. Without it the row has nowhere to link, which is how a list of published
    // work becomes a list nobody can read.
    string? UrlName,
    string Title,
    DateTime ScheduledDateTime,
    InvestigationStatus Status,
    string OrganizationName,
    string OrganizationUrlName);

/// <summary>
/// "N investigations by M groups since Y" — the line that makes a place feel like a history rather
/// than a list.
/// </summary>
/// <param name="InvestigationCount">Visits the caller may see — never the raw total.</param>
/// <param name="OrganizationCount">Distinct groups among those visits.</param>
/// <param name="Since">Null when nothing is visible, so the caller can omit the phrase entirely.</param>
public sealed record PlaceSummary(int InvestigationCount, int OrganizationCount, int? Since)
{
    internal static PlaceSummary From(IReadOnlyList<PublicPlaceInvestigationRow> rows) => new(
        rows.Count,
        rows.Select(r => r.OrganizationName).Distinct().Count(),
        rows.Count == 0 ? null : rows.Min(r => r.ScheduledDateTime).Year);
}
