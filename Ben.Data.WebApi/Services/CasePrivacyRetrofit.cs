using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services;

// The result records live in Ben.Service.Models so the web client sees the same shape.

/// <summary>
/// Applies a case's privacy protections after the fact (item 182) — for a group that took the case
/// on a plan without them and has since upgraded.
/// </summary>
/// <remarks>
/// <para><b>What it does mechanically:</b> makes the case private, drops exact coordinates from
/// what publishes, and generates the stripped copies for every file on the case that lacks one —
/// the originals were kept untouched precisely so this is possible later.</para>
///
/// <para><b>What it refuses to do silently:</b> rewrite prose. A client's name typed into a report
/// body, a timeline entry or a case title is found and REPORTED, never replaced — an investigator's
/// account of a night is theirs, and a find-and-replace through it can change meaning, break
/// quotations, or mangle a sentence in ways nobody reviews. The pseudonym machinery already covers
/// every place the platform itself writes a name; this covers the places a person did.</para>
///
/// <para><b>What it cannot do at all:</b> undo publication. If the case was public, the report says
/// so, because a group that upgrades should not be left believing the exposure was erased.</para>
/// </remarks>
public sealed class CasePrivacyRetrofit
{
    private readonly IFileStorageService _fileStorage;
    private readonly IMediaSanitizationService _sanitizer;
    private readonly IAvMetadataStripper _avStripper;
    private readonly ILogger<CasePrivacyRetrofit> _logger;

    public CasePrivacyRetrofit(
        IFileStorageService fileStorage, IMediaSanitizationService sanitizer,
        IAvMetadataStripper avStripper, ILogger<CasePrivacyRetrofit> logger)
    {
        _fileStorage = fileStorage;
        _sanitizer   = sanitizer;
        _avStripper  = avStripper;
        _logger      = logger;
    }

    public async Task<CasePrivacyRetrofitResult?> ApplyAsync(
        BenDataContext db, Guid orgId, Guid caseId, Guid actingUserId, CancellationToken ct)
    {
        var caseRow = await db.Cases
            .FirstOrDefaultAsync(c => c.Id == caseId && c.OrganizationId == orgId, ct);
        if (caseRow is null) return null;

        var wasPublic = caseRow.IsPublic;

        // ── 1. Private, and staying that way ──────────────────────────────────
        var madePrivate = caseRow.IsPublic;
        if (caseRow.IsPublic)
        {
            caseRow.IsPublic          = false;
            caseRow.DateUpdated       = DateTime.UtcNow;
            caseRow.UpdatedByAppUserId = actingUserId;
        }

        // ── 2. The exact spot ─────────────────────────────────────────────────
        // Public surfaces already generalize what they draw, but the exact pair sitting on the row
        // is what a future feature would reach for by accident. Blanking it is the durable fix, and
        // the address fields still carry the truth for the group's own use.
        var locationGeneralized = caseRow.Latitude is not null || caseRow.Longitude is not null;
        if (locationGeneralized)
        {
            caseRow.Latitude  = null;
            caseRow.Longitude = null;
        }

        // ── 3. Every file on the case gets its stripped copy ──────────────────
        var fileIds = await CaseFileIdsAsync(db, caseId, ct);
        var files = await db.UploadFiles
            .Where(f => fileIds.Contains(f.Id) && f.StoragePath != null)
            .ToListAsync(ct);

        int stripped = 0, already = 0, unstrippable = 0;
        foreach (var file in files)
        {
            var outcome = await EnsureStrippedCopyAsync(file, ct);
            switch (outcome)
            {
                case StripOutcome.Created:   stripped++;     break;
                case StripOutcome.Existed:   already++;      break;
                default:                     unstrippable++; break;
            }
        }

        // ── 4. Prose that names the client — found, not rewritten ─────────────
        var occurrences = await FindClientNamesAsync(db, caseRow, ct);

        await db.SaveChangesAsync(ct);

        return new CasePrivacyRetrofitResult(
            madePrivate, locationGeneralized, stripped, already, unstrippable, occurrences, wasPublic);
    }

    private enum StripOutcome { Created, Existed, NotPossible }

