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

namespace Ben.Data.WebApi.Controllers.Entities;

[ApiController]
[Route("api/orgs/{orgId:guid}/cases/{caseId:guid}/reports")]
[Authorize]
public sealed class CaseReportController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _db;

    public CaseReportController(IDbContextFactory<BenDataContext> db) => _db = db;

    // ── Report CRUD ───────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IEnumerable<CaseReportSummary>>> GetAll(Guid orgId, Guid caseId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var reports = await db.CaseReports.AsNoTracking()
            .Where(r => r.CaseId == caseId)
            .OrderByDescending(r => r.DateCreated)
            .Select(r => new CaseReportSummary(r.Id, r.CaseId, r.Title, r.Status, r.ExpectedDeliveryDate, r.PublishedAt, r.DateCreated))
            .ToListAsync(ct);
        return Ok(reports);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CaseReportDetail>> GetById(Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var report = await db.CaseReports.AsNoTracking()
            .Include(r => r.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Files.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.UploadFile)
            .FirstOrDefaultAsync(r => r.Id == id && r.CaseId == caseId, ct);
        return report is null ? NotFound() : Ok(ToDetail(report));
    }

    [HttpPost]
    public async Task<ActionResult<CaseReportDetail>> Create(Guid orgId, Guid caseId, [FromBody] UpsertCaseReportRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
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
        return Ok(ToDetail(report));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CaseReportDetail>> Update(Guid orgId, Guid caseId, Guid id, [FromBody] UpsertCaseReportRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var report = await db.CaseReports
            .Include(r => r.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Files.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.UploadFile)
            .FirstOrDefaultAsync(r => r.Id == id && r.CaseId == caseId, ct);
        if (report is null) return NotFound();

        report.Title                = request.Title.Trim();
        report.Summary              = request.Summary?.Trim();
        report.Conclusion           = request.Conclusion?.Trim();
        report.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
        report.DateUpdated          = DateTime.UtcNow;
        report.UpdatedByAppUserId   = userId;
        await db.SaveChangesAsync(ct);
        return Ok(ToDetail(report));
    }

    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<CaseReportDetail>> Publish(Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var report = await db.CaseReports
            .Include(r => r.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Files.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.UploadFile)
            .FirstOrDefaultAsync(r => r.Id == id && r.CaseId == caseId, ct);
        if (report is null) return NotFound();

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
            Body               = $"Your investigation report has been published: <strong>{report.Title}</strong>. You can view and download it from your case page.",
            SenderSide         = Ben.Data.Common.Enums.CaseMessageSide.Organization,
            IsReadByClient     = false,
            IsReadByOrg        = true,
            DateCreated        = DateTime.UtcNow,
            CreatedByAppUserId = userId,
        });
        await db.SaveChangesAsync(ct);
        if (transaction is not null)
            await transaction.CommitAsync(ct);
        return Ok(ToDetail(report));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
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
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
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
        return Ok(new CaseReportSectionDto(section.Id, section.CaseReportId, section.SortOrder, section.Title, section.Body, section.SectionType, []));
    }

    [HttpPut("{id:guid}/sections/{sectionId:guid}")]
    public async Task<ActionResult<CaseReportSectionDto>> UpdateSection(Guid orgId, Guid caseId, Guid id, Guid sectionId, [FromBody] UpsertSectionRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var section = await db.CaseReportSections
            .Include(s => s.Files).ThenInclude(f => f.UploadFile)
            .FirstOrDefaultAsync(s => s.Id == sectionId && s.CaseReportId == id, ct);
        if (section is null) return NotFound();

        section.Title = request.Title.Trim(); section.Body = request.Body?.Trim(); section.SectionType = request.SectionType;
        await db.SaveChangesAsync(ct);
        return Ok(ToSectionDto(section));
    }

    [HttpDelete("{id:guid}/sections/{sectionId:guid}")]
    public async Task<IActionResult> DeleteSection(Guid orgId, Guid caseId, Guid id, Guid sectionId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var section = await db.CaseReportSections.FirstOrDefaultAsync(s => s.Id == sectionId && s.CaseReportId == id, ct);
        if (section is null) return NotFound();
        db.CaseReportSections.Remove(section);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/sections/{sectionId:guid}/files")]
    public async Task<ActionResult<CaseReportSectionFileDto>> AddSectionFile(Guid orgId, Guid caseId, Guid id, Guid sectionId, [FromBody] AddSectionFileRequest request, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
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
        return Ok(new CaseReportSectionFileDto(link.Id, file.Id, file.FileName, file.ContentType, file.FileSize, link.Caption, link.SortOrder));
    }

    [HttpDelete("{id:guid}/sections/{sectionId:guid}/files/{fileId:guid}")]
    public async Task<IActionResult> RemoveSectionFile(Guid orgId, Guid caseId, Guid id, Guid sectionId, Guid fileId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();
        var link = await db.CaseReportSectionFiles.FirstOrDefaultAsync(f => f.Id == fileId && f.CaseReportSectionId == sectionId, ct);
        if (link is null) return NotFound();
        db.CaseReportSectionFiles.Remove(link);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── PDF export ────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> ExportPdf(Guid orgId, Guid caseId, Guid id, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        await using var db = await _db.CreateDbContextAsync(ct);
        if (!await IsOrgMember(db, orgId, userId, ct)) return Forbid();
        if (!await CaseOrgAccess.CaseBelongsToOrgAsync(db, caseId, orgId, ct)) return NotFound();

        var report = await db.CaseReports.AsNoTracking()
            .Include(r => r.Case)
            .Include(r => r.Sections.OrderBy(s => s.SortOrder))
                .ThenInclude(s => s.Files.OrderBy(f => f.SortOrder))
                    .ThenInclude(f => f.UploadFile)
            .FirstOrDefaultAsync(r => r.Id == id && r.CaseId == caseId, ct);
        if (report is null) return NotFound();

        var pdfBytes = GeneratePdf(report);
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

    private static async Task<bool> IsOrgMember(BenDataContext db, Guid orgId, Guid userId, CancellationToken ct)
        => await db.OrganizationUserMemberships.AsNoTracking()
            .AnyAsync(m => m.OrganizationId == orgId && m.AppUserId == userId && m.IsActive, ct);

    private static CaseReportDetail ToDetail(CaseReport r) => new(
        r.Id, r.CaseId, r.Title, r.Summary, r.Conclusion, r.Status,
        r.ExpectedDeliveryDate, r.PublishedAt, r.DateCreated,
        r.Sections.OrderBy(s => s.SortOrder).Select(ToSectionDto).ToList());

    private static CaseReportSectionDto ToSectionDto(CaseReportSection s) => new(
        s.Id, s.CaseReportId, s.SortOrder, s.Title, s.Body, s.SectionType,
        s.Files.OrderBy(f => f.SortOrder)
               .Select(f => new CaseReportSectionFileDto(f.Id, f.UploadFileId, f.UploadFile.FileName, f.UploadFile.ContentType, f.UploadFile.FileSize, f.Caption, f.SortOrder))
               .ToList());

    private static byte[] GeneratePdf(CaseReport report)
        => Ben.Data.WebApi.Services.CaseReportPdfGenerator.Generate(report);

    private static string StripHtml(string html)
        => System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ").Trim();
}

// ── Request / Response records ────────────────────────────────────────────────

public sealed record UpsertCaseReportRequest(
    string    Title,
    string?   Summary,
    string?   Conclusion,
    DateTime? ExpectedDeliveryDate);

public sealed record UpsertSectionRequest(
    string                              Title,
    string?                             Body,
    Ben.Data.Common.Enums.CaseReportSectionType SectionType);

public sealed record AddSectionFileRequest(Guid UploadFileId, string? Caption);

public sealed record CaseReportSummary(
    Guid                                  Id,
    Guid                                  CaseId,
    string                                Title,
    Ben.Data.Common.Enums.CaseReportStatus Status,
    DateTime?                             ExpectedDeliveryDate,
    DateTime?                             PublishedAt,
    DateTime                              DateCreated);

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
    IReadOnlyList<CaseReportSectionDto>   Sections);

public sealed record CaseReportSectionDto(
    Guid                                       Id,
    Guid                                       CaseReportId,
    int                                        SortOrder,
    string                                     Title,
    string?                                    Body,
    Ben.Data.Common.Enums.CaseReportSectionType SectionType,
    IReadOnlyList<CaseReportSectionFileDto>    Files);

public sealed record CaseReportSectionFileDto(
    Guid    Id,
    Guid    UploadFileId,
    string  FileName,
    string  ContentType,
    long    FileSize,
    string? Caption,
    int     SortOrder);
