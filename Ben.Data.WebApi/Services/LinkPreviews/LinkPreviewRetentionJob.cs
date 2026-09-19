using Ben.Data.Common.Interfaces;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.LinkPreviews;

/// <summary>
/// Forgets link previews nobody has posted for a while, with their copied pictures.
/// </summary>
/// <remarks>
/// A preview is a week-old snapshot of somebody else's page. Kept for ever it would grow without limit and go on
/// describing pages that have changed. Past its expiry it is removed. A message whose card is removed falls back to the
/// link's host until somebody posts that link again.
///
/// <para>This used to promise an exception — "unless a research page's rail still points at it" — that the code never
/// contained and now could not: research pages were retired (see the <c>RetireResearchPages</c> migration), and
/// <c>StoredLinkPreview</c> has no relationship to anything. The sentence outlived the feature, and a reader of this
/// job believed a protection was in place that was not (2026-09-17 audit). If a kept-forever preview is wanted again,
/// it needs a reference to hold it, not a comment.</para>
/// </remarks>
public sealed class LinkPreviewRetentionJob(
    IDbContextFactory<BenDataContext> dbFactory, IFileStorageService storage, ILogger<LinkPreviewRetentionJob> log) : IScheduledJob
{
    private const int BatchSize = 200;

    public string Name => "link-preview-retention";

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var expired = await db.LinkPreviews.AsNoTracking()
            .Where(p => p.ExpiresUtc < now)
            .OrderBy(p => p.ExpiresUtc)
            .Take(BatchSize)
            .Select(p => new { p.Id, p.ThumbnailStoragePath })
            .ToListAsync(ct);
        if (expired.Count == 0) return;

        var ids = expired.Select(p => p.Id).ToList();
        await db.LinkPreviews.Where(p => ids.Contains(p.Id)).ExecuteDeleteAsync(ct);

        foreach (var path in expired.Select(p => p.ThumbnailStoragePath).OfType<string>())
        {
            try { await storage.DeleteAsync(path, ct); }
            catch (Exception ex) { log.LogWarning(ex, "Could not remove an expired link preview picture at {Path}.", path); }
        }

        log.LogInformation("Removed {Count} expired link preview(s).", expired.Count);
    }
}
