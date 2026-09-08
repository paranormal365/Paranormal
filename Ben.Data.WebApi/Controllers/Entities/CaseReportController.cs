using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Telerik.Windows.Documents.Fixed.FormatProviders.Pdf;
using Telerik.Windows.Documents.Fixed.Model;
using Telerik.Windows.Documents.Fixed.Model.Editing;
using Ben.Data.WebApi.Services.Access;

namespace Ben.Data.WebApi.Controllers.Entities;

[ApiController]
[Route("api/orgs/{orgId:guid}/cases/{caseId:guid}/reports")]
[Authorize]
public sealed class CaseReportController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public CaseReportController(IDbContextFactory<BenDataContext> db,
        Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService security,
        Ben.Data.Common.Interfaces.IFileStorageService fileStorage)
    { _db = db; _security = security; _fileStorage = fileStorage; }

    private readonly Ben.Data.Common.Interfaces.IFileStorageService _fileStorage;

    private Task<IReadOnlyDictionary<Guid, string?>> ReadoutsAsync(CaseReport report, CancellationToken ct)
        => Ben.Data.WebApi.Services.CaseReportReadouts.ForAsync(report.Sections.SelectMany(x => x.FieldSessions), _fileStorage, ct);

    private Task<IReadOnlyDictionary<Guid, string?>> ReadoutsAsync(IEnumerable<CaseReportSectionFieldSession> citations, CancellationToken ct)
        => Ben.Data.WebApi.Services.CaseReportReadouts.ForAsync(citations, _fileStorage, ct);

    private Task<string?> ReadoutAsync(FieldSessionUpload session, CancellationToken ct)
        => Ben.Data.WebApi.Services.CaseReportReadouts.ForAsync(session, _fileStorage, ct);

    private readonly Ben.Service.RepositoryService.GenericInterfaces.IOrganizationSecurityService _security;

    // ── Report CRUD ───────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CaseReportSummary>>> GetAll(Guid orgId, Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Read, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var reports = await db.CaseReports.AsNoTracking()
            .Where(r => r.CaseId == caseId)
            .OrderByDescending(r => r.DateCreated)
            .Select(r => new CaseReportSummary(r.Id, r.CaseId, r.Title, r.Status, r.ExpectedDeliveryDate, r.PublishedAt, r.DateCreated, r.IsPublicSummaryVisible))
            .ToListAsync(ct);
        return Ok(reports);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CaseReportDetail>> GetById(Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Read, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var report = await db.CaseReports.AsNoTracking()
            .Include(r => r.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Files.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.UploadFile)
            .Include(r => r.Sections)
                .ThenInclude(s => s.FieldSessions.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.FieldSessionUpload)
                        .ThenInclude(u => u.DocumentUploadFile)
            .FirstOrDefaultAsync(r => r.Id == id && r.CaseId == caseId, ct);
        return report is null ? NotFound() : Ok(ToDetail(report, await ReadoutsAsync(report, ct)));
    }

    [HttpPost]
    public async Task<ActionResult<CaseReportDetail>> Create(Guid orgId, Guid caseId, [FromBody] UpsertCaseReportRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Create, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var report = new CaseReport
        {
            Id = Guid.NewGuid(), CaseId = caseId, Title = request.Title.Trim(),
            Summary = request.Summary?.Trim(), Conclusion = request.Conclusion?.Trim(),
            ExpectedDeliveryDate = request.ExpectedDeliveryDate,
            Status = CaseReportStatus.Draft,
            DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseReports.Add(report);
        await db.SaveChangesAsync(ct);
        return Ok(ToDetail(report, await ReadoutsAsync(report, ct)));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CaseReportDetail>> Update(Guid orgId, Guid caseId, Guid id, [FromBody] UpsertCaseReportRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var report = await db.CaseReports
            .Include(r => r.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Files.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.UploadFile)
            .Include(r => r.Sections)
                .ThenInclude(s => s.FieldSessions.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.FieldSessionUpload)
                        .ThenInclude(u => u.Files)
            .Include(r => r.Sections)
                .ThenInclude(s => s.FieldSessions)
                    .ThenInclude(f => f.FieldSessionUpload)
                        .ThenInclude(u => u.DocumentUploadFile)
            .FirstOrDefaultAsync(r => r.Id == id && r.CaseId == caseId, ct);
        if (report is null) return NotFound();

        report.Title                = request.Title.Trim();
        report.Summary              = request.Summary?.Trim();
        report.Conclusion           = request.Conclusion?.Trim();
        report.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
        // W-P3: null leaves the group's choice alone, so a caller predating this field cannot
        // silently take a published summary off the group's public page.
        if (request.IsPublicSummaryVisible is { } showPublicly)
            report.IsPublicSummaryVisible = showPublicly;
        report.DateUpdated          = DateTime.UtcNow;
        report.UpdatedByAppUserId   = userId;
        await db.SaveChangesAsync(ct);
        return Ok(ToDetail(report, await ReadoutsAsync(report, ct)));
    }

    /// <summary>
    /// Would this report's summary and conclusion put the client's name or street on the public
    /// case page? (site evaluation 2026-09-06, W-P3; the same check item 176 gave the title.)
    /// </summary>
    /// <remarks>
    /// <para>Advisory, like the title's. A surname is also a place name, and only the group knows
    /// which their prose means — so this warns and lets them publish anyway.</para>
    ///
    /// <para>Server-side for the same reason as the title's: the client's real name deliberately
    /// never reaches the org-facing records, so the check has to run where the name lives and
    /// return only the sentence. It reads the report's stored text rather than taking it as a
    /// parameter — the summary is prose and can be long, and the thing worth checking is what
    /// would actually be published.</para>
    ///
    /// <para>Empty list when the prose is clean or the case has no client: both mean nothing to
    /// warn about.</para>
    /// </remarks>
    [HttpGet("{id:guid}/public-summary-leak-check")]
    public async Task<ActionResult<IReadOnlyList<string>>> PublicSummaryLeakCheck(
        Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Read, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var report = await db.CaseReports.AsNoTracking()
            .Where(r => r.Id == id && r.CaseId == caseId)
            .Select(r => new { r.Summary, r.Conclusion })
            .FirstOrDefaultAsync(ct);
        if (report is null) return NotFound();

        var caseRow = await db.Cases.AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => new { c.ClientRequestId, c.StreetAddress1, c.PublicPseudonym })
            .FirstOrDefaultAsync(ct);
        if (caseRow is null) return NotFound();

        string?[] names = [];
        if (caseRow.ClientRequestId is { } requestId)
        {
            var client = await db.ClientRequests.AsNoTracking()
                .Where(r => r.Id == requestId)
                .Select(r => new { r.AppUser.FirstName, r.AppUser.LastName, r.AppUser.DisplayName })
                .FirstOrDefaultAsync(ct);
            if (client is not null) names = [client.FirstName, client.LastName, client.DisplayName];
        }

        // Tags stripped first — and NOT because markup could hide a name from the check. The
        // word boundary in PublicTitleLeakCheck treats "<" and ">" as separators, so
        // "<strong>Park</strong>" matches perfectly well. What stripping prevents is the
        // opposite: a name living only in an ATTRIBUTE — a link title, an image alt — warning
        // about prose that never shows it to anybody. A warning nobody can act on is how a
        // group learns to click past all of them.
        var prose = $"{StripTags(report.Summary)} {StripTags(report.Conclusion)}";

        // The pseudonym is not re-checked here — the title's own check already covers it, and
        // reporting it twice would put the same sentence in front of somebody twice.
        return Ok(PublicTitleLeakCheck.Check(
            prose, pseudonym: null, names, caseRow.StreetAddress1, subject: "summary"));
    }

    private static string StripTags(string? html)
        => string.IsNullOrWhiteSpace(html)
            ? string.Empty
            : System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");

    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<CaseReportDetail>> Publish(Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var report = await db.CaseReports
            .Include(r => r.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Files.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.UploadFile)
            .Include(r => r.Sections)
                .ThenInclude(s => s.FieldSessions.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.FieldSessionUpload)
                        .ThenInclude(u => u.Files)
            .Include(r => r.Sections)
                .ThenInclude(s => s.FieldSessions)
                    .ThenInclude(f => f.FieldSessionUpload)
                        .ThenInclude(u => u.DocumentUploadFile)
            .FirstOrDefaultAsync(r => r.Id == id && r.CaseId == caseId, ct);
        if (report is null) return NotFound();

        // W-A14: a second publish is a re-publish, and the client is told so in those words. The
        // endpoint always allowed this; nothing in the editor ever offered it, so a group that
        // corrected a published report had no way to say they had. The message is the whole
        // point — a client who was sent a document is entitled to know it changed.
        var isRepublish = report.Status == CaseReportStatus.Published && report.PublishedAt is not null;

        report.Status               = CaseReportStatus.Published;
        report.PublishedAt          = DateTime.UtcNow;
        report.PublishedByAppUserId = userId;
        report.DateUpdated          = DateTime.UtcNow;
        report.UpdatedByAppUserId   = userId;

        // Both saves must land together — otherwise a failure between them leaves the report
        // Published with no client notification. The in-memory provider used by tests doesn't
        // support transactions, so skip it there rather than fail every test.
        var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct)
            : null;
        await using var _ = transaction;
        await db.SaveChangesAsync(ct);

        // Notify client via case message so unread badge and message panel both update
        db.CaseMessages.Add(new Ben.Data.Source.Entities.CaseMessage
        {
            Id                 = Guid.NewGuid(),
            CaseId             = caseId,
            AuthorAppUserId    = userId,
            Body               = isRepublish
                ? $"Your investigation report has been updated: {report.Title}. The version on your case page is the current one."
                : $"Your investigation report has been published: {report.Title}. You can view and download it from your case page.",
            SenderSide         = Ben.Data.Common.Enums.CaseMessageSide.Organization,
            IsReadByClient     = false,
            IsReadByOrg        = true,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return Ok(ToDetail(report, await ReadoutsAsync(report, ct)));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Delete, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var report = await db.CaseReports.FirstOrDefaultAsync(r => r.Id == id && r.CaseId == caseId, ct);
        if (report is null) return NotFound();
        if (report.Status == CaseReportStatus.Published) return Conflict("Published reports cannot be deleted.");

        db.CaseReports.Remove(report);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Section CRUD ──────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/sections")]
    public async Task<ActionResult<CaseReportSectionDto>> AddSection(Guid orgId, Guid caseId, Guid id, [FromBody] UpsertSectionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        if (!await db.CaseReports.AnyAsync(r => r.Id == id && r.CaseId == caseId, ct)) return NotFound();

        var maxOrder = await db.CaseReportSections.Where(s => s.CaseReportId == id).MaxAsync(s => (int?)s.SortOrder, ct) ?? 0;
        var section  = new CaseReportSection
        {
            Id = Guid.NewGuid(), CaseReportId = id, SortOrder = maxOrder + 10,
            Title = request.Title.Trim(), Body = request.Body?.Trim(),
            SectionType = request.SectionType, DateCreated = DateTime.UtcNow, CreatedByAppUserId = userId,
        };
        db.CaseReportSections.Add(section);
        await db.SaveChangesAsync(ct);
        await TouchReportAsync(db, id, ct);
        return Ok(new CaseReportSectionDto(section.Id, section.CaseReportId, section.SortOrder, section.Title, section.Body, section.SectionType, [], []));
    }

    [HttpPut("{id:guid}/sections/{sectionId:guid}")]
    public async Task<ActionResult<CaseReportSectionDto>> UpdateSection(Guid orgId, Guid caseId, Guid id, Guid sectionId, [FromBody] UpsertSectionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var section = await db.CaseReportSections
            .Include(s => s.Files).ThenInclude(f => f.UploadFile)
            .Include(s => s.FieldSessions).ThenInclude(f => f.FieldSessionUpload).ThenInclude(u => u.DocumentUploadFile)
            .FirstOrDefaultAsync(s => s.Id == sectionId && s.CaseReportId == id, ct);
        if (section is null) return NotFound();

        section.Title = request.Title.Trim(); section.Body = request.Body?.Trim(); section.SectionType = request.SectionType;
        await db.SaveChangesAsync(ct);
        await TouchReportAsync(db, id, ct);
        return Ok(ToSectionDto(section, await ReadoutsAsync(section.FieldSessions, ct)));
    }

    [HttpDelete("{id:guid}/sections/{sectionId:guid}")]
    public async Task<IActionResult> DeleteSection(Guid orgId, Guid caseId, Guid id, Guid sectionId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var section = await db.CaseReportSections.FirstOrDefaultAsync(s => s.Id == sectionId && s.CaseReportId == id, ct);
        if (section is null) return NotFound();
        db.CaseReportSections.Remove(section);
        await db.SaveChangesAsync(ct);
        await TouchReportAsync(db, id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/sections/{sectionId:guid}/files")]
    public async Task<ActionResult<CaseReportSectionFileDto>> AddSectionFile(Guid orgId, Guid caseId, Guid id, Guid sectionId, [FromBody] AddSectionFileRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        if (!await db.CaseReportSections.AnyAsync(s => s.Id == sectionId && s.CaseReportId == id, ct)) return NotFound();
        var file = await db.UploadFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == request.UploadFileId, ct);
        if (file is null) return BadRequest("File not found.");

        var maxOrder = await db.CaseReportSectionFiles.Where(f => f.CaseReportSectionId == sectionId).MaxAsync(f => (int?)f.SortOrder, ct) ?? 0;
        var link = new CaseReportSectionFile
        {
            Id = Guid.NewGuid(), CaseReportSectionId = sectionId, UploadFileId = request.UploadFileId,
            Caption = request.Caption?.Trim(), SortOrder = maxOrder + 10,
        };
        db.CaseReportSectionFiles.Add(link);
        await db.SaveChangesAsync(ct);
        await TouchReportAsync(db, id, ct);
        return Ok(new CaseReportSectionFileDto(link.Id, file.Id, file.FileName, file.ContentType, file.FileSize, link.Caption, link.SortOrder));
    }

    [HttpDelete("{id:guid}/sections/{sectionId:guid}/files/{fileId:guid}")]
    public async Task<IActionResult> RemoveSectionFile(Guid orgId, Guid caseId, Guid id, Guid sectionId, Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        var link = await db.CaseReportSectionFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.CaseReportSectionId == sectionId, ct);
        if (link is null) return NotFound();
        db.CaseReportSectionFiles.Remove(link);
        await db.SaveChangesAsync(ct);
        await TouchReportAsync(db, id, ct);
        return NoContent();
    }

    // ── Field sessions cited by a section ─────────────────────────────────────

    /// <summary>
    /// The field sessions this case could cite.
    /// </summary>
    /// <remarks>
    /// A session reaches a case through its investigation — that is the only tie there is, since
    /// a session is recorded against an investigation and an investigation belongs to a case (or
    /// to nothing at all, in which case it is nobody's report to write). Sessions uploaded
    /// against another org's investigation are not here to be cited, and the org check above is
    /// what keeps it that way.
    /// </remarks>
    [HttpGet("field-sessions")]
    public async Task<ActionResult<IEnumerable<AvailableFieldSessionDto>>> GetAvailableFieldSessions(
        Guid orgId, Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Read, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var sessions = await db.FieldSessionUploads.AsNoTracking()
            .Where(f => f.InvestigationId != null
                     && f.Investigation!.CaseId == caseId
                     && f.Investigation.OrganizationId == orgId)
            .OrderByDescending(f => f.StartedAt)
            .Select(f => new AvailableFieldSessionDto(
                f.Id, f.InvestigationId, f.Investigation!.Title,
                f.LocationLabel, f.RecordedByName, f.DeviceModel,
                f.StartedAt, f.EndedAt, f.ReadingCount, f.MarkerCount, f.Files.Count))
            .ToListAsync(ct);
        return Ok(sessions);
    }

    [HttpPost("{id:guid}/sections/{sectionId:guid}/field-sessions")]
    public async Task<ActionResult<CaseReportSectionFieldSessionDto>> AddSectionFieldSession(
        Guid orgId, Guid caseId, Guid id, Guid sectionId,
        [FromBody] AddSectionFieldSessionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        if (!await db.CaseReportSections.AnyAsync(s => s.Id == sectionId && s.CaseReportId == id, ct)) return NotFound();

        // The session has to belong to THIS case, through its investigation. Without this a
        // report could cite a night recorded for somebody else entirely, and the PDF would
        // present it as this case's evidence.
        var session = await db.FieldSessionUploads.AsNoTracking()
            .Include(f => f.Files)
            .Include(f => f.DocumentUploadFile)
            .FirstOrDefaultAsync(f => f.Id == request.FieldSessionUploadId
                                   && f.InvestigationId != null
                                   && f.Investigation!.CaseId == caseId
                                   && f.Investigation.OrganizationId == orgId, ct);
        if (session is null) return BadRequest("That field session doesn't belong to this case.");

        var existing = await db.CaseReportSectionFieldSessions
            .FirstOrDefaultAsync(f => f.CaseReportSectionId == sectionId
                                   && f.FieldSessionUploadId == request.FieldSessionUploadId, ct);
        if (existing is not null)
        {
            existing.Caption = request.Caption?.Trim();
            await db.SaveChangesAsync(ct);
            await TouchReportAsync(db, id, ct);
            return Ok(new CaseReportSectionFieldSessionDto(
                existing.Id, session.Id, session.LocationLabel, session.RecordedByName,
                session.StartedAt, session.EndedAt, session.ReadingCount, session.MarkerCount,
                session.Files.Count, existing.Caption, existing.SortOrder, await ReadoutAsync(session, ct)));
        }

        var maxOrder = await db.CaseReportSectionFieldSessions
            .Where(f => f.CaseReportSectionId == sectionId)
            .MaxAsync(f => (int?)f.SortOrder, ct) ?? 0;
        var link = new CaseReportSectionFieldSession
        {
            Id = Guid.NewGuid(), CaseReportSectionId = sectionId,
            FieldSessionUploadId = session.Id, Caption = request.Caption?.Trim(),
            SortOrder = maxOrder + 10,
        };
        db.CaseReportSectionFieldSessions.Add(link);
        await db.SaveChangesAsync(ct);
        await TouchReportAsync(db, id, ct);

        return Ok(new CaseReportSectionFieldSessionDto(
            link.Id, session.Id, session.LocationLabel, session.RecordedByName,
            session.StartedAt, session.EndedAt, session.ReadingCount, session.MarkerCount,
            session.Files.Count, link.Caption, link.SortOrder, await ReadoutAsync(session, ct)));
    }

    [HttpDelete("{id:guid}/sections/{sectionId:guid}/field-sessions/{linkId:guid}")]
    public async Task<IActionResult> RemoveSectionFieldSession(
        Guid orgId, Guid caseId, Guid id, Guid sectionId, Guid linkId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Update, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var link = await db.CaseReportSectionFieldSessions
            .FirstOrDefaultAsync(f => f.Id == linkId && f.CaseReportSectionId == sectionId, ct);
        if (link is null) return NotFound();
        // Removes the CITATION only. The session, its document and its recordings are untouched.
        db.CaseReportSectionFieldSessions.Remove(link);
        await db.SaveChangesAsync(ct);
        await TouchReportAsync(db, id, ct);
        return NoContent();
    }

    // ── PDF export ────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> ExportPdf(Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await MayAsync(orgId, OrganizationSecurityAction.Read, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var report = await db.CaseReports.AsNoTracking()
            .Include(r => r.Case)
            .Include(r => r.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Files.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.UploadFile)
            .Include(r => r.Sections)
                .ThenInclude(s => s.FieldSessions.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.FieldSessionUpload)
                        .ThenInclude(u => u.Files)
            .Include(r => r.Sections)
                .ThenInclude(s => s.FieldSessions)
                    .ThenInclude(f => f.FieldSessionUpload)
                        .ThenInclude(u => u.DocumentUploadFile)
            .FirstOrDefaultAsync(r => r.Id == id && r.CaseId == caseId, ct);
        if (report is null) return NotFound();

        var pdfBytes = GeneratePdf(report, await ReadoutsAsync(report, ct));
        var fileName = $"report-{report.Title.Replace(' ', '-')}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    // ── Client-visible report (published only) ────────────────────────────────

    [HttpGet("client/{caseId2:guid}")]
    [AllowAnonymous] // access controlled by IsPublished check below
    public async Task<ActionResult<IEnumerable<CaseReportSummary>>> GetClientReports(Guid orgId, Guid caseId, Guid caseId2, CancellationToken ct)
    {
        // Route alias — caseId2 is unused; both route params point to the same case
        await using var db = await _db.CreateDbContextAsync(ct);
        var reports = await db.CaseReports.AsNoTracking()
            .Where(r => r.CaseId == caseId && r.Status == CaseReportStatus.Published)
            .OrderByDescending(r => r.PublishedAt)
            .Select(r => new CaseReportSummary(r.Id, r.CaseId, r.Title, r.Status, r.ExpectedDeliveryDate, r.PublishedAt, r.DateCreated))
            .ToListAsync(ct);
        return Ok(reports);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Item 156 Phase D: bare membership stopped being the rule here — see CaseFileController.
    /// <summary>Whether the caller may take <paramref name="action"/> on this group's cases.</summary>
    /// <remarks>
    /// <para><b>Was <c>IsOrgMember</c>, and asked <c>Case.Read</c> for all sixteen endpoints</b> —
    /// including <see cref="Publish"/>, which is what puts a report in front of the client, and
    /// <see cref="Delete"/>. Anyone who could read a case could write the group's findings, publish
    /// them to the client under the group's name, and delete published reports.</para>
    ///
    /// <para>Found sweeping the client-facing surfaces on Ben's prompt, 2026-08-26. The name is
    /// most of why it survived review: a helper called "is org member" reads as a belonging check,
    /// so nobody asked what it permitted. It is named for the question it answers now.</para>
    ///
    /// <para><b>Publishing is Update, not Create.</b> It changes an existing report's state rather
    /// than making a new one, and there is no separate publish action to grant. A member who may
    /// only read the case now sees reports and cannot write, publish or delete them.</para>
    /// </remarks>
    private Task<bool> MayAsync(Guid orgId, OrganizationSecurityAction action, CancellationToken ct)
        => User.IsInRole(Ben.Data.Common.Constants.RoleNames.SuperAdmin)
            ? Task.FromResult(true)
            : _security.MayAsync(GetCurrentUserId(), orgId, OrganizationPermissionArea.Cases, action, ct);

    /// <summary>
    /// Marks the report itself as changed, whatever part of it was actually edited.
    /// </summary>
    /// <remarks>
    /// <para>W-A14 of the 2026-09-06 evaluation: a published report stayed fully editable and
    /// nothing said so. The client had been sent a document and was reading a live PDF, so every
    /// later edit reached them silently — no notice on the case board, no new date, no record of
    /// what they had actually been given.</para>
    ///
    /// <para>Only the report row's own Update touched DateUpdated before this. A report IS its
    /// sections, and rewriting a section is the ordinary edit — so an "edited since published"
    /// flag built on the old behaviour would have been blind to almost every case it exists for.
    /// Every mutating endpoint here calls this, including the ones that change a section's files
    /// or its cited field sessions.</para>
    /// </remarks>
    private async Task TouchReportAsync(BenDataContext db, Guid reportId, CancellationToken ct)
    {
        var report = await db.CaseReports.FirstOrDefaultAsync(r => r.Id == reportId, ct);
        if (report is null) return;
        report.DateUpdated        = DateTime.UtcNow;
        report.UpdatedByAppUserId = GetCurrentUserId();
        await db.SaveChangesAsync(ct);
    }

    private static CaseReportDetail ToDetail(CaseReport r, IReadOnlyDictionary<Guid, string?> readouts) => new(
        r.Id, r.CaseId, r.Title, r.Summary, r.Conclusion, r.Status,
        r.ExpectedDeliveryDate, r.PublishedAt, r.DateCreated,
        r.Sections.OrderBy(s => s.SortOrder).Select(s => ToSectionDto(s, readouts)).ToList(),
        r.IsPublicSummaryVisible,
        r.DateUpdated);

    private static CaseReportSectionDto ToSectionDto(CaseReportSection s, IReadOnlyDictionary<Guid, string?> readouts) => new(
        s.Id, s.CaseReportId, s.SortOrder, s.Title, s.Body, s.SectionType,
        s.Files.OrderBy(f => f.SortOrder)
               .Select(f => new CaseReportSectionFileDto(f.Id, f.UploadFileId, f.UploadFile.FileName, f.UploadFile.ContentType, f.UploadFile.FileSize, f.Caption, f.SortOrder))
               .ToList(),
        s.FieldSessions.OrderBy(f => f.SortOrder)
               .Select(f => ToSectionFieldSessionDto(f, readouts.GetValueOrDefault(f.FieldSessionUploadId)))
               .ToList());

    private static CaseReportSectionFieldSessionDto ToSectionFieldSessionDto(CaseReportSectionFieldSession f, string? readout) => new(
        f.Id, f.FieldSessionUploadId,
        f.FieldSessionUpload.LocationLabel,
        f.FieldSessionUpload.RecordedByName,
        f.FieldSessionUpload.StartedAt, f.FieldSessionUpload.EndedAt,
        f.FieldSessionUpload.ReadingCount, f.FieldSessionUpload.MarkerCount,
        f.FieldSessionUpload.Files.Count,
        f.Caption, f.SortOrder, readout);

    private static byte[] GeneratePdf(CaseReport report, IReadOnlyDictionary<Guid, string?> readouts)
        => Ben.Data.WebApi.Services.CaseReportPdfGenerator.Generate(report, readouts);

    private static string StripHtml(string html)
        => System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
}

// ── Request / Response records ────────────────────────────────────────────────

/// <param name="IsPublicSummaryVisible">
/// Show this report's summary and conclusion on the public case page (W-P3). Null means "leave
/// as-is", so a caller that predates the field cannot switch a group's choice off.
/// </param>
public sealed record UpsertCaseReportRequest(
    string    Title,
    string?   Summary,
    string?   Conclusion,
    DateTime? ExpectedDeliveryDate,
    bool?     IsPublicSummaryVisible = null);

public sealed record UpsertSectionRequest(
    string                              Title,
    string?                             Body,
    Ben.Data.Common.Enums.CaseReportSectionType SectionType);

public sealed record AddSectionFileRequest(Guid UploadFileId, string? Caption);

public sealed record AddSectionFieldSessionRequest(Guid FieldSessionUploadId, string? Caption);

/// <summary>A field session a report section could cite, as the picker lists it.</summary>
public sealed record AvailableFieldSessionDto(
    Guid      Id,
    Guid?     InvestigationId,
    string?   InvestigationTitle,
    string?   LocationLabel,
    string?   RecordedByName,
    string    DeviceModel,
    DateTime  StartedAt,
    DateTime? EndedAt,
    int       ReadingCount,
    int       MarkerCount,
    int       FileCount);

/// <summary>A field session as it appears inside a report section.</summary>
public sealed record CaseReportSectionFieldSessionDto(
    Guid      Id,
    Guid      FieldSessionUploadId,
    string?   LocationLabel,
    string?   RecordedByName,
    DateTime  StartedAt,
    DateTime? EndedAt,
    int       ReadingCount,
    int       MarkerCount,
    int       FileCount,
    string?   Caption,
    int       SortOrder,
    string?   Readout);

public sealed record CaseReportSummary(
    Guid                                  Id,
    Guid                                  CaseId,
    string                                Title,
    Ben.Data.Common.Enums.CaseReportStatus Status,
    DateTime?                             ExpectedDeliveryDate,
    DateTime?                             PublishedAt,
    DateTime                              DateCreated,
    bool                                  IsPublicSummaryVisible = false);

/// <param name="IsPublicSummaryVisible">
/// The group has switched this report's summary onto the public case page (W-P3).
/// </param>
/// <param name="DateUpdated">
/// When the report was last changed in any way, including its sections.
/// <para>W-A14: paired with <c>PublishedAt</c>, this is how the editor knows a published report
/// has been edited since the client was sent it. Every mutating endpoint on this controller
/// touches it — see <c>TouchReportAsync</c> — because a report is its sections, and a report
/// whose only change was a rewritten section is exactly the case that matters.</para>
/// </param>
public sealed record CaseReportDetail(
    Guid                                  Id,
    Guid                                  CaseId,
    string                                Title,
    string?                               Summary,
    string?                               Conclusion,
    Ben.Data.Common.Enums.CaseReportStatus Status,
    DateTime?                             ExpectedDeliveryDate,
    DateTime?                             PublishedAt,
    DateTime                              DateCreated,
    IReadOnlyList<CaseReportSectionDto>   Sections,
    bool                                  IsPublicSummaryVisible = false,
    DateTime?                             DateUpdated = null);

public sealed record CaseReportSectionDto(
    Guid                                       Id,
    Guid                                       CaseReportId,
    int                                        SortOrder,
    string                                     Title,
    string?                                    Body,
    Ben.Data.Common.Enums.CaseReportSectionType SectionType,
    IReadOnlyList<CaseReportSectionFileDto>    Files,
    IReadOnlyList<CaseReportSectionFieldSessionDto> FieldSessions);

public sealed record CaseReportSectionFileDto(
    Guid    Id,
    Guid    UploadFileId,
    string  FileName,
    string  ContentType,
    long    FileSize,
    string? Caption,
    int     SortOrder);
