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
using Microsoft.Extensions.Logging;

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
    private readonly IAuditLogService _auditLog;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MyCaseController> _logger;

    // Fixed Guid for the 'Case Evidence' upload file type seeded by UploadFileTypeSeeder
    private static readonly Guid EvidenceFileTypeId = new("20000000-0000-0000-0000-000000000001");

    public MyCaseController(IDbContextFactory<BenDataContext> db, IMapper mapper,
        IFileStorageService fileStorage, FileMetadataExtractorService metadataExtractor, IAuditLogService auditLog,
        IEmailService emailService, IConfiguration configuration, ILogger<MyCaseController> logger)
    {
        _db = db; _mapper = mapper; _fileStorage = fileStorage; _metadataExtractor = metadataExtractor; _auditLog = auditLog;
        _emailService = emailService; _configuration = configuration; _logger = logger;
    }

    /// <summary>
    /// Returns all active cases the current user can access as a client — either as the
    /// originating (primary) client, or as a secondary co-client via <see cref="CaseClientAccess"/>
    /// (including one accepted through a sub-client invite, item #4). Previously only checked
    /// primary-client ownership, so a co-client's grant was inert for browsing — they could act on
    /// individual occurrences (which already checked <see cref="IsCaseClient"/>) but never actually
    /// see the case here or on its detail page; fixed as a follow-up surfaced while adding invites.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClientCaseListItem>>> GetMyCases(CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);

        var accessibleCaseIds = new HashSet<Guid>();
        accessibleCaseIds.UnionWith(await db.Cases.AsNoTracking()
            .Where(c => c.ClientRequest != null && c.ClientRequest.AppUserId == userId)
            .Select(c => c.Id).ToListAsync(ct));
        accessibleCaseIds.UnionWith(await db.CaseClientAccesses.AsNoTracking()
            .Where(a => a.AppUserId == userId).Select(a => a.CaseId).ToListAsync(ct));

        var cases = await db.Cases.AsNoTracking()
            .Include(c => c.ClientRequest)
            .Include(c => c.CaseManagerAppUser)
            .Where(c => accessibleCaseIds.Contains(c.Id) && c.Status != CaseStatus.Proposed)
            .OrderByDescending(c => c.DateCaseOpened)
            .ToListAsync(ct);

        // Fetch next upcoming investigation per case in a single query
        var caseIds    = cases.Select(c => c.Id).ToList();
        var now        = DateTime.UtcNow;
        var nextInvMap = await db.Investigations.AsNoTracking()
            .Where(i => caseIds.Contains(i.CaseId)
                     && i.ScheduledDateTime >= now
                     && i.Status == InvestigationStatus.Scheduled)
            .GroupBy(i => i.CaseId)
            .Select(g => new { CaseId = g.Key, Next = g.Min(i => i.ScheduledDateTime) })
            .ToDictionaryAsync(x => x.CaseId, x => (DateTime?)x.Next, ct);

        return Ok(cases.Select(c => new ClientCaseListItem(
            CaseId:                  c.Id,
            CaseReference:           $"#{c.CaseYear}-{c.OrgCaseNumber:D3}",
            Title:                   c.Title,
            City:                    c.City,
            State:                   c.State,
            Status:                  c.Status,
            CaseManagerDisplayName:  c.CaseManagerAppUser?.DisplayName,
            DateCaseOpened:          c.DateCaseOpened,
            NextInvestigationDate:   nextInvMap.GetValueOrDefault(c.Id))));
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

        // Same access rule as GetMyCases — primary client OR a secondary co-client grant. See that
        // method's doc comment for why this changed.
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var c = await db.Cases.AsNoTracking()
            .Include(x => x.ClientRequest)
            .Include(x => x.CaseManagerAppUser)
            // The client's own reports, plus anything the org deliberately shared with them.
            // Previously this was "your own reports, plus public Evidence" — which meant an
            // investigator had no way to tell a client anything without publishing it to the world,
            // and notes written *for* the client were invisible to them.
            .Include(x => x.TimelineEntries.Where(e =>
                e.EntryType == CaseTimelineEntryType.ClientReport ||
                e.Visibility >= CaseTimelineVisibility.Client)
                .OrderBy(e => e.EventDateTime ?? e.DateCreated))
                .ThenInclude(e => e.Files)
                .ThenInclude(f => f.UploadFile)
            .FirstOrDefaultAsync(x => x.Id == caseId, ct);

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
            // Now that org-authored entries can reach this list, the client needs to know which
            // ones are theirs. Without it an investigator's note reads as something they wrote.
            FromInvestigators: e.AuthorAppUserId != userId,
            DateCreated:   e.DateCreated,
            Files:         e.Files.Select(f => new OccurrenceFileItem(
                f.UploadFileId, f.UploadFile.FileName, f.UploadFile.ContentType, f.UploadFile.FileSize)).ToList())).ToList();

        // Compute cancellation deadline — requires org's primary address coordinates
        var orgAddr = await db.OrganizationAddresses.AsNoTracking()
            .Where(a => a.OrganizationId == c.OrganizationId && a.Latitude != null && a.Longitude != null)
            .OrderBy(a => a.DateCreated)
            .FirstOrDefaultAsync(ct);

        double distMiles = 0.0;
        if (c.Latitude.HasValue && c.Longitude.HasValue && orgAddr?.Latitude != null && orgAddr.Longitude != null)
            distMiles = HaversineDistanceMiles((double)c.Latitude, (double)c.Longitude, (double)orgAddr.Latitude, (double)orgAddr.Longitude);

        var invItems = investigations.Select(i => new ClientCaseInvestigation(
            Id:                     i.Id,
            Title:                  i.Title,
            ScheduledDateTime:      i.ScheduledDateTime,
            EndDateTime:            i.EndDateTime,
            Location:               i.Location,
            Status:                 i.Status,
            EvidenceDueDate:        i.EvidenceDueDate,
            CancellationDeadlineUtc: i.Status == InvestigationStatus.Scheduled
                ? Ben.Data.Common.Helpers.InvestigationCancellationHelper.CancellationDeadlineUtc(i.ScheduledDateTime, distMiles)
                : null)).ToList();

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
            UnreadMessageCount:      unreadCount,
            IsPrimaryClient:         c.ClientRequest?.AppUserId == userId));
    }

    /// <summary>
    /// Logs a new occurrence (ClientReport timeline entry) on the client's case.
    /// Created OrgOnly; the case manager decides whether to share it further.
    /// </summary>
    [HttpPost("{caseId:guid}/occurrences")]
    public async Task<ActionResult<CaseTimelineEntryRecord>> LogOccurrence(
        Guid caseId, [FromBody] LogOccurrenceRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var entry = new CaseTimelineEntry
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            AuthorAppUserId    = userId,
            EntryType          = CaseTimelineEntryType.ClientReport,
            EventDateTime      = request.EventDateTime,
            Title              = request.Title?.Trim(),
            Body               = request.Body?.Trim(),
            // The client always sees their own reports via the EntryType clause, so OrgOnly here
            // means "not shared onward", not "hidden from its author".
            Visibility         = CaseTimelineVisibility.OrgOnly,
            IpAddress          = HttpContext.Connection.RemoteIpAddress?.ToString(),
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.CaseTimelineEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(CaseTimelineEntry), entry.Id, entry, userId, AppSources.WebApi, ct));

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
        var before = await db.CaseTimelineEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId
                && e.AuthorAppUserId == userId
                && e.EntryType == CaseTimelineEntryType.ClientReport, ct);
        var entry = await db.CaseTimelineEntries
            .Include(e => e.Case).ThenInclude(c => c.ClientRequest)
            .FirstOrDefaultAsync(e => e.Id == entryId && e.CaseId == caseId
                && e.AuthorAppUserId == userId
                && e.EntryType == CaseTimelineEntryType.ClientReport, ct);
        if (entry is null || before is null) return NotFound();
        if (!await IsCaseClient(db, caseId, userId, ct)) return Forbid();

        entry.EventDateTime      = request.EventDateTime;
        entry.Title              = request.Title?.Trim();
        entry.Body               = request.Body?.Trim();
        entry.DateUpdated        = DateTime.UtcNow;
        entry.UpdatedByAppUserId = userId;
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(CaseTimelineEntry), entry.Id, before, entry, userId, AppSources.WebApi, ct));

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
        if (!await IsCaseClient(db, caseId, userId, ct)) return Forbid();

        db.CaseTimelineEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(CaseTimelineEntry), entry.Id, entry, userId, AppSources.WebApi, ct));
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
    [DisableRequestSizeLimit]
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

        var entryFile = new CaseTimelineEntryFile
        {
            Id                   = Guid.NewGuid(),
            CaseTimelineEntryId  = entryId,
            UploadFileId         = uploadFile.Id,
            DateCreated          = DateTime.UtcNow,
            CreatedByAppUserId   = userId,
        };
        db.CaseTimelineEntryFiles.Add(entryFile);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(CaseTimelineEntryFile), entryFile.Id, entryFile, userId, AppSources.WebApi, ct));

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
            catch (Exception ex)
            {
                // Extraction is best-effort — never surface this to the caller — but a silent
                // failure here previously meant a systemic breakage was invisible until someone
                // noticed missing metadata.
                _logger.LogWarning(ex, "Metadata extraction failed for upload file {UploadFileId}", capturedId);
            }
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
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(CaseTimelineEntryFile), link.Id, link, userId, AppSources.WebApi, ct));

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
    {
        // Primary client check
        if (await db.Cases.AsNoTracking()
            .Include(c => c.ClientRequest)
            .AnyAsync(c => c.Id == caseId && c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct))
            return true;
        // Secondary co-client check
        return await db.CaseClientAccesses.AsNoTracking()
            .AnyAsync(a => a.CaseId == caseId && a.AppUserId == userId, ct);
    }

    // ── Co-client management ──────────────────────────────────────────────────

    /// <summary>Lists secondary users the primary client has granted access to this case.</summary>
    [HttpGet("{caseId:guid}/co-clients")]
    public async Task<ActionResult<IEnumerable<CoClientItem>>> GetCoClients(Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);
        // Only the primary client can manage co-clients
        var primaryClient = await db.Cases.AsNoTracking().Include(c => c.ClientRequest)
            .FirstOrDefaultAsync(c => c.Id == caseId && c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct);
        if (primaryClient is null) return Forbid();

        var coClients = await db.CaseClientAccesses.AsNoTracking()
            .Include(a => a.AppUser)
            .Where(a => a.CaseId == caseId)
            .ToListAsync(ct);
        return Ok(coClients.Select(a => new CoClientItem(a.Id, a.AppUserId, a.AppUser.DisplayName ?? a.AppUser.Email!)));
    }

    /// <summary>Primary client grants another registered user access to the case.</summary>
    [HttpPost("{caseId:guid}/co-clients")]
    public async Task<ActionResult<CoClientItem>> AddCoClient(Guid caseId, [FromBody] AddCoClientRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);

        var primaryClient = await db.Cases.AsNoTracking().Include(c => c.ClientRequest)
            .FirstOrDefaultAsync(c => c.Id == caseId && c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct);
        if (primaryClient is null) return Forbid();

        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == request.Email, ct);
        if (target is null) return BadRequest("No account found with that email address.");
        if (target.Id == userId) return BadRequest("You are already the primary client.");
        if (await db.CaseClientAccesses.AnyAsync(a => a.CaseId == caseId && a.AppUserId == target.Id, ct))
            return Conflict("This user already has access.");

        var access = new Ben.Data.Source.Entities.CaseClientAccess
        {
            Id = Guid.NewGuid(), CaseId = caseId, AppUserId = target.Id,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseClientAccesses.Add(access);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(Ben.Data.Source.Entities.CaseClientAccess), access.Id, access, userId, AppSources.WebApi, ct));
        return Ok(new CoClientItem(access.Id, target.Id, target.DisplayName ?? target.Email!));
    }

    /// <summary>Primary client revokes a co-client's access.</summary>
    [HttpDelete("{caseId:guid}/co-clients/{accessId:guid}")]
    public async Task<IActionResult> RemoveCoClient(Guid caseId, Guid accessId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);

        var primaryClient = await db.Cases.AsNoTracking().Include(c => c.ClientRequest)
            .FirstOrDefaultAsync(c => c.Id == caseId && c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct);
        if (primaryClient is null) return Forbid();

        var access = await db.CaseClientAccesses.FirstOrDefaultAsync(a => a.Id == accessId && a.CaseId == caseId, ct);
        if (access is null) return NotFound();
        db.CaseClientAccesses.Remove(access);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(Ben.Data.Source.Entities.CaseClientAccess), access.Id, access, userId, AppSources.WebApi, ct));
        return NoContent();
    }

    // ── Sub-client invites (item #4's remaining piece — email invite for people with no account yet) ─

    /// <summary>Returns this case's pending (not accepted/revoked/expired) invites.</summary>
    [HttpGet("{caseId:guid}/invites")]
    public async Task<ActionResult<IEnumerable<CaseClientInviteRecord>>> GetInvites(Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);

        var primaryClient = await db.Cases.AsNoTracking().Include(c => c.ClientRequest)
            .FirstOrDefaultAsync(c => c.Id == caseId && c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct);
        if (primaryClient is null) return Forbid();

        var now = DateTime.UtcNow;
        var invites = await db.CaseClientInvites.AsNoTracking()
            .Where(i => i.CaseId == caseId && i.DateAccepted == null && i.DateRevoked == null && i.DateExpires > now)
            .OrderByDescending(i => i.DateCreated)
            .ToListAsync(ct);
        return Ok(invites.Select(ToInviteRecord));
    }

    /// <summary>
    /// The single entry point for adding a secondary user to a case: primary client supplies just
    /// an email. An existing account is linked immediately (identical to the old <see cref="AddCoClient"/>
    /// path — kept alongside, untouched, for any other caller); no account yet mints a 14-day,
    /// revocable invite and — if <see cref="IEmailService.IsConfigured"/> — emails it. Either way
    /// the primary client always gets the invite link back, since email delivery is never
    /// guaranteed (unconfigured in every environment today, and a live send can still fail).
    /// </summary>
    [HttpPost("{caseId:guid}/invites")]
    public async Task<ActionResult<InviteCoClientResult>> InviteCoClient(
        Guid caseId, [FromBody] InviteCoClientRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Email)) return BadRequest("Email is required.");
        var email = request.Email.Trim();

        await using var db = await _db.CreateDbContextAsync(ct);
        var primaryClient = await db.Cases.AsNoTracking().Include(c => c.ClientRequest)
            .FirstOrDefaultAsync(c => c.Id == caseId && c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct);
        if (primaryClient is null) return Forbid();

        var target = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);
        if (target is not null)
        {
            if (target.Id == userId) return BadRequest("You are already the primary client.");
            if (await db.CaseClientAccesses.AnyAsync(a => a.CaseId == caseId && a.AppUserId == target.Id, ct))
                return Conflict("This user already has access.");

            var access = new Ben.Data.Source.Entities.CaseClientAccess
            {
                Id = Guid.NewGuid(), CaseId = caseId, AppUserId = target.Id,
                DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
            };
            db.CaseClientAccesses.Add(access);
            await db.SaveChangesAsync(ct);
            _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(Ben.Data.Source.Entities.CaseClientAccess), access.Id, access, userId, AppSources.WebApi, ct));
            return Ok(new InviteCoClientResult(
                LinkedExistingAccount: true,
                CoClient: new CoClientItem(access.Id, target.Id, target.DisplayName ?? target.Email!),
                Invite: null, EmailSent: false));
        }

        // No account yet — revoke any still-pending invite for this exact (case, email) so there's
        // only ever one live token per invitee, then mint a fresh one.
        var priorPending = await db.CaseClientInvites
            .Where(i => i.CaseId == caseId && i.Email == email && i.DateAccepted == null && i.DateRevoked == null)
            .ToListAsync(ct);
        foreach (var prior in priorPending)
        {
            prior.DateRevoked = DateTime.UtcNow;
            prior.UpdatedByAppUserId = userId;
            prior.DateUpdated = DateTime.UtcNow;
        }

        var invite = new Ben.Data.Source.Entities.CaseClientInvite
        {
            Id = Guid.NewGuid(), CaseId = caseId, Email = email,
            Token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
            DateExpires = DateTime.UtcNow.AddDays(14),
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseClientInvites.Add(invite);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(Ben.Data.Source.Entities.CaseClientInvite), invite.Id, invite, userId, AppSources.WebApi, ct));

        var emailSent = false;
        if (_emailService.IsConfigured)
        {
            try
            {
                var inviter = await db.AppUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
                var appBaseUrl = _configuration["AppBaseUrl"]?.TrimEnd('/') ?? string.Empty;
                var inviteLink = $"{appBaseUrl}/invite/{invite.Token}";
                var inviterName = System.Net.WebUtility.HtmlEncode(inviter?.DisplayName ?? "Someone");
                var caseTitle = System.Net.WebUtility.HtmlEncode(primaryClient.Title);
                var subject = $"{inviter?.DisplayName ?? "Someone"} invited you to a case on IsHaunted.com";
                var body = $"<p>{inviterName} has invited you to collaborate on the case " +
                           $"\"<strong>{caseTitle}</strong>\" on IsHaunted.com.</p>" +
                           $"<p><a href=\"{inviteLink}\">Accept invitation</a></p>" +
                           $"<p>This link expires {invite.DateExpires:MMMM d, yyyy}.</p>";
                await _emailService.SendAsync(email, subject, body, ct);
                emailSent = true;
            }
            catch { /* best-effort — the invite still succeeds; the UI falls back to copy-link */ }
        }

        return Ok(new InviteCoClientResult(
            LinkedExistingAccount: false, CoClient: null, Invite: ToInviteRecord(invite), EmailSent: emailSent));
    }

    /// <summary>Primary client revokes a pending invite. Idempotent — revoking twice is a no-op.</summary>
    [HttpDelete("{caseId:guid}/invites/{inviteId:guid}")]
    public async Task<IActionResult> RevokeInvite(Guid caseId, Guid inviteId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);

        var primaryClient = await db.Cases.AsNoTracking().Include(c => c.ClientRequest)
            .FirstOrDefaultAsync(c => c.Id == caseId && c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct);
        if (primaryClient is null) return Forbid();

        var before = await db.CaseClientInvites.AsNoTracking().FirstOrDefaultAsync(i => i.Id == inviteId && i.CaseId == caseId, ct);
        if (before is null) return NotFound();
        if (before.DateRevoked is not null) return NoContent();

        var invite = await db.CaseClientInvites.FirstAsync(i => i.Id == inviteId, ct);
        invite.DateRevoked = DateTime.UtcNow;
        invite.UpdatedByAppUserId = userId;
        invite.DateUpdated = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogUpdateAsync(nameof(Ben.Data.Source.Entities.CaseClientInvite), invite.Id, before, invite, userId, AppSources.WebApi, ct));
        return NoContent();
    }

    private static CaseClientInviteRecord ToInviteRecord(Ben.Data.Source.Entities.CaseClientInvite i) =>
        new(i.Id, i.CaseId, i.Email, i.Token, i.DateExpires, i.DateCreated);

    // ── Related people (basic-info, no account) ─────────────────────────────────

    /// <summary>Returns people referenced on this case who are not platform users.</summary>
    [HttpGet("{caseId:guid}/related-people")]
    public async Task<ActionResult<IEnumerable<CaseRelatedPersonRecord>>> GetRelatedPeople(Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var people = await db.CaseRelatedPeople.AsNoTracking()
            .Where(p => p.CaseId == caseId)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
        return Ok(people.Select(ToRecord));
    }

    /// <summary>Primary client adds a basic-info reference to someone connected to the case (no account created).</summary>
    [HttpPost("{caseId:guid}/related-people")]
    public async Task<ActionResult<CaseRelatedPersonRecord>> AddRelatedPerson(
        Guid caseId, [FromBody] AddRelatedPersonRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("Name is required.");
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);

        var primaryClient = await db.Cases.AsNoTracking().Include(c => c.ClientRequest)
            .FirstOrDefaultAsync(c => c.Id == caseId && c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct);
        if (primaryClient is null) return Forbid();

        var person = new Ben.Data.Source.Entities.CaseRelatedPerson
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            Name               = request.Name.Trim(),
            Age                = request.Age,
            Relationship       = request.Relationship?.Trim(),
            LivesAtProperty    = request.LivesAtProperty,
            Notes              = request.Notes?.Trim(),
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        };
        db.CaseRelatedPeople.Add(person);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogCreateAsync(nameof(Ben.Data.Source.Entities.CaseRelatedPerson), person.Id, person, userId, AppSources.WebApi, ct));
        return Ok(ToRecord(person));
    }

    /// <summary>Primary client removes a related-person reference.</summary>
    [HttpDelete("{caseId:guid}/related-people/{personId:guid}")]
    public async Task<IActionResult> RemoveRelatedPerson(Guid caseId, Guid personId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();
        await using var db = await _db.CreateDbContextAsync(ct);

        var primaryClient = await db.Cases.AsNoTracking().Include(c => c.ClientRequest)
            .FirstOrDefaultAsync(c => c.Id == caseId && c.ClientRequest != null && c.ClientRequest.AppUserId == userId, ct);
        if (primaryClient is null) return Forbid();

        var person = await db.CaseRelatedPeople.FirstOrDefaultAsync(p => p.Id == personId && p.CaseId == caseId, ct);
        if (person is null) return NotFound();
        db.CaseRelatedPeople.Remove(person);
        await db.SaveChangesAsync(ct);
        _ = TryAuditAsync(_auditLog.LogDeleteAsync(nameof(Ben.Data.Source.Entities.CaseRelatedPerson), person.Id, person, userId, AppSources.WebApi, ct));
        return NoContent();
    }

    private static CaseRelatedPersonRecord ToRecord(Ben.Data.Source.Entities.CaseRelatedPerson p) => new()
    {
        Id              = p.Id,
        CaseId          = p.CaseId,
        Name            = p.Name,
        Age             = p.Age,
        Relationship    = p.Relationship,
        LivesAtProperty = p.LivesAtProperty,
        Notes           = p.Notes,
        DateCreated     = p.DateCreated,
    };

    // ── Investigation cancellation (client-initiated, time-gated) ─────────────

    [HttpPost("{caseId:guid}/investigations/{invId:guid}/cancel")]
    public async Task<ActionResult<CancellationResult>> CancelInvestigation(
        Guid caseId, Guid invId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId == Guid.Empty) return Unauthorized();

        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsCaseClient(db, caseId, userId, ct)) return NotFound();

        var investigation = await db.Investigations.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == invId && i.CaseId == caseId, ct);
        if (investigation is null) return NotFound();
        if (investigation.Status != Ben.Data.Common.Enums.InvestigationStatus.Scheduled)
            return Conflict($"Investigation is already {investigation.Status}.");

        // Load coords for distance-based deadline calculation
        var c = await db.Cases.AsNoTracking()
            .Include(x => x.Organization)
            .FirstAsync(x => x.Id == caseId, ct);

        var orgAddr = await db.OrganizationAddresses.AsNoTracking()
            .Where(a => a.OrganizationId == c.OrganizationId && a.Latitude != null && a.Longitude != null)
            .OrderBy(a => a.DateCreated)
            .FirstOrDefaultAsync(ct);

        double distMiles = 0.0;
        if (c.Latitude.HasValue && c.Longitude.HasValue && orgAddr?.Latitude != null && orgAddr.Longitude != null)
            distMiles = HaversineDistanceMiles((double)c.Latitude, (double)c.Longitude, (double)orgAddr.Latitude, (double)orgAddr.Longitude);

        var deadline    = Ben.Data.Common.Helpers.InvestigationCancellationHelper.CancellationDeadlineUtc(investigation.ScheduledDateTime, distMiles);
        var leadHours   = (int)Ben.Data.Common.Helpers.InvestigationCancellationHelper.RequiredLeadHours(distMiles);

        if (!Ben.Data.Common.Helpers.InvestigationCancellationHelper.IsCancellationAllowed(investigation.ScheduledDateTime, distMiles))
            return UnprocessableEntity($"Cancellation window has closed. Cancellations must be made at least {leadHours} hours before the scheduled visit (deadline was {deadline:MMM d, yyyy h:mm tt} UTC).");

        // Apply cancellation and notify case manager
        var inv = await db.Investigations.FirstAsync(i => i.Id == invId, ct);
        inv.Status = Ben.Data.Common.Enums.InvestigationStatus.Cancelled;
        inv.DateUpdated = DateTime.UtcNow;
        inv.UpdatedByAppUserId = userId;

        db.CaseMessages.Add(new Ben.Data.Source.Entities.CaseMessage
        {
            Id = Guid.NewGuid(), CaseId = caseId, AuthorAppUserId = userId,
            Body = $"The client has cancelled the investigation scheduled for <strong>{investigation.ScheduledDateTime.ToLocalTime():MMM d, yyyy h:mm tt}</strong>.",
            SenderSide = Ben.Data.Common.Enums.CaseMessageSide.Client,
            IsReadByClient = true, IsReadByOrg = false,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync(ct);
        return Ok(new CancellationResult(invId, deadline, leadHours));
    }

    // Haversine duplicated in WebApi (WebApi cannot reference Ben.Web.Library)
    private static double HaversineDistanceMiles(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 3958.8;
        var dLat = (lat2 - lat1) * Math.PI / 180.0;
        var dLon = (lon2 - lon1) * Math.PI / 180.0;
        var a    = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

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
    DateTime  DateCaseOpened,
    DateTime? NextInvestigationDate = null);

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
    int       UnreadMessageCount = 0,
    // Item #4 follow-up: the client's own reliable signal for showing primary-only management UI
    // (Shared Access, invites) — MyCaseDetail.razor previously *inferred* this from whether
    // GetCoClients happened not to throw, but the generic HTTP client swallows a 403 as an empty
    // list rather than throwing, so every co-client (old AddCoClient flow or a new invite) saw the
    // primary-only admin controls too. Surfaced only once co-clients could reach this page at all
    // (see the GetMyCase/GetMyCases fix above) — previously unreachable, now a real bug.
    bool      IsPrimaryClient = false);

