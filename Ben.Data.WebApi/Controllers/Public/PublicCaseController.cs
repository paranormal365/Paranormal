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
/// Client identity is replaced by the client's alias, or the org's pseudonym, or nothing —
/// see <see cref="PublicClientName"/>. A real client name is never emitted here.
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
            // Falls back to the reference for a case published before slugs existed, so a card
            // always has somewhere to point rather than silently linking nowhere.
            UrlName:          c.UrlName ?? $"{c.CaseYear}-{c.OrgCaseNumber:D3}",
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
    /// <summary>
    /// Splits "2026-042" or "#2026-042" into its parts, or nulls when it is not a reference at all.
    /// </summary>
    /// <remarks>
    /// Nulls rather than a failure: a segment that is not a reference is almost certainly a slug,
    /// and the old behaviour — a 400 saying "expected format 2026-042" — would now be wrong for
    /// every readable address on the site.
    /// </remarks>
    private static (int? Year, int? Number) ParseCaseReference(string value)
    {
        var parts = value.TrimStart('#').Split('-');
        return parts.Length == 2 && int.TryParse(parts[0], out var year) && int.TryParse(parts[1], out var number)
            ? (year, number)
            : (null, null);
    }

    [HttpGet("cases/{caseRef}")]
    public async Task<ActionResult<PublicCaseDetail>> GetPublicCase(
        string orgUrlName, string caseRef, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        var org = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.UrlName == orgUrlName, ct);
        if (org is null) return NotFound();

        // Accepts either the readable slug or the old "2026-042" reference. The slug is what people
        // share; the reference is what an organization says out loud to a client, and both should
        // land on the same page rather than one of them being a dead end.
        var slug = caseRef.Trim().ToLowerInvariant();
        var (refYear, refNumber) = ParseCaseReference(caseRef);

        var c = await db.Cases.AsNoTracking()
            .Include(x => x.TimelineEntries.Where(e => e.Visibility == CaseTimelineVisibility.Public).OrderBy(e => e.EventDateTime ?? e.DateCreated).ThenBy(e => e.DateCreated).ThenBy(e => e.Id))
                .ThenInclude(e => e.Files)
            .FirstOrDefaultAsync(x => x.OrganizationId == org.Id
                                   && x.IsPublic
                                   && (x.Status == CaseStatus.Public || x.Status == CaseStatus.Haunted)
                                   && (x.UrlName == slug
                                       || (refYear != null && x.CaseYear == refYear && x.OrgCaseNumber == refNumber)), ct);
        if (c is null) return NotFound();

        // The client's own alias when they set one, the org's pseudonym otherwise, and nothing
        // if neither — the real name is never an outcome here. See PublicClientName.
        var clientName = PublicClientName.For(c);

        var publicTimeline = c.TimelineEntries.Select(e => new PublicTimelineEntry(
            EntryType:      e.EntryType,
            EventDateTime:  e.EventDateTime,
            Title:          e.Title,
            Body:           e.Body,
            EvidenceFileIds: e.EntryType == CaseTimelineEntryType.Evidence
                ? e.Files.Select(f => f.UploadFileId).ToList()
                : [])).ToList();

        return Ok(new PublicCaseDetail(
            CaseId:         c.Id,
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
    // The readable address to link to, falling back to the reference.
    string UrlName,
    string Title,
    string City,
    string State,
    Ben.Data.Common.Enums.CaseStatus Status,
    DateTime DateCaseOpened,
    DateTime? DateCaseClosed,
    bool IsHaunted);

public sealed record PublicCaseDetail(
    Guid CaseId,
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
    string? Body,
    IReadOnlyList<Guid> EvidenceFileIds);
