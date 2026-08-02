using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers;

/// <summary>
/// Client-facing case dashboard. All routes require authentication as the case's client.
/// Cases are identified by the ClientRequest that originated them; the requesting user
/// is considered the primary client for that case.
/// </summary>
[ApiController]
[Route("api/my-cases")]
[Authorize]
public sealed class MyCaseController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;
    private readonly IMapper _mapper;

    public MyCaseController(IDbContextFactory<BenDataContext> db, IMapper mapper)
    { _db = db; _mapper = mapper; }

    /// <summary>Returns all active cases where the current user is the originating client.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClientCaseListItem>>> GetMyCases(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var cases = await db.Cases.AsNoTracking()
            .Include(c => c.ClientRequest)
            .Include(c => c.CaseManagerAppUser)
            .Where(c => c.ClientRequest != null && c.ClientRequest.AppUserId == userId
                     && c.Status != CaseStatus.Proposed)
            .OrderByDescending(c => c.DateCaseOpened)
            .ToListAsync(ct);

        return Ok(cases.Select(c => new ClientCaseListItem(
            CaseId:                  c.Id,
            CaseReference:           $"#{c.CaseYear}-{c.OrgCaseNumber:D3}",
            Title:                   c.Title,
            City:                    c.City,
            State:                   c.State,
            Status:                  c.Status,
            CaseManagerDisplayName:  c.CaseManagerAppUser?.DisplayName,
            DateCaseOpened:          c.DateCaseOpened)));
    }

    /// <summary>
    /// Returns full case detail for the client: header, client-accessible timeline
    /// entries, and upcoming investigations.
    /// </summary>
    [HttpGet("{caseId:guid}")]
    public async Task<ActionResult<ClientCaseDetail>> GetMyCase(Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var c = await db.Cases.AsNoTracking()
            .Include(x => x.ClientRequest)
            .Include(x => x.CaseManagerAppUser)
            .Include(x => x.TimelineEntries.Where(e =>
                e.EntryType == CaseTimelineEntryType.ClientReport ||
                (e.EntryType == CaseTimelineEntryType.Evidence && e.IsPublic))
                .OrderBy(e => e.EventDateTime ?? e.DateCreated))
            .FirstOrDefaultAsync(x => x.Id == caseId
                && x.ClientRequest != null && x.ClientRequest.AppUserId == userId, ct);

        if (c is null) return NotFound();

        var investigations = await db.Investigations.AsNoTracking()
            .Where(i => i.CaseId == caseId && i.Status != InvestigationStatus.Cancelled)
            .OrderBy(i => i.ScheduledDateTime)
            .ToListAsync(ct);

        var occurrences = c.TimelineEntries.Select(e => new ClientCaseOccurrence(
            Id:            e.Id,
            EntryType:     e.EntryType,
            EventDateTime: e.EventDateTime,
            Title:         e.Title,
            Body:          e.Body,
            DateCreated:   e.DateCreated)).ToList();

        var invItems = investigations.Select(i => new ClientCaseInvestigation(
            Id:                i.Id,
            Title:             i.Title,
            ScheduledDateTime: i.ScheduledDateTime,
            EndDateTime:       i.EndDateTime,
            Location:          i.Location,
            Status:            i.Status,
            EvidenceDueDate:   i.EvidenceDueDate)).ToList();

        return Ok(new ClientCaseDetail(
            CaseId:                  c.Id,
            CaseReference:           $"#{c.CaseYear}-{c.OrgCaseNumber:D3}",
            Title:                   c.Title,
            City:                    c.City,
            State:                   c.State,
            Status:                  c.Status,
            Description:             c.Description,
            CaseManagerDisplayName:  c.CaseManagerAppUser?.DisplayName,
            DateCaseOpened:          c.DateCaseOpened,
            DateCaseClosed:          c.DateCaseClosed,
            Occurrences:             occurrences,
            Investigations:          invItems));
    }

    /// <summary>
    /// Logs a new occurrence (ClientReport timeline entry) on the client's case.
    /// The entry is created with IsPublic = false; the case manager can approve it.
    /// </summary>
    [HttpPost("{caseId:guid}/occurrences")]
    public async Task<ActionResult<CaseTimelineEntryRecord>> LogOccurrence(
        Guid caseId, [FromBody] LogOccurrenceRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var c = await db.Cases.AsNoTracking()
            .Include(x => x.ClientRequest)
            .FirstOrDefaultAsync(x => x.Id == caseId
                && x.ClientRequest != null && x.ClientRequest.AppUserId == userId, ct);
        if (c is null) return NotFound();

        var entry = new CaseTimelineEntry
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            AuthorAppUserId    = userId,
            EntryType          = CaseTimelineEntryType.ClientReport,
            EventDateTime      = request.EventDateTime,
            Title              = request.Title?.Trim(),
            Body               = request.Body?.Trim(),
            IsPublic           = false,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.CaseTimelineEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        var loaded = await db.CaseTimelineEntries.AsNoTracking()
            .Include(e => e.AuthorAppUser)
            .Include(e => e.ExperienceTypes)
            .FirstAsync(e => e.Id == entry.Id, ct);
        return Ok(_mapper.Map<CaseTimelineEntryRecord>(loaded));
    }

    /// <summary>Updates an occurrence the client previously logged.</summary>
    [HttpPut("{caseId:guid}/occurrences/{entryId:guid}")]
    public async Task<ActionResult<CaseTimelineEntryRecord>> UpdateOccurrence(
        Guid caseId, Guid entryId, [FromBody] LogOccurrenceRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entry = await db.CaseTimelineEntries
            .Include(e => e.Case).ThenInclude(c => c.ClientRequest)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId
                && e.AuthorAppUserId == userId
                && e.EntryType == CaseTimelineEntryType.ClientReport, ct);
        if (entry is null) return NotFound();
        if (entry.Case.ClientRequest?.AppUserId != userId) return Forbid();

        entry.EventDateTime      = request.EventDateTime;
        entry.Title              = request.Title?.Trim();
        entry.Body               = request.Body?.Trim();
        entry.DateUpdated        = DateTime.UtcNow;
        entry.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);

        var loaded = await db.CaseTimelineEntries.AsNoTracking()
            .Include(e => e.AuthorAppUser)
            .Include(e => e.ExperienceTypes)
            .FirstAsync(e => e.Id == entry.Id, ct);
        return Ok(_mapper.Map<CaseTimelineEntryRecord>(loaded));
    }

    /// <summary>Deletes an occurrence the client previously logged.</summary>
    [HttpDelete("{caseId:guid}/occurrences/{entryId:guid}")]
    public async Task<IActionResult> DeleteOccurrence(Guid caseId, Guid entryId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        var entry = await db.CaseTimelineEntries
            .Include(e => e.Case).ThenInclude(c => c.ClientRequest)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId
                && e.AuthorAppUserId == userId
                && e.EntryType == CaseTimelineEntryType.ClientReport, ct);
        if (entry is null) return NotFound();
        if (entry.Case.ClientRequest?.AppUserId != userId) return Forbid();

        db.CaseTimelineEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}

