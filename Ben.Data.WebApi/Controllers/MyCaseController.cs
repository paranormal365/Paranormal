using AutoMapper;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
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
    private readonly IFileStorageService _fileStorage;
    private readonly FileMetadataExtractorService _metadataExtractor;

    // Fixed Guid for the 'Case Evidence' upload file type seeded by UploadFileTypeSeeder
    private static readonly Guid EvidenceFileTypeId = new("20000000-0000-0000-0000-000000000001");

    public MyCaseController(IDbContextFactory<BenDataContext> db, IMapper mapper,
        IFileStorageService fileStorage, FileMetadataExtractorService metadataExtractor)
    { _db = db; _mapper = mapper; _fileStorage = fileStorage; _metadataExtractor = metadataExtractor; }

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
                .ThenInclude(e => e.Files)
                .ThenInclude(f => f.UploadFile)
            .FirstOrDefaultAsync(x => x.Id == caseId
                && x.ClientRequest != null && x.ClientRequest.AppUserId == userId, ct);

        if (c is null) return NotFound();

        var investigations = await db.Investigations.AsNoTracking()
            .Where(i => i.CaseId == caseId && i.Status != InvestigationStatus.Cancelled)
            .OrderBy(i => i.ScheduledDateTime)
            .ToListAsync(ct);

        var unreadCount = await db.CaseMessages
            .CountAsync(m => m.CaseId == caseId && m.SenderSide == CaseMessageSide.Organization && !m.IsReadByClient, ct);

        var occurrences = c.TimelineEntries.Select(e => new ClientCaseOccurrence(
            Id:            e.Id,
            EntryType:     e.EntryType,
            EventDateTime: e.EventDateTime,
            Title:         e.Title,
            Body:          e.Body,
            DateCreated:   e.DateCreated,
            Files:         e.Files.Select(f => new OccurrenceFileItem(
                f.UploadFileId, f.UploadFile.FileName, f.UploadFile.ContentType, f.UploadFile.FileSize)).ToList())).ToList();

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
            Investigations:          invItems,
            UnreadMessageCount:      unreadCount));
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
            IpAddress          = HttpContext.Connection.RemoteIpAddress?.ToString(),
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

    // ── Client report view (published reports only) ────────────────────────────

    [HttpGet("{caseId:guid}/reports")]
    public async Task<ActionResult<IEnumerable<CaseReportSummary>>> GetPublishedReports(Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var reports = await db.CaseReports.AsNoTracking()
            .Where(r => r.CaseId == caseId && r.Status == Ben.Data.Common.Enums.CaseReportStatus.Published)
            .OrderByDescending(r => r.PublishedAt)
            .Select(r => new CaseReportSummary(r.Id, r.CaseId, r.Title, r.Status, r.ExpectedDeliveryDate, r.PublishedAt, r.DateCreated))
            .ToListAsync(ct);
        return Ok(reports);
    }

    /// <summary>Streams a published report as a PDF to the client.</summary>
    [HttpGet("{caseId:guid}/reports/{reportId:guid}/pdf")]
    public async Task<IActionResult> GetPublishedReportPdf(Guid caseId, Guid reportId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var report = await db.CaseReports.AsNoTracking()
            .Include(r => r.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Files.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.UploadFile)
            .FirstOrDefaultAsync(r => r.Id == reportId && r.CaseId == caseId
                && r.Status == Ben.Data.Common.Enums.CaseReportStatus.Published, ct);
        if (report is null) return NotFound();

        // Reuse the static PDF generator from CaseReportController via shared helper
        var pdfBytes = CaseReportPdfGenerator.Generate(report);
        return File(pdfBytes, "application/pdf", $"report-{report.Title.Replace(' ', '-')}.pdf");
    }

    // ── Investigation scheduling (client responds to proposed dates) ───────────

    [HttpGet("{caseId:guid}/schedule-proposals")]
    public async Task<ActionResult<IEnumerable<ScheduleProposalDto>>> GetScheduleProposals(Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var proposals = await db.InvestigationScheduleProposals.AsNoTracking()
            .Include(p => p.Slots)
            .Where(p => p.CaseId == caseId && p.Status == Ben.Data.Common.Enums.ScheduleProposalStatus.Pending)
            .OrderByDescending(p => p.DateCreated)
            .ToListAsync(ct);

        return Ok(proposals.Select(ProposalToDto));
    }

    [HttpPost("{caseId:guid}/schedule-proposals/{proposalId:guid}/accept")]
    public async Task<ActionResult<ScheduleProposalDto>> AcceptProposal(
        Guid caseId, Guid proposalId, [FromBody] AcceptProposalRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var proposal = await db.InvestigationScheduleProposals.Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.Id == proposalId && p.CaseId == caseId
                && p.Status == Ben.Data.Common.Enums.ScheduleProposalStatus.Pending, ct);
        if (proposal is null) return NotFound();

        var slot = proposal.Slots.FirstOrDefault(s => s.Id == request.SlotId);
        if (slot is null) return BadRequest("Slot not found in this proposal.");

        // Auto-create the Investigation
        var investigation = new Ben.Data.Source.Entities.Investigation
        {
            Id = Guid.NewGuid(), CaseId = caseId,
            Title = "Scheduled Investigation",
            ScheduledDateTime = slot.StartDateTime,
            EndDateTime = slot.EndDateTime,
            Status = Ben.Data.Common.Enums.InvestigationStatus.Scheduled,
            Notes = $"Scheduled via date negotiation; client accepted {slot.StartDateTime:MMM d, yyyy h:mm tt}.",
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.Investigations.Add(investigation);

        proposal.Status = Ben.Data.Common.Enums.ScheduleProposalStatus.AcceptedByClient;
        proposal.AcceptedSlotId = slot.Id;
        proposal.InvestigationId = investigation.Id;
        proposal.ClientRespondedAt = DateTime.UtcNow;
        proposal.DateUpdated = DateTime.UtcNow;
        proposal.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(ProposalToDto(proposal));
    }

    [HttpPost("{caseId:guid}/schedule-proposals/{proposalId:guid}/counter")]
    public async Task<ActionResult<ScheduleProposalDto>> CounterProposal(
        Guid caseId, Guid proposalId, [FromBody] CounterProposalRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var proposal = await db.InvestigationScheduleProposals.Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.Id == proposalId && p.CaseId == caseId
                && p.Status == Ben.Data.Common.Enums.ScheduleProposalStatus.Pending, ct);
        if (proposal is null) return NotFound();

        proposal.Status = Ben.Data.Common.Enums.ScheduleProposalStatus.Countered;
        proposal.ClientCounterDateTime = request.PreferredDateTime;
        proposal.ClientResponseNotes = request.Notes?.Trim();
        proposal.ClientRespondedAt = DateTime.UtcNow;
        proposal.DateUpdated = DateTime.UtcNow;
        proposal.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(ProposalToDto(proposal));
    }

    [HttpPost("{caseId:guid}/schedule-proposals/{proposalId:guid}/decline")]
    public async Task<ActionResult<ScheduleProposalDto>> DeclineProposal(
        Guid caseId, Guid proposalId, [FromBody] DeclineProposalRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var proposal = await db.InvestigationScheduleProposals.Include(p => p.Slots)
            .FirstOrDefaultAsync(p => p.Id == proposalId && p.CaseId == caseId
                && p.Status == Ben.Data.Common.Enums.ScheduleProposalStatus.Pending, ct);
        if (proposal is null) return NotFound();

        proposal.Status = Ben.Data.Common.Enums.ScheduleProposalStatus.Declined;
        proposal.ClientResponseNotes = request.Notes?.Trim();
        proposal.ClientRespondedAt = DateTime.UtcNow;
        proposal.DateUpdated = DateTime.UtcNow;
        proposal.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        return Ok(ProposalToDto(proposal));
    }

    private static ScheduleProposalDto ProposalToDto(Ben.Data.Source.Entities.InvestigationScheduleProposal p) => new(
        p.Id, p.CaseId, p.Status, p.Notes, p.AcceptedSlotId,
        p.ClientCounterDateTime, p.ClientResponseNotes, p.ClientRespondedAt,
        p.InvestigationId, p.DateCreated,
        p.Slots.OrderBy(s => s.SortOrder).Select(s => new SlotDto(s.Id, s.StartDateTime, s.EndDateTime, s.SortOrder)).ToList());

    // ── Occurrence file attachments ────────────────────────────────────────────

    /// <summary>Attaches a file to an occurrence. Saved to cases/{caseId}/... path.</summary>
    [HttpPost("{caseId:guid}/occurrences/{entryId:guid}/files")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<OccurrenceFileItem>> AttachFile(
        Guid caseId, Guid entryId, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("File is empty.");
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var entry = await db.CaseTimelineEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId
                && e.AuthorAppUserId == userId, ct);
        if (entry is null) return NotFound();

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var fileBytes = ms.ToArray();

        var storedName   = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
        var storagePath  = _fileStorage.CaseFilePath(caseId, storedName);
        using (var ws = new MemoryStream(fileBytes))
            await _fileStorage.WriteAsync(storagePath, ws, ct);

        var uploadFile = new UploadFile
        {
            Id                 = Guid.NewGuid(),
            UploadFileTypeId   = EvidenceFileTypeId,
            AppUserId          = userId,
            FileName           = file.FileName,
            StoredFileName     = storedName,
            ContentType        = file.ContentType,
            FileSize           = fileBytes.Length,
            StoragePath        = storagePath,
            IsPublic           = false,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.UploadFiles.Add(uploadFile);

        db.CaseTimelineEntryFiles.Add(new CaseTimelineEntryFile
        {
            Id                   = Guid.NewGuid(),
            CaseTimelineEntryId  = entryId,
            UploadFileId         = uploadFile.Id,
            DateCreated          = DateTime.UtcNow,
            CreatedByAppUserId   = userId,
        });
        await db.SaveChangesAsync(ct);

        // Metadata extraction fire-and-forget
        var capturedBytes = fileBytes;
        var capturedType  = file.ContentType;
        var capturedId    = uploadFile.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                var meta = _metadataExtractor.Extract(capturedId, capturedType, capturedBytes);
                await using var dbMeta = await _db.CreateDbContextAsync(CancellationToken.None);
                dbMeta.UploadFileMetadata.Add(meta);
                await dbMeta.SaveChangesAsync(CancellationToken.None);
            }
            catch { }
        });

        return Ok(new OccurrenceFileItem(uploadFile.Id, uploadFile.FileName, uploadFile.ContentType, uploadFile.FileSize));
    }

    /// <summary>Removes a file attachment from an occurrence and deletes the stored file.</summary>
    [HttpDelete("{caseId:guid}/occurrences/{entryId:guid}/files/{fileId:guid}")]
    public async Task<IActionResult> DetachFile(Guid caseId, Guid entryId, Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var link = await db.CaseTimelineEntryFiles
            .Include(f => f.UploadFile)
            .FirstOrDefaultAsync(f => f.CaseTimelineEntryId == entryId && f.UploadFileId == fileId, ct);
        if (link is null) return NotFound();
        // Verify the entry belongs to this client's case
        if (!await db.CaseTimelineEntries.AnyAsync(e => e.Id == entryId && e.CaseId == caseId && e.AuthorAppUserId == userId, ct))
            return Forbid();

        var storagePath = link.UploadFile.StoragePath;
        db.CaseTimelineEntryFiles.Remove(link);
        db.UploadFiles.Remove(link.UploadFile);
        await db.SaveChangesAsync(ct);

        if (storagePath is not null)
            await _fileStorage.DeleteAsync(storagePath, ct);

        return NoContent();
    }

    // ── Case messages ──────────────────────────────────────────────────────────

    /// <summary>Returns all messages for this case and marks org messages as read by the client.</summary>
    [HttpGet("{caseId:guid}/messages")]
    public async Task<ActionResult<IEnumerable<CaseMessageRecord>>> GetMessages(Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var messages = await db.CaseMessages.AsNoTracking()
            .Include(m => m.AuthorAppUser)
            .Where(m => m.CaseId == caseId)
            .OrderBy(m => m.DateCreated)
            .ToListAsync(ct);

        // Mark unread org messages as read now that the client is viewing
        var unread = await db.CaseMessages
            .Where(m => m.CaseId == caseId && m.SenderSide == CaseMessageSide.Organization && !m.IsReadByClient)
            .ToListAsync(ct);
        if (unread.Count > 0)
        {
            unread.ForEach(m => m.IsReadByClient = true);
            await db.SaveChangesAsync(ct);
        }

        return Ok(messages.Select(ToRecord));
    }

    /// <summary>Posts a new message from the client to the org.</summary>
    [HttpPost("{caseId:guid}/messages")]
    public async Task<ActionResult<CaseMessageRecord>> PostMessage(
        Guid caseId, [FromBody] PostCaseMessageRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Body)) return BadRequest("Message body is required.");

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var msg = new Ben.Data.Source.Entities.CaseMessage
        {
            Id                = Guid.NewGuid(),
            CaseId            = caseId,
            AuthorAppUserId   = userId,
            Body              = request.Body.Trim(),
            SenderSide        = CaseMessageSide.Client,
            IsReadByClient    = true,
            IsReadByOrg       = false,
            DateCreated       = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.CaseMessages.Add(msg);
        await db.SaveChangesAsync(ct);

        await db.Entry(msg).Reference(m => m.AuthorAppUser).LoadAsync(ct);
        return Ok(ToRecord(msg));
    }

    private static async Task<bool> IsCaseClient(Ben.Data.Source.Context.BenDataContext db, Guid caseId, Guid userId, CancellationToken ct)
        => await db.Cases.AsNoTracking()
            .Include(c => c.ClientRequest)
            .AnyAsync(c => c.Id == caseId && c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct);

    private static CaseMessageRecord ToRecord(Ben.Data.Source.Entities.CaseMessage m) => new(
        m.Id, m.CaseId, m.AuthorAppUserId,
        m.AuthorAppUser?.DisplayName ?? "Unknown",
        m.Body, m.SenderSide, m.IsReadByClient, m.IsReadByOrg, m.DateCreated);
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
    IReadOnlyList<ClientCaseInvestigation> Investigations,
    int       UnreadMessageCount = 0);

