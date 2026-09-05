using Ben.Data.Source.Entities;
using Ben.Service.RepositoryService.GenericInterfaces;
using Ben.Data.Common.Constants;
using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin;

/// <summary>
/// Field sessions whose readings are not on this server, and the one button that removes them.
/// </summary>
/// <remarks>
/// <para>Local Playwright runs used to write to the shared database. The suite uploads a session
/// document, the row lands in SQL — which the live site reads — and the bytes land in the
/// developer's own uploads directory, which the live site cannot see. The result is a row that
/// exists everywhere and can be opened nowhere: every read answers "This session's readings are no
/// longer on the server." 32 of them were found on production on 2026-09-02.</para>
///
/// <para><b>The rule for what may be deleted is deliberately not "looks like a test".</b> Matching
/// on a label like "Playback check" would delete a real session somebody happened to name that
/// way. The rule is instead a fact about the data: <b>the document's bytes cannot be read</b>. A
/// session in that state carries nothing — no readings, no chart, no replay — and nothing else in
/// the product can do anything with it. That makes deleting it safe in a way "it looked like a
/// test" never is.</para>
///
/// <para>Two endpoints, and the split matters: the preview changes nothing and is what the button
/// is drawn from, so the person clicking has already seen the exact list. A count alone would ask
/// them to trust an arithmetic they cannot check.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/orphaned-field-sessions")]
public sealed class AdminOrphanedFieldSessionController : BenControllerBase
{
    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IFileStorageService _fileStorage;
    private readonly ILogger<AdminOrphanedFieldSessionController> _log;
    private readonly IAuditLogService _auditLog;

    public AdminOrphanedFieldSessionController(
        IDbContextFactory<BenDataContext> dbFactory,
        IFileStorageService fileStorage,
        ILogger<AdminOrphanedFieldSessionController> log,
        IAuditLogService auditLog)
    {
        _dbFactory   = dbFactory;
        _fileStorage = fileStorage;
        _log         = log;
        _auditLog    = auditLog;
    }

