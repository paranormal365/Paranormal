using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Feed;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Sweeps feed media stuck in <see cref="FeedMediaReviewState.Pending"/> back through the
/// screener (item 186 F5b).
/// </summary>
/// <remarks>
/// <para>Pending is supposed to be momentary — the post-create path screens inline. It stops being
/// momentary when the screener threw, when the process died mid-create, when a video landed on a
/// host with no ffmpeg, or for everything posted in the window between F4 (media, fail-closed) and
/// this screener existing. All of those are the same problem — media nobody has looked at — and
/// this job is the one recovery path for all of them.</para>
///
/// <para>Skips entirely when the registered screener is <see cref="ManualReviewScreener"/>: its
/// verdict for everything IS Pending, so a sweep would churn the queue and change nothing.</para>
///
/// <para>Safe to run any time, any number of times (the scheduler's contract): it re-reads state
/// under each pass, only ever moves Pending → a verdict, and takes a bounded batch so one pass
/// never monopolizes a five-minute interval.</para>
/// </remarks>
public sealed class PendingMediaScreeningJob : IScheduledJob
{
    /// <summary>Oldest first, bounded; a backlog drains across passes rather than in one.</summary>
    public const int BatchSize = 50;

    /// <summary>Leave the newest alone — an in-flight create is screening its own media.</summary>
    public static readonly TimeSpan MinimumAge = TimeSpan.FromMinutes(2);

    private readonly IDbContextFactory<BenDataContext> _dbFactory;
    private readonly IFeedMediaScreener _screener;
    private readonly ILogger<PendingMediaScreeningJob> _logger;

    public PendingMediaScreeningJob(
        IDbContextFactory<BenDataContext> dbFactory,
        IFeedMediaScreener screener,
        ILogger<PendingMediaScreeningJob> logger)
    {
        _dbFactory = dbFactory;
        _screener = screener;
        _logger = logger;
    }

    public string Name => "feed-media-screening";

    public async Task RunAsync(CancellationToken ct)
    {
        if (!_screener.IsAutomatic) return;

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var cutoff = DateTime.UtcNow - MinimumAge;

        var waiting = await db.OrgMessages
            .Where(m => m.MediaUploadFileId != null
                     && m.MediaUploadFile!.StoragePath != null
                     && m.MediaReviewState == FeedMediaReviewState.Pending
                     && m.DateCreated < cutoff)
            .OrderBy(m => m.DateCreated)
            .Take(BatchSize)
            .Select(m => new { m.Id, m.MediaUploadFileId, m.MediaUploadFile!.StoragePath, m.MediaUploadFile.ContentType })
            .ToListAsync(ct);

        if (waiting.Count == 0) return;

        int approved = 0, held = 0, stillPending = 0;
        foreach (var item in waiting)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var verdict = await _screener.ScreenAsync(item.StoragePath!, item.ContentType, ct);
                if (verdict.State == FeedMediaReviewState.Pending)
                {
                    stillPending++; // e.g. video with no ffmpeg — nothing to record, retry later
                    continue;
                }

                // Re-read under this pass: a moderator may have decided while we screened, and
                // a person's decision outranks the sweep.
                var post = await db.OrgMessages.FirstOrDefaultAsync(m => m.Id == item.Id, ct);
                if (post is null || post.MediaReviewState != FeedMediaReviewState.Pending) continue;

                post.MediaReviewState = verdict.State;
                post.MediaReviewNote = verdict.Reason;
                await db.SaveChangesAsync(ct);
                if (verdict.State == FeedMediaReviewState.Approved) approved++; else held++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One broken file must not strand the rest of the batch behind it.
                _logger.LogWarning(ex, "Screening sweep failed on post {PostId}; left Pending.", item.Id);
                stillPending++;
            }
        }

        _logger.LogInformation(
            "Feed media sweep: {Approved} approved, {Held} held, {Pending} still pending of {Total}.",
            approved, held, stillPending, waiting.Count);
    }
}