public sealed record ClientCaseOccurrence(
    Guid      Id,
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType,
    DateTime? EventDateTime,
    string?   Title,
    string?   Body,
    DateTime  DateCreated,
    IReadOnlyList<OccurrenceFileItem> Files);

public sealed record OccurrenceFileItem(
    Guid   FileId,
    string FileName,
    string ContentType,
    long   FileSize);

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

public sealed record PostCaseMessageRequest(string Body);

public sealed record CaseMessageRecord(
    Guid   Id,
    Guid   CaseId,
    Guid   AuthorAppUserId,
    string AuthorDisplayName,
    string Body,
    Ben.Data.Common.Enums.CaseMessageSide SenderSide,
    bool   IsReadByClient,
    bool   IsReadByOrg,
    DateTime DateCreated);

public sealed record CaseReportSummary(
    Guid                                   Id,
    Guid                                   CaseId,
    string                                 Title,
    Ben.Data.Common.Enums.CaseReportStatus Status,
    DateTime?                              ExpectedDeliveryDate,
    DateTime?                              PublishedAt,
    DateTime                               DateCreated);

public sealed record AcceptProposalRequest(Guid SlotId);
public sealed record CounterProposalRequest(DateTime PreferredDateTime, string? Notes);
public sealed record DeclineProposalRequest(string? Notes);

public sealed record ScheduleProposalDto(
    Guid                                              Id,
    Guid                                              CaseId,
    Ben.Data.Common.Enums.ScheduleProposalStatus      Status,
    string?                                           Notes,
    Guid?                                             AcceptedSlotId,
    DateTime?                                         ClientCounterDateTime,
    string?                                           ClientResponseNotes,
    DateTime?                                         ClientRespondedAt,
    Guid?                                             InvestigationId,
    DateTime                                          DateCreated,
    IReadOnlyList<SlotDto>                            Slots);

public sealed record SlotDto(Guid Id, DateTime StartDateTime, DateTime? EndDateTime, int SortOrder);
