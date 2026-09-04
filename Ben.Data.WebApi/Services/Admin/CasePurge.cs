using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Admin;

/// <summary>
/// What deleting one case would destroy, and what it would leave standing (item 183).
/// </summary>
/// <remarks>
/// <para>Two blocks, and the difference is the point. The first are records that exist only
/// because the case does — its timeline, its notes, its reports, its investigations. The second
/// are records that belong to somebody else and merely mention the case: a person's field
/// session, a feed post, a calendar event, a video project. Those are unlinked, not destroyed.</para>
///
/// <para><b>There is no refusal.</b> The person and group purges each have one because each can
/// lock the platform out of something — the last SuperAdmin, a group's own billing. Deleting a
/// case cannot. What it can do is destroy work, so the screen shows the counts and asks for the
/// title to be typed; a client on the case and a public page are <i>notices</i>, in the same
/// spirit as item 212's.</para>
/// </remarks>
public sealed record CasePurgePreview(
    Guid CaseId,
    string Title,
    string CaseReference,
    string OrganizationName,
    CaseStatus Status,
    bool IsPublic,

    // ── destroyed ─────────────────────────────────────────────────────────────
    int TimelineEntries,
    int Files,
    int Notes,
    int Messages,
    int ResearchEntries,
    int Reports,
    int Investigations,
    int Contacts,
    int Votes,
    int TransferLogs,
    int ClientAccessRows,
    int StoredFiles,

    // ── kept, with the case reference removed ─────────────────────────────────
    int FieldSessionsDetached,
    int FeedPostsUnlinked,
    int CalendarEventsUnlinked,
    int VideoProjectsUnlinked,
    int EvidenceVotesUnlinked,
    int PublicPagesUnlinked,

    // ── worth reading before pressing the button ──────────────────────────────
    string? ClientName);

/// <summary>What deleting the case actually did.</summary>
public sealed record CasePurgeResult(
    Guid CaseId, string Title, string CaseReference,
    int TimelineEntries, int Files, int Investigations,
    int FieldSessionsDetached, int StoredFiles);