    /// <summary>Every session whose document cannot be read back. Changes nothing.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<OrphanedFieldSessionRecord>>> Preview(
        CancellationToken ct)
        => Ok(await FindAsync(ct));

    /// <summary>
    /// Deletes them, with their file rows.
    /// </summary>
    /// <remarks>
    /// The set is recomputed here rather than taken from the caller: a list posted back could name
    /// a session that has since become readable, and re-deriving it means the button can only ever
    /// delete what the rule already covers. The ids on the request are the caller saying what they
    /// were shown — if the answer has changed since, nothing is deleted and the new count comes
    /// back, because a screen that has gone stale should not act on the old number.
    /// </remarks>
    /// <summary>
    /// Deletes the sessions named in the request.
    /// </summary>
    /// <remarks>
    /// The caller chooses which rows, but the server still decides what is deletable: the orphan
    /// set is recomputed here and the request is intersected with it. An id that is no longer
    /// orphaned — because its bytes came back, or somebody else deleted it — is refused by name
    /// rather than quietly skipped, because a screen that has gone stale should say so instead of
    /// doing three quarters of what was asked.
    /// </remarks>
    [HttpDelete]
    public async Task<ActionResult<OrphanedFieldSessionPurgeResult>> Purge(
        [FromBody] PurgeOrphanedSessionsRequest request, CancellationToken ct)
    {
        if (request?.Ids is null || request.Ids.Count == 0)
            return BadRequest("Choose at least one session to delete.");

        var orphans = await FindAsync(ct);
        var orphanIds = orphans.Select(o => o.Id).ToHashSet();

        var notOrphaned = request.Ids.Where(id => !orphanIds.Contains(id)).ToList();
        if (notOrphaned.Count > 0)
            return Conflict(new OrphanedFieldSessionPurgeResult(0, orphans.Count,
                $"{notOrphaned.Count} of the {request.Ids.Count} you chose "
                + (notOrphaned.Count == 1 ? "is" : "are") + " no longer orphaned — the list has "
                + "changed since you looked. Nothing was deleted; look again."));

        var ids = request.Ids.Distinct().ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var documentFileIds = await db.FieldSessionUploads.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => s.DocumentUploadFileId)
            .ToListAsync(ct);

        var mediaFileIds = await db.FieldSessionUploadFiles.AsNoTracking()
            .Where(f => ids.Contains(f.FieldSessionUploadId))
            .Select(f => f.UploadFileId)
            .ToListAsync(ct);

        int citations;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            // A case report can cite a field session, and that foreign key is NoAction — so a
            // cited session refuses to delete and takes the whole batch down with it. The first
            // version of this endpoint missed that and did nothing at all on a database where any
            // session had been cited, which is every database a report test has ever run against.
            //
            // Removing the citation is right rather than merely expedient: it points at a session
            // whose readings are not on this server, so the report already renders it as an
            // absence. A citation of nothing is not evidence somebody would miss.
            citations = await db.CaseReportSectionFieldSessions
                .Where(c => ids.Contains(c.FieldSessionUploadId))
                .ExecuteDeleteAsync(ct);

            // Before the file rows: a share link may name one recording, and that foreign key is
            // NoAction — see the comment in OrganizationPurge for why the alternatives are worse.
            // A link to a session whose readings are not on this server reaches nothing anyway.
            // Swept by both columns for the reason given there: today a link's file always belongs
            // to the link's own session, and nothing in the schema enforces it.
            var linkedFileIds = await db.FieldSessionUploadFiles.AsNoTracking()
                .Where(f => ids.Contains(f.FieldSessionUploadId)).Select(f => f.Id).ToListAsync(ct);
            await db.FieldSessionShareLinks
                .Where(l => ids.Contains(l.FieldSessionUploadId)
                         || (l.FieldSessionUploadFileId != null
                             && linkedFileIds.Contains(l.FieldSessionUploadFileId.Value)))
                .ExecuteDeleteAsync(ct);

            await db.FieldSessionUploadFiles.Where(f => ids.Contains(f.FieldSessionUploadId))
                .ExecuteDeleteAsync(ct);
            await db.FieldSessionUploads.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync(ct);

            await transaction.CommitAsync(ct);
        }

        // The UploadFile rows go afterwards and on their own, deliberately. Two dozen tables point
        // at UploadFiles and several do so with NoAction, so one file another feature still
        // references would abort a combined transaction and put the sessions back — undoing work
        // that had already succeeded for the sake of tidying. The sessions are the point; a file
        // row that will not go is reported, not fatal.
        var fileIds = documentFileIds.Concat(mediaFileIds).Distinct().ToList();
        var filesRemoved = 0;
        var filesKept    = 0;
        foreach (var fileId in fileIds)
        {
            try
            {
                filesRemoved += await db.UploadFiles.Where(f => f.Id == fileId).ExecuteDeleteAsync(ct);
            }
            catch (DbUpdateException)
            {
                filesKept++;   // something else still points at it
            }
            catch (Microsoft.Data.SqlClient.SqlException)
            {
                filesKept++;
            }
        }

        _log.LogWarning(
            "Deleted {SessionCount} orphaned field sessions, {Citations} report citations and "
            + "{FilesRemoved} upload rows ({FilesKept} kept, still referenced), by {UserId}.",
            ids.Count, citations, filesRemoved, filesKept, GetCurrentUserId());

        // A door that deletes rows says who opened it. Ben deleted 33 sessions on 2026-09-03 and the
        // audit log recorded nothing; the log line above is Warning level, which the database sink
        // does not keep. One audit row per session, with what the row said about itself.
        var actingUserId = GetCurrentUserId();
        foreach (var orphan in orphans.Where(o => ids.Contains(o.Id)))
        {
            await _auditLog.LogDeleteAsync(nameof(FieldSessionUpload), orphan.Id,
                new { orphan.LocationLabel, orphan.DeviceModel, orphan.RecordedByName, orphan.StartedAt, orphan.ReadingCount, orphan.MarkerCount, Reason = "orphaned: document not on this server" },
                actingUserId, AppSources.WebApi);
        }

        var note = filesKept > 0
            ? $"{filesKept} file record{(filesKept == 1 ? " was" : "s were")} left in place because "
              + "something else still references them."
            : null;

        return Ok(new OrphanedFieldSessionPurgeResult(ids.Count, orphans.Count - ids.Count, null) { Note = note });
    }

    /// <summary>
    /// A session is orphaned when its document has no storage path and no inline bytes, or when
    /// the path it names is not on this server's disk.
    /// </summary>
    /// <remarks>
    /// The disk check is what makes this honest across machines: the row's path is perfectly valid
    /// on the laptop that wrote it, so "has a StoragePath" is not the question — "can this server
    /// open it" is. Sessions are few enough that checking each one is cheaper than being wrong.
    /// </remarks>
    private async Task<List<OrphanedFieldSessionRecord>> FindAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var candidates = await db.FieldSessionUploads.AsNoTracking()
            .Include(s => s.DocumentUploadFile)
            .OrderByDescending(s => s.DateCreated)
            .Select(s => new
            {
                s.Id,
                s.LocationLabel,
                s.DeviceModel,
                s.StartedAt,
                s.DateCreated,
                s.ReadingCount,
                s.MarkerCount,
                s.RecordedByName,
                s.InvestigationId,
                s.PublishedAtUtc,
                StoragePath = s.DocumentUploadFile.StoragePath,
                HasInlineBytes = s.DocumentUploadFile.FileData != null,
                MediaCount = db.FieldSessionUploadFiles.Count(f => f.FieldSessionUploadId == s.Id),
            })
            .ToListAsync(ct);

        var orphans = new List<OrphanedFieldSessionRecord>();
        foreach (var c in candidates)
        {
            if (c.HasInlineBytes) continue;

            var readable = false;
            if (!string.IsNullOrEmpty(c.StoragePath))
            {
                try
                {
                    await using var stream = await _fileStorage.OpenReadAsync(c.StoragePath, ct);
                    readable = stream is not null;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                             or FileNotFoundException or DirectoryNotFoundException)
                {
                    readable = false;
                }
            }

            if (readable) continue;

            orphans.Add(new OrphanedFieldSessionRecord(
                c.Id, c.LocationLabel, c.DeviceModel, c.StartedAt, c.DateCreated,
                c.ReadingCount, c.MarkerCount, c.RecordedByName,
                c.InvestigationId is not null, c.PublishedAtUtc is not null, c.MediaCount));
        }

        return orphans;
    }
}

/// <summary>One session that cannot be opened, described well enough to recognise.</summary>
/// <summary>Which sessions to delete. Named explicitly so the screen can offer a choice.</summary>
public sealed record PurgeOrphanedSessionsRequest(IReadOnlyList<Guid> Ids);

public sealed record OrphanedFieldSessionRecord(
    Guid Id, string? LocationLabel, string DeviceModel, DateTime StartedAt, DateTime DateCreated,
    int ReadingCount, int MarkerCount, string? RecordedByName,
    bool LinkedToInvestigation, bool PublishedToArchive, int MediaCount);

/// <summary>What the button did. <paramref name="Refusal"/> is set when it did nothing.</summary>
public sealed record OrphanedFieldSessionPurgeResult(int Deleted, int Remaining, string? Refusal)
{
    /// <summary>Something worth saying that is not a refusal — e.g. file rows left in place.</summary>
    public string? Note { get; init; }
}
