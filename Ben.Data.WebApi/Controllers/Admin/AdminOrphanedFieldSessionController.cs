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

    public AdminOrphanedFieldSessionController(
        IDbContextFactory<BenDataContext> dbFactory,
        IFileStorageService fileStorage,
        ILogger<AdminOrphanedFieldSessionController> log)
    {
        _dbFactory   = dbFactory;
        _fileStorage = fileStorage;
        _log         = log;
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
    /// delete what the rule already covers. <paramref name="expectedCount"/> is the caller saying
    /// what they were shown — if the answer has changed since, nothing is deleted and the new
    /// count comes back, because a screen that has gone stale should not act on the old number.
    /// </remarks>
    [HttpDelete]
    public async Task<ActionResult<OrphanedFieldSessionPurgeResult>> Purge(
        [FromQuery] int expectedCount, CancellationToken ct)
    {
        var orphans = await FindAsync(ct);
        if (orphans.Count != expectedCount)
            return Conflict(new OrphanedFieldSessionPurgeResult(0, orphans.Count,
                $"The list changed since you looked: {orphans.Count} now, not {expectedCount}. "
                + "Nothing was deleted — look again."));

        if (orphans.Count == 0)
            return Ok(new OrphanedFieldSessionPurgeResult(0, 0, "There was nothing to delete."));

        var ids = orphans.Select(o => o.Id).ToList();

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // The file rows first: FieldSessionUploadFile cascades from the session, but the document's
        // own UploadFile row is referenced BY the session and would be left behind.
        var documentFileIds = await db.FieldSessionUploads.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => s.DocumentUploadFileId)
            .ToListAsync(ct);

        var mediaFileIds = await db.FieldSessionUploadFiles.AsNoTracking()
            .Where(f => ids.Contains(f.FieldSessionUploadId))
            .Select(f => f.UploadFileId)
            .ToListAsync(ct);

        await db.FieldSessionUploadFiles.Where(f => ids.Contains(f.FieldSessionUploadId))
            .ExecuteDeleteAsync(ct);
        await db.FieldSessionUploads.Where(s => ids.Contains(s.Id)).ExecuteDeleteAsync(ct);

        var fileIds = documentFileIds.Concat(mediaFileIds).Distinct().ToList();
        await db.UploadFiles.Where(f => fileIds.Contains(f.Id)).ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);

        _log.LogWarning(
            "Deleted {SessionCount} orphaned field sessions and {FileCount} upload rows, by {UserId}.",
            ids.Count, fileIds.Count, GetCurrentUserId());

        return Ok(new OrphanedFieldSessionPurgeResult(ids.Count, 0, null));
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
public sealed record OrphanedFieldSessionRecord(
    Guid Id, string? LocationLabel, string DeviceModel, DateTime StartedAt, DateTime DateCreated,
    int ReadingCount, int MarkerCount, string? RecordedByName,
    bool LinkedToInvestigation, bool PublishedToArchive, int MediaCount);

/// <summary>What the button did. <paramref name="Refusal"/> is set when it did nothing.</summary>
public sealed record OrphanedFieldSessionPurgeResult(int Deleted, int Remaining, string? Refusal);
