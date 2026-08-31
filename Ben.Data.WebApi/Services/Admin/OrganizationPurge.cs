using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Admin;

/// <summary>
/// What a group's deletion would destroy, counted before anything is destroyed.
/// </summary>
/// <param name="OrganizationName">Echoed back so a confirmation dialog names what it is about to remove.</param>
/// <param name="StoredFiles">Files whose BYTES will be deleted from storage, not merely their rows.</param>
public sealed record OrganizationPurgePreview(
    Guid OrganizationId,
    string OrganizationName,
    int Members,
    int Cases,
    int Investigations,
    int Events,
    int FieldSessions,
    int EventEvidence,
    int StoredFiles,
    int CmsPages,
    int BillingRows);

/// <summary>
/// Removes a group and everything that belongs to it. SuperAdmin, irreversible, and the most
/// destructive operation in the product.
/// </summary>
/// <remarks>
/// <para><b>Why it exists.</b> Ben, 2026-08-31: there is no separate test environment — the
/// development hosts point at the database that serves the live site — so seeded and abandoned
/// test groups accumulate in the only database there is. The ordinary delete on
/// <c>OrganizationController</c> deliberately REFUSES a group with real work attached, which is
/// right for an administrator tidying up and useless for the person who has to clear the test
/// data out. This is the deliberate, named exception to that rule.</para>
///
/// <para><b>What it never touches.</b> Three things survive, and each for its own reason:</para>
/// <list type="bullet">
///   <item><description><b>People.</b> An <c>AppUser</c> is not the group's property. Members lose
///   their membership and nothing else — they may belong to other groups, and they will still be
///   here tomorrow.</description></item>
///   <item><description><b>Places.</b> A place is shared: several groups investigate the same
///   building, and its public archive is built from many people's visits. Deleting a location
///   because one group that visited it is going away would destroy other people's work
///   (Ben, 2026-08-31: "the seeded and test locations look true and public ones").</description></item>
///   <item><description><b>Lookups and taxonomy.</b> Equipment catalogues, experience types, file
///   types — site-wide reference data that no group owns.</description></item>
/// </list>
///
/// <para><b>One transaction, leaf-first.</b> Every foreign key onto Organizations is NoAction by
/// convention here, so a missed table is a constraint violation rather than an orphan — and inside
/// a transaction that violation rolls the whole thing back. The failure mode is therefore "nothing
/// happened, and the error names the table", never a half-deleted group. That is what makes this
/// safe to run before it is provably exhaustive, and
/// <c>OrganizationPurgeCoverageTests</c> is what keeps it exhaustive as tables are added.</para>
///
/// <para><b>Field sessions with no investigation are the person's, not the group's.</b> Somebody
/// who scouted a building on their own account keeps that recording when a group they belonged to
/// is deleted — it was never the group's to lose. Only sessions attached to one of this group's
/// investigations go.</para>
/// </remarks>
public sealed class OrganizationPurge
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<OrganizationPurge> _log;

    public OrganizationPurge(
        IDbContextFactory<BenDataContext> dbFactory,
        IFileStorageService fileStorage,
        ILogger<OrganizationPurge> log)
    {
        _dbFactory = dbFactory;
        _fileStorage = fileStorage;
        _log = log;
    }

    /// <summary>Counts what would go, changing nothing.</summary>
    public async Task<OrganizationPurgePreview?> PreviewAsync(Guid organizationId, CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var org = await db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, ct);
        if (org is null) return null;

        var caseIds = await db.Cases.AsNoTracking()
            .Where(c => c.OrganizationId == organizationId).Select(c => c.Id).ToListAsync(ct);
        var investigationIds = await db.Investigations.AsNoTracking()
            .Where(i => i.OrganizationId == organizationId).Select(i => i.Id).ToListAsync(ct);
        var eventIds = await db.OrgCalendarEvents.AsNoTracking()
            .Where(e => e.OrganizationId == organizationId).Select(e => e.Id).ToListAsync(ct);

        return new OrganizationPurgePreview(
            organizationId,
            org.Name,
            Members: await db.OrganizationUserMemberships.AsNoTracking()
                .CountAsync(m => m.OrganizationId == organizationId, ct),
            Cases: caseIds.Count,
            Investigations: investigationIds.Count,
            Events: eventIds.Count,
            FieldSessions: await db.FieldSessionUploads.AsNoTracking()
                .CountAsync(s => s.InvestigationId != null
                              && investigationIds.Contains(s.InvestigationId.Value), ct),
            EventEvidence: await db.EventEvidenceSubmissions.AsNoTracking()
                .CountAsync(e => eventIds.Contains(e.OrgCalendarEventId), ct),
            StoredFiles: (await StoredPathsAsync(db, organizationId, caseIds, investigationIds, eventIds, ct)).Count,
            CmsPages: await db.OrganizationPages.AsNoTracking()
                .CountAsync(p => p.OrganizationId == organizationId, ct),
            BillingRows: await db.BillingLedgerEntries.AsNoTracking()
                .CountAsync(b => b.OrganizationId == organizationId, ct));
    }

    /// <summary>
    /// Every stored file this group owns, by storage path.
    /// </summary>
    /// <remarks>
    /// Collected BEFORE the rows go, because once the rows are gone nothing remembers where the
    /// bytes were — and orphaned bytes on a disk nobody can enumerate are worse than the rows.
    /// </remarks>
    private static async Task<List<string>> StoredPathsAsync(
        BenDataContext db, Guid organizationId,
        List<Guid> caseIds, List<Guid> investigationIds, List<Guid> eventIds, CancellationToken ct)
    {
        var fileIds = new List<Guid>();

        fileIds.AddRange(await db.CaseFiles.AsNoTracking()
            .Where(f => caseIds.Contains(f.CaseId))
            .Select(f => f.UploadFileId).ToListAsync(ct));

        fileIds.AddRange(await db.EventEvidenceSubmissions.AsNoTracking()
            .Where(e => eventIds.Contains(e.OrgCalendarEventId))
            .Select(e => e.UploadFileId).ToListAsync(ct));

        fileIds.AddRange(await db.FieldSessionUploadFiles.AsNoTracking()
            .Where(f => f.FieldSessionUpload.InvestigationId != null
                     && investigationIds.Contains(f.FieldSessionUpload.InvestigationId.Value))
            .Select(f => f.UploadFileId).ToListAsync(ct));

        var distinct = fileIds.Distinct().ToList();

        var paths = await db.UploadFiles.AsNoTracking()
            .Where(f => distinct.Contains(f.Id) && f.StoragePath != null && f.StoragePath != "")
            .Select(f => f.StoragePath!)
            .ToListAsync(ct);

        // OrganizationFile carries its own StoragePath rather than pointing at an UploadFile, so
        // it is collected separately — and missing it would leave the group's own documents on
        // disk with nothing left to name them.
        paths.AddRange(await db.OrganizationFiles.AsNoTracking()
            .Where(f => f.OrganizationId == organizationId
                     && f.StoragePath != null && f.StoragePath != "")
            .Select(f => f.StoragePath!)
            .ToListAsync(ct));

        return paths.Distinct().ToList();
    }

    /// <summary>
    /// Deletes the group and everything belonging to it. Returns what was removed.
    /// </summary>
    /// <param name="confirmationName">
    /// Must equal the group's own name. Not security — a SuperAdmin has already been let in — but
    /// the difference between deleting a group and deleting the group you meant. Typing the name
    /// is the one confirmation that cannot be clicked through by habit.
    /// </param>
    public async Task<(OrganizationPurgePreview? Removed, string? Error)> PurgeAsync(
        Guid organizationId, string confirmationName, Guid actingUserId, CancellationToken ct)
    {
        var preview = await PreviewAsync(organizationId, ct);
        if (preview is null) return (null, "No such group.");

        if (!string.Equals(confirmationName?.Trim(), preview.OrganizationName, StringComparison.Ordinal))
            return (null, $"Type the group's name exactly — \"{preview.OrganizationName}\" — to confirm.");

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var caseIds = await db.Cases.Where(c => c.OrganizationId == organizationId)
            .Select(c => c.Id).ToListAsync(ct);
        var investigationIds = await db.Investigations.Where(i => i.OrganizationId == organizationId)
            .Select(i => i.Id).ToListAsync(ct);
        var eventIds = await db.OrgCalendarEvents.Where(e => e.OrganizationId == organizationId)
            .Select(e => e.Id).ToListAsync(ct);
        var sessionIds = await db.FieldSessionUploads
            .Where(s => s.InvestigationId != null && investigationIds.Contains(s.InvestigationId.Value))
            .Select(s => s.Id).ToListAsync(ct);
        var reportIds = await db.CaseReports.Where(r => caseIds.Contains(r.CaseId))
            .Select(r => r.Id).ToListAsync(ct);

        // Paths first: after the rows go, nothing remembers where the bytes were.
        var storedPaths = await StoredPathsAsync(db, organizationId, caseIds, investigationIds, eventIds, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            // ── depth 3: children of reports and sessions ────────────────────
            var sectionIds = await db.CaseReportSections
                .Where(x => reportIds.Contains(x.CaseReportId)).Select(x => x.Id).ToListAsync(ct);
            await db.CaseReportSectionFieldSessions
                .Where(x => sectionIds.Contains(x.CaseReportSectionId)).ExecuteDeleteAsync(ct);
            await db.CaseReportSections.Where(x => reportIds.Contains(x.CaseReportId)).ExecuteDeleteAsync(ct);
            await db.FieldSessionUploadFiles
                .Where(x => sessionIds.Contains(x.FieldSessionUploadId)).ExecuteDeleteAsync(ct);

            // ── depth 2: children of cases, investigations and events ────────
            await db.CaseClientAccesses.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.CaseClientInvites.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.CaseContacts.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.CaseFiles.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.CaseMessages.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.CaseNotes.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.CaseRelatedPeople.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.CaseResearchEntries.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.CaseTimelineEntries.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.CaseTransferLogs.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.CaseVotes.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.EvidenceVotes.Where(x => x.CaseId != null && caseIds.Contains(x.CaseId.Value)).ExecuteDeleteAsync(ct);
            await db.FeedPostConsents.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);
            await db.CaseReports.Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);

            await db.EquipmentCheckouts
                .Where(x => x.InvestigationId != null && investigationIds.Contains(x.InvestigationId.Value)).ExecuteDeleteAsync(ct);
            await db.InvestigationAttendees.Where(x => investigationIds.Contains(x.InvestigationId)).ExecuteDeleteAsync(ct);
            await db.InvestigationFindings.Where(x => investigationIds.Contains(x.InvestigationId)).ExecuteDeleteAsync(ct);
            await db.FieldSessionUploads.Where(x => sessionIds.Contains(x.Id)).ExecuteDeleteAsync(ct);

            await db.EventAttendanceInvites.Where(x => eventIds.Contains(x.OrgCalendarEventId)).ExecuteDeleteAsync(ct);
            await db.EventEvidenceSubmissions.Where(x => eventIds.Contains(x.OrgCalendarEventId)).ExecuteDeleteAsync(ct);
            await db.EventReminderSents.Where(x => eventIds.Contains(x.OrgCalendarEventId)).ExecuteDeleteAsync(ct);
            await db.OrgCalendarEventAttendees.Where(x => eventIds.Contains(x.OrgCalendarEventId)).ExecuteDeleteAsync(ct);

            // Proposals reference both a case and an investigation; removed before either.
            await db.InvestigationScheduleProposals
                .Where(x => caseIds.Contains(x.CaseId)).ExecuteDeleteAsync(ct);

            // ── depth 1: everything hanging directly off the group ───────────
            await db.Investigations.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrgCalendarEvents.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.Cases.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);

            await db.BillingLedgerEntries.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.ClientRequestOrganizations.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.CouponRedemptions.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.EquipmentItemShares.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.InvestigationDuties.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.MemberSeatSubscriptions.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrgCalendarEventTypes.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrgMemberGroups.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrgMessages.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationAccessGrants.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationAds.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationAddresses.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationAreaOfOperations.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationBillingContacts.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationCmsTemplates.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationEmails.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationFileDeleteLogs.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationFiles.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationLinks.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationLogos.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationMemberLevels.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationMembershipQuestions.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationMembershipRequests.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationNotes.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationPages.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationPhones.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationSubscriptions.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.OrganizationUrlNameAliases.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.PlaceRooms.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.Publications.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.TierChangeNotices.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.UploadFileOrganizationShares.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.UploadFilePermissionRequests.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);

            // Roles, leaf-first, then the memberships, then the group itself.
            var roleIds = await db.OrganizationRoles.Where(r => r.OrganizationId == organizationId)
                .Select(r => r.Id).ToListAsync(ct);
            await db.OrganizationRoleMemberships.Where(x => roleIds.Contains(x.OrganizationRoleId)).ExecuteDeleteAsync(ct);
            await db.OrganizationRolePermissions.Where(x => roleIds.Contains(x.OrganizationRoleId)).ExecuteDeleteAsync(ct);
            await db.OrganizationRoles.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);

            await db.OrganizationUserMemberships.Where(x => x.OrganizationId == organizationId).ExecuteDeleteAsync(ct);
            await db.Organizations.Where(x => x.Id == organizationId).ExecuteDeleteAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            _log.LogError(ex, "Purge of organization {OrganizationId} rolled back.", organizationId);

            // The table name is in the constraint, and it is the only thing that makes this
            // actionable — a purge that says "something failed" leaves nobody anywhere.
            return (null, $"Nothing was deleted. The database refused: {ex.GetBaseException().Message}");
        }

        // Bytes last, and only after the rows are certainly gone. The other order can delete
        // somebody's files and then roll the rows back, leaving records pointing at nothing.
        foreach (var path in storedPaths)
        {
            try { await _fileStorage.DeleteAsync(path, ct); }
            catch (Exception ex)
            {
                // A file that will not delete is a tidy-up problem, not a reason to pretend the
                // group still exists. Logged so somebody can sweep it later.
                _log.LogWarning(ex, "Purged organization {OrganizationId}: could not delete {Path}.",
                    organizationId, path);
            }
        }

        _log.LogWarning(
            "SuperAdmin {UserId} purged organization {OrganizationId} (\"{Name}\"): "
          + "{Members} members, {Cases} cases, {Investigations} investigations, {Files} files.",
            actingUserId, organizationId, preview.OrganizationName,
            preview.Members, preview.Cases, preview.Investigations, preview.StoredFiles);

        return (preview, null);
    }
}