/// <summary>
/// Deleting a case (SuperAdmin). The verb that did not exist until item 183 — a mistaken or
/// duplicate case was permanent, and the one created against the shared database during testing
/// had to be removed with raw SQL.
/// </summary>
/// <remarks>
/// <para><b>A group never reaches this.</b> A case is a record of real work, usually for a paying
/// client, so the rule the product states is: close it, do not delete it. This exists for the
/// mistakes that rule cannot cover — a duplicate, a test row, a case opened against the wrong
/// group — and it is deliberately behind a SuperAdmin screen with the title typed back.</para>
///
/// <para><b>Recordings survive.</b> A field session is a person's own recording that happens to
/// be attached to one of the case's investigations. Deleting the case sets its
/// <c>InvestigationId</c> to null, which is exactly what a personal session is, so it returns to
/// its owner rather than being destroyed with somebody else's case. Everything the session is
/// made of — its files, its share links, its document — is untouched.</para>
///
/// <para><b>Order is forced by the database.</b> Twelve tables point at <c>Cases</c> with
/// NoAction and would refuse the delete; the grandchildren under reports, timeline entries and
/// investigations refuse it a level deeper. The sequence below is the one the group purge already
/// proved, scoped to a single case, and <c>CasePurgeCoverageTests</c> derives the list from the
/// model so the next table anyone hangs off a case fails a test rather than a delete.</para>
/// </remarks>
public sealed class CasePurge
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<CasePurge> _log;

    public CasePurge(
        IDbContextFactory<BenDataContext> dbFactory,
        IFileStorageService fileStorage,
        ILogger<CasePurge> log)
    {
        _dbFactory   = dbFactory;
        _fileStorage = fileStorage;
        _log         = log;
    }

    /// <summary>What this would do, before anybody commits to it.</summary>
    public async Task<CasePurgePreview?> PreviewAsync(Guid caseId, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var record = await db.Cases.AsNoTracking()
            .Where(c => c.Id == caseId)
            .Select(c => new
            {
                c.Id, c.Title, c.CaseYear, c.OrgCaseNumber, c.Status, c.IsPublic,
                OrganizationName = c.Organization.Name,
                c.ClientRequestId,
            })
            .FirstOrDefaultAsync(ct);
        if (record is null) return null;

        var investigationIds = await db.Investigations.AsNoTracking()
            .Where(i => i.CaseId == caseId).Select(i => i.Id).ToListAsync(ct);
        var reportIds = await db.CaseReports.AsNoTracking()
            .Where(r => r.CaseId == caseId).Select(r => r.Id).ToListAsync(ct);

        // The client's own name, from the request the case came from. A notice, not a bar: a
        // SuperAdmin deleting a duplicate should see whose record they are about to remove.
        string? clientName = null;
        if (record.ClientRequestId is { } requestId)
        {
            clientName = await db.ClientRequests.AsNoTracking()
                .Where(r => r.Id == requestId)
                .Join(db.Users.AsNoTracking(), r => r.AppUserId, u => u.Id, (r, u) => u.DisplayName ?? u.Email)
                .FirstOrDefaultAsync(ct);
        }

        var storedPaths = await CaseCopyPathsAsync(db, caseId, reportIds, ct);

        return new CasePurgePreview(
            record.Id,
            record.Title,
            $"#{record.CaseYear}-{record.OrgCaseNumber:D3}",
            record.OrganizationName,
            record.Status,
            record.IsPublic,

            TimelineEntries:  await db.CaseTimelineEntries.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            Files:            await db.CaseFiles.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            Notes:            await db.CaseNotes.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            Messages:         await db.CaseMessages.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            ResearchEntries:  await db.CaseResearchEntries.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            Reports:          reportIds.Count,
            Investigations:   investigationIds.Count,
            Contacts:         await db.CaseContacts.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            Votes:            await db.CaseVotes.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            TransferLogs:     await db.CaseTransferLogs.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            ClientAccessRows: await db.CaseClientAccesses.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct)
                            + await db.CaseClientInvites.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            StoredFiles:      storedPaths.Count,

            FieldSessionsDetached: investigationIds.Count == 0 ? 0 : await db.FieldSessionUploads.AsNoTracking()
                .CountAsync(s => s.InvestigationId != null && investigationIds.Contains(s.InvestigationId.Value), ct),
            FeedPostsUnlinked:      await db.OrgMessages.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            CalendarEventsUnlinked: await db.OrgCalendarEvents.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            VideoProjectsUnlinked:  await db.VideoProjects.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            EvidenceVotesUnlinked:  await db.EvidenceVotes.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),
            PublicPagesUnlinked:    await db.OrganizationPages.AsNoTracking().CountAsync(x => x.CaseId == caseId, ct),

            ClientName: clientName);
    }

    /// <summary>
    /// Deletes the case. <paramref name="confirmation"/> must be the case title, typed exactly.
    /// </summary>
    public async Task<(CasePurgeResult? Result, string? Error)> PurgeAsync(
        Guid caseId, string? confirmation, Guid actingUserId, CancellationToken ct = default)
    {
        var preview = await PreviewAsync(caseId, ct);
        if (preview is null) return (null, "That case no longer exists.");

        if (!string.Equals(confirmation?.Trim(), preview.Title, StringComparison.Ordinal))
            return (null, $"Type “{preview.Title}” exactly to confirm.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var investigationIds = await db.Investigations.Where(i => i.CaseId == caseId)
            .Select(i => i.Id).ToListAsync(ct);
        var reportIds = await db.CaseReports.Where(r => r.CaseId == caseId)
            .Select(r => r.Id).ToListAsync(ct);
        var sectionIds = await db.CaseReportSections.Where(s => reportIds.Contains(s.CaseReportId))
            .Select(s => s.Id).ToListAsync(ct);
        var entryIds = await db.CaseTimelineEntries.Where(e => e.CaseId == caseId)
            .Select(e => e.Id).ToListAsync(ct);
        var attendeeIds = await db.InvestigationAttendees.Where(a => investigationIds.Contains(a.InvestigationId))
            .Select(a => a.Id).ToListAsync(ct);

        // Read BEFORE the link rows go: afterwards nothing remembers which files were the case's
        // own copies, and bytes nobody can name again are the one part of this that cannot be
        // cleaned up later.
        var copyFiles = await CaseCopyFilesAsync(db, caseId, reportIds, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // ── kept, unlinked ────────────────────────────────────────────────
            // These belong to somebody else and merely mention the case. A field session is the
            // clearest: it is a person's recording, and a null InvestigationId is exactly what a
            // personal session is, so it goes back to its owner intact.
            if (investigationIds.Count > 0)
            {
                await db.FieldSessionUploads
                    .Where(s => s.InvestigationId != null && investigationIds.Contains(s.InvestigationId.Value))
                    .ExecuteUpdateAsync(u => u.SetProperty(s => s.InvestigationId, (Guid?)null), ct);
                await db.EquipmentCheckouts
                    .Where(x => x.InvestigationId != null && investigationIds.Contains(x.InvestigationId.Value))
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.InvestigationId, (Guid?)null), ct);
                // A timeline entry on ANOTHER case that names one of these investigations. Rare,
                // and a refused delete if it is not cleared first.
                await db.CaseTimelineEntries
                    .Where(x => x.CaseId != caseId && x.InvestigationId != null
                             && investigationIds.Contains(x.InvestigationId.Value))
                    .ExecuteUpdateAsync(u => u.SetProperty(x => x.InvestigationId, (Guid?)null), ct);
                await db.UploadFileShares
                    .Where(x => x.TargetInvestigationId != null && investigationIds.Contains(x.TargetInvestigationId.Value))
                    .ExecuteDeleteAsync(ct);
            }

            await db.OrgMessages.Where(x => x.CaseId == caseId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CaseId, (Guid?)null), ct);
            await db.OrgCalendarEvents.Where(x => x.CaseId == caseId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CaseId, (Guid?)null), ct);
            await db.VideoProjects.Where(x => x.CaseId == caseId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CaseId, (Guid?)null), ct);
            await db.EvidenceVotes.Where(x => x.CaseId == caseId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CaseId, (Guid?)null), ct);
            await db.OrganizationPages.Where(x => x.CaseId == caseId)
                .ExecuteUpdateAsync(u => u.SetProperty(x => x.CaseId, (Guid?)null), ct);

            // ── destroyed: the grandchildren first ────────────────────────────
            await db.CaseReportSectionFieldSessions.Where(x => sectionIds.Contains(x.CaseReportSectionId)).ExecuteDeleteAsync(ct);
            await db.CaseReportSectionFiles.Where(x => sectionIds.Contains(x.CaseReportSectionId)).ExecuteDeleteAsync(ct);
            await db.CaseReportSections.Where(x => reportIds.Contains(x.CaseReportId)).ExecuteDeleteAsync(ct);
            await db.CaseReports.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);

            await db.CaseTimelineEntryExperienceTypes.Where(x => entryIds.Contains(x.CaseTimelineEntryId)).ExecuteDeleteAsync(ct);
            await db.CaseTimelineEntryFiles.Where(x => entryIds.Contains(x.CaseTimelineEntryId)).ExecuteDeleteAsync(ct);
            await db.CaseTimelineEntries.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);

            await db.InvestigationDutyAssignments.Where(x => attendeeIds.Contains(x.InvestigationAttendeeId)).ExecuteDeleteAsync(ct);
            await db.InvestigationAttendees.Where(x => investigationIds.Contains(x.InvestigationId)).ExecuteDeleteAsync(ct);
            await db.InvestigationFindings.Where(x => investigationIds.Contains(x.InvestigationId)).ExecuteDeleteAsync(ct);
            await db.InvestigationScheduleProposals.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);
            await db.Investigations.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);

            // ── destroyed: the case's own rows ────────────────────────────────
            await db.CaseClientAccesses.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);
            await db.CaseClientInvites.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);
            await db.CaseContacts.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);
            await db.CaseFiles.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);
            await db.CaseMessages.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);
            await db.CaseNotes.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);
            await db.CaseRelatedPeople.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);
            await db.CaseResearchEntries.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);
            await db.CaseTransferLogs.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);
            await db.CaseVotes.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);
            // The consent a feed post recorded against this case. Its CaseId is required, so it
            // cannot outlive the case even though the post it belongs to does.
            await db.FeedPostConsents.Where(x => x.CaseId == caseId).ExecuteDeleteAsync(ct);

            await db.Cases.Where(c => c.Id == caseId).ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _log.LogError(ex, "Purge of case {CaseId} rolled back.", caseId);
            return (null, $"Nothing was deleted. The database refused: {ex.GetBaseException().Message}");
        }

        // ── the bytes, afterwards and one at a time ───────────────────────────
        // Only the case's OWN copies: copy-on-attach mints a fresh file per case, and the
        // person's original is not this case's to destroy. A copy something else still holds is
        // left standing rather than taking the rest down with it.
        var filesRemoved = 0;
        foreach (var file in copyFiles)
        {
            if (!await UploadFileRows.TryDeleteAsync(db, file.Id, ct)) continue;
            filesRemoved++;
            if (string.IsNullOrEmpty(file.StoragePath)) continue;
            try { await _fileStorage.DeleteAsync(file.StoragePath, ct); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Deleted case {CaseId} but could not remove {Path}.", caseId, file.StoragePath);
            }
        }

        try { await _fileStorage.DeleteDirectoryAsync($"cases/{caseId}", ct); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Deleted case {CaseId} but could not remove its storage directory.", caseId);
        }

        _log.LogWarning("Case {CaseId} “{Title}” deleted by {ActingUserId}.", caseId, preview.Title, actingUserId);

        return (new CasePurgeResult(
            caseId, preview.Title, preview.CaseReference,
            preview.TimelineEntries, filesRemoved, preview.Investigations,
            preview.FieldSessionsDetached, preview.StoredFiles), null);
    }

    /// <summary>
    /// The files this case owns: the copies copy-on-attach minted for it, from every door that
    /// attaches one. A file that is not a case copy is somebody's original, merely linked here,
    /// and is left alone.
    /// </summary>
    private static async Task<List<FileToRemove>> CaseCopyFilesAsync(
        BenDataContext db, Guid caseId, List<Guid> reportIds, CancellationToken ct)
    {
        var ids = await db.CaseFiles.AsNoTracking().Where(f => f.CaseId == caseId)
            .Select(f => f.UploadFileId).ToListAsync(ct);
        ids.AddRange(await db.CaseTimelineEntryFiles.AsNoTracking()
            .Where(f => f.CaseTimelineEntry.CaseId == caseId)
            .Select(f => f.UploadFileId).ToListAsync(ct));
        ids.AddRange(await db.CaseReportSectionFiles.AsNoTracking()
            .Join(db.CaseReportSections.AsNoTracking(), f => f.CaseReportSectionId, s => s.Id,
                  (f, s) => new { f.UploadFileId, s.CaseReportId })
            .Where(x => reportIds.Contains(x.CaseReportId))
            .Select(x => x.UploadFileId).ToListAsync(ct));

        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0) return [];

        return await db.UploadFiles.AsNoTracking()
            .Where(f => distinct.Contains(f.Id) && f.CaseCopyOfUploadFileId != null)
            .Select(f => new FileToRemove(f.Id, f.StoragePath))
            .ToListAsync(ct);
    }

    private static async Task<List<string>> CaseCopyPathsAsync(
        BenDataContext db, Guid caseId, List<Guid> reportIds, CancellationToken ct)
        => (await CaseCopyFilesAsync(db, caseId, reportIds, ct))
            .Where(f => !string.IsNullOrEmpty(f.StoragePath))
            .Select(f => f.StoragePath!)
            .ToList();

    private sealed record FileToRemove(Guid Id, string? StoragePath);
}