public sealed record ClientCaseOccurrence(
    Guid      Id,
    Ben.Data.Common.Enums.CaseTimelineEntryType EntryType,
    DateTime? EventDateTime,
    string?   Title,
    string?   Body,
    bool      FromInvestigators,   // true when the org wrote this, false when the client did
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
    DateTime?  EvidenceDueDate = null,
    DateTime?  CancellationDeadlineUtc = null);

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

public sealed record CancellationResult(Guid InvestigationId, DateTime DeadlineUtc, int RequiredLeadHours);

public sealed record CoClientItem(Guid AccessId, Guid AppUserId, string DisplayName);
public sealed record AddCoClientRequest(string Email);

public sealed record InviteCoClientRequest(string Email);

/// <summary>
/// Result of <see cref="MyCaseController.InviteCoClient"/> — exactly one of <see cref="CoClient"/>
/// (existing account, linked immediately) or <see cref="Invite"/> (no account yet) is set.
/// </summary>
public sealed record InviteCoClientResult(bool LinkedExistingAccount, CoClientItem? CoClient, CaseClientInviteRecord? Invite, bool EmailSent);

/// <summary>A pending sub-client invite, as returned to the inviting primary client — includes the
/// raw <see cref="Token"/> so the UI can render a "Copy Link" action.</summary>
public sealed record CaseClientInviteRecord(Guid Id, Guid CaseId, string Email, string Token, DateTime DateExpires, DateTime DateCreated);