// ── Response records ──────────────────────────────────────────────────────────

public sealed record ClientCaseListItem(
    Guid      CaseId,
    string    CaseReference,
    string    Title,
    string    City,
    string    State,
    Ben.Data.Common.Enums.CaseStatus Status,
    string?   CaseManagerDisplayName,
    DateTime  DateCaseOpened);

public sealed record ClientCaseDetail(
    Guid      CaseId,
    string    CaseReference,
    string    Title,
    string    City,
    string    State,
    Ben.Data.Common.Enums.CaseStatus Status,
    string?   Description,
    string?   CaseManagerDisplayName,
    DateTime  DateCaseOpened,
    DateTime? DateCaseClosed,
    IReadOnlyList<ClientCaseOccurrence>    Occurrences,
    IReadOnlyList<ClientCaseInvestigation> Investigations);

public sealed record ClientCaseOccurrence(
    Guid      Id,
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType,
    DateTime? EventDateTime,
    string?   Title,
    string?   Body,
    DateTime  DateCreated);

public sealed record ClientCaseInvestigation(
    Guid       Id,
    string     Title,
    DateTime   ScheduledDateTime,
    DateTime?  EndDateTime,
    string?    Location,
    Ben.Data.Common.Enums.InvestigationStatus Status,
    DateTime?  EvidenceDueDate = null);

public sealed record LogOccurrenceRequest(
    DateTime? EventDateTime,
    string?   Title,
    string?   Body);
