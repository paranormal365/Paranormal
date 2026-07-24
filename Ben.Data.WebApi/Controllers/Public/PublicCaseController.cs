using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Public;

/// <summary>
/// Public case discovery — no authentication required.
/// Only cases with IsPublic = true and status Public or Haunted are returned.
/// Client identity is replaced by PublicPseudonym where set.
/// </summary>
[ApiController]
[Route("api/public/organizations/{orgUrlName}")]
[AllowAnonymous]
public sealed class PublicCaseController : ControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public PublicCaseController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    { _db = db; _mapper = mapper; }

    /// <summary>Returns all public cases for an organization by URL name.</summary>
    [HttpGet("cases")]
    public async Task<ActionResult<IEnumerable<PublicCaseListItem>>> GetPublicCases(
        string orgUrlName, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var org = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.UrlName == orgUrlName, ct);
        if (org is null) return NotFound();

        var cases = await db.Cases.AsNoTracking()
            .Where(c => c.OrganizationId == org.Id
                     && c.IsPublic
                     && (c.Status == CaseStatus.Public || c.Status == CaseStatus.Haunted))
            .OrderByDescending(c => c.DateCaseOpened)
            .ToListAsync(ct);

        var result = cases.Select(c => new PublicCaseListItem(
            CaseReference:    $"#{c.CaseYear}-{c.OrgCaseNumber:D3}",
            Title:            c.Title,
            City:             c.City,
            State:            c.State,
            Status:           c.Status,
            DateCaseOpened:   c.DateCaseOpened,
            DateCaseClosed:   c.DateCaseClosed,
            IsHaunted:        c.Status == CaseStatus.Haunted));

        return Ok(result);
    }

    /// <summary>Returns the public detail of a specific case by reference (e.g. "2026-042").</summary>
    [HttpGet("cases/{caseRef}")]
    public async Task<ActionResult<PublicCaseDetail>> GetPublicCase(
        string orgUrlName, string caseRef, CancellationToken ct)
    {
        // Parse caseRef: "2026-042" → year=2026, number=42
        var parts = caseRef.TrimStart('#').Split('-');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int year) || !int.TryParse(parts[1], out int number))
            return BadRequest("Invalid case reference. Expected format: 2026-042");

        await using var db = await _db.CreateDbContextAsync(ct);
        var org = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.UrlName == orgUrlName, ct);
        if (org is null) return NotFound();

        var c = await db.Cases.AsNoTracking()
            .Include(x => x.TimelineEntries.Where(e => e.IsPublic).OrderBy(e => e.EventDateTime ?? e.DateCreated))
            .FirstOrDefaultAsync(x => x.OrganizationId == org.Id
                                   && x.CaseYear == year && x.OrgCaseNumber == number
                                   && x.IsPublic
                                   && (x.Status == CaseStatus.Public || x.Status == CaseStatus.Haunted), ct);
        if (c is null) return NotFound();

        // Apply pseudonym: replace client identifiers with PublicPseudonym when set
        var clientName = string.IsNullOrWhiteSpace(c.PublicPseudonym) ? null : c.PublicPseudonym;

        var publicTimeline = c.TimelineEntries.Select(e => new PublicTimelineEntry(
            EntryType:     e.EntryType,
            EventDateTime: e.EventDateTime,
            Title:         e.Title,
            Body:          e.Body)).ToList();

        return Ok(new PublicCaseDetail(
            CaseReference:  $"#{c.CaseYear}-{c.OrgCaseNumber:D3}",
            Title:          c.Title,
            City:           c.City,
            State:          c.State,
            Country:        c.Country,
            Status:         c.Status,
            IsHaunted:      c.Status == CaseStatus.Haunted,
            ClientName:     clientName,
            Description:    c.Description,
            DateCaseOpened: c.DateCaseOpened,
            DateCaseClosed: c.DateCaseClosed,
            Timeline:       publicTimeline,
            OrgName:        org.Name,
            OrgUrlName:     org.UrlName));
    }
}

// ── Public response records ───────────────────────────────────────────────────

public sealed record PublicCaseListItem(
    string CaseReference,
    string Title,
    string City,
    string State,
    Ben.Data.Common.Enums.CaseStatus Status,
    DateTime DateCaseOpened,
    DateTime? DateCaseClosed,
    bool IsHaunted);

public sealed record PublicCaseDetail(
    string CaseReference,
    string Title,
    string City,
    string State,
    string Country,
    Ben.Data.Common.Enums.CaseStatus Status,
    bool IsHaunted,
    string? ClientName,
    string? Description,
    DateTime DateCaseOpened,
    DateTime? DateCaseClosed,
    IReadOnlyList<PublicTimelineEntry> Timeline,
    string OrgName,
    string OrgUrlName);

public sealed record PublicTimelineEntry(
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType,
    DateTime? EventDateTime,
    string? Title,
    string? Body);