    private async Task<StripOutcome> EnsureStrippedCopyAsync(UploadFile file, CancellationToken ct)
    {
        var storagePath = file.StoragePath!;

        var imagePath = _sanitizer.SanitizedPathFor(storagePath);
        var avPath    = _sanitizer.StrippedPathFor(storagePath);
        if (_fileStorage.Exists(imagePath) || (avPath != imagePath && _fileStorage.Exists(avPath)))
            return StripOutcome.Existed;

        try
        {
            await using var source = await _fileStorage.OpenReadAsync(storagePath, ct);
            await using var buffer = new MemoryStream();
            await source.CopyToAsync(buffer, ct);
            var original = buffer.ToArray();

            if (_sanitizer.CanSanitize(file.ContentType))
            {
                var clean = _sanitizer.Sanitize(original);
                await using var stream = new MemoryStream(clean, writable: false);
                await _fileStorage.WriteAsync(imagePath, stream, ct);
                return StripOutcome.Created;
            }

            if (_avStripper.CanStrip(file.ContentType)
                && await _avStripper.StripAsync(original, file.FileName, ct) is { } remuxed)
            {
                await using var stream = new MemoryStream(remuxed, writable: false);
                await _fileStorage.WriteAsync(avPath, stream, ct);
                return StripOutcome.Created;
            }

            return StripOutcome.NotPossible;
        }
        catch (Exception ex) when (ex is IOException or UnreadableImageException)
        {
            // One unreadable file must not abandon the rest of the case.
            _logger.LogWarning(ex, "Could not produce a stripped copy of {FileId}.", file.Id);
            return StripOutcome.NotPossible;
        }
    }

    private static async Task<List<Guid>> CaseFileIdsAsync(BenDataContext db, Guid caseId, CancellationToken ct)
    {
        var direct = await db.CaseFiles.AsNoTracking()
            .Where(f => f.CaseId == caseId).Select(f => f.UploadFileId).ToListAsync(ct);
        var timeline = await db.CaseTimelineEntryFiles.AsNoTracking()
            .Where(f => f.CaseTimelineEntry.CaseId == caseId).Select(f => f.UploadFileId).ToListAsync(ct);
        var reports = await db.CaseReportSectionFiles.AsNoTracking()
            .Where(f => f.Section.CaseReport.CaseId == caseId).Select(f => f.UploadFileId).ToListAsync(ct);
        var research = await db.CaseResearchEntries.AsNoTracking()
            .Where(e => e.CaseId == caseId && e.UploadFileId != null)
            .Select(e => e.UploadFileId!.Value).ToListAsync(ct);

        return [.. direct.Concat(timeline).Concat(reports).Concat(research).Distinct()];
    }

    /// <summary>
    /// Every place the client's real name appears in text a person wrote. Uses the same whole-word,
    /// three-character-minimum matching as the publish-time check (item 176), so "Parker" is not a
    /// hit for a client named Park.
    /// </summary>
    private static async Task<List<ClientNameOccurrence>> FindClientNamesAsync(
        BenDataContext db, Case caseRow, CancellationToken ct)
    {
        var occurrences = new List<ClientNameOccurrence>();
        if (caseRow.ClientRequestId is not { } requestId) return occurrences;

        var client = await db.ClientRequests.AsNoTracking()
            .Where(r => r.Id == requestId)
            .Select(r => new { r.AppUser.FirstName, r.AppUser.LastName, r.AppUser.DisplayName })
            .FirstOrDefaultAsync(ct);
        if (client is null) return occurrences;

        string?[] names = [client.FirstName, client.LastName, client.DisplayName];

        void Scan(string? text, string where, string field, Guid id, string kind)
        {
            foreach (var warning in PublicTitleLeakCheck.Check(text, null, names, null))
            {
                // The check's sentence carries the token in quotes; the caller wants the token.
                var start = warning.IndexOf('"');
                var end   = start >= 0 ? warning.IndexOf('"', start + 1) : -1;
                var token = start >= 0 && end > start ? warning[(start + 1)..end] : "the client's name";
                occurrences.Add(new ClientNameOccurrence(where, field, token, id, kind));
            }
        }

        Scan(caseRow.Title, "The case label", "Title", caseRow.Id, "Case");
        Scan(caseRow.Description, "The case description", "Description", caseRow.Id, "Case");

        foreach (var entry in await db.CaseTimelineEntries.AsNoTracking()
                     .Where(e => e.CaseId == caseRow.Id).ToListAsync(ct))
        {
            var where = $"Timeline entry: {entry.Title ?? entry.DateCreated.ToString("MM/dd/yyyy")}";
            Scan(entry.Title, where, "Title", entry.Id, "CaseTimelineEntry");
            Scan(entry.Body,  where, "Body",  entry.Id, "CaseTimelineEntry");
        }

        foreach (var report in await db.CaseReports.AsNoTracking()
                     .Where(r => r.CaseId == caseRow.Id).ToListAsync(ct))
        {
            var where = $"Report: {report.Title}";
            Scan(report.Title,      where, "Title",      report.Id, "CaseReport");
            Scan(report.Summary,    where, "Summary",    report.Id, "CaseReport");
            Scan(report.Conclusion, where, "Conclusion", report.Id, "CaseReport");

            foreach (var section in await db.CaseReportSections.AsNoTracking()
                         .Where(s => s.CaseReportId == report.Id).ToListAsync(ct))
            {
                var sectionWhere = $"Report section: {section.Title}";
                Scan(section.Title, sectionWhere, "Title", section.Id, "CaseReportSection");
                Scan(section.Body,  sectionWhere, "Body",  section.Id, "CaseReportSection");
            }
        }

        return occurrences;
    }
}
