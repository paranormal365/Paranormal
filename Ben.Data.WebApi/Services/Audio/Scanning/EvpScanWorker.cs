using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Audio.Scanning;

/// <summary>
/// Runs queued EVP scans one at a time, oldest first, for as long as the API is up
/// (phase 6, 10/26/2026).
/// </summary>
/// <remarks>
/// <para><b>One at a time per server.</b> A scan is a steady stream of decoding, and two at once
/// would only make both slower while doubling the memory. People queue behind each other instead,
/// and each sees where theirs stands.</para>
/// <para><b>A restart</b> leaves any scan that was running half done. On starting, those are
/// marked failed with a sentence that says what happened, rather than sitting at "running" for
/// ever; their recordings keep the candidates they had.</para>
/// </remarks>
public sealed class EvpScanWorker(
    IServiceScopeFactory scopes, IDbContextFactory<BenDataContext> dbContextFactory,
    EvpScanQueue queue, ILogger<EvpScanWorker> logger) : BackgroundService
{
    public const string RestartedSentence = "The server restarted during this scan. Start it again.";

    /// <summary>How long the worker waits for a wake-up before looking at the table anyway.</summary>
    internal static readonly TimeSpan IdleCheck = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        try { await RecoverAsync(stopping); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not tidy up EVP scans interrupted by the last restart");
        }

        while (!stopping.IsCancellationRequested)
        {
            try
            {
                var next = await NextQueuedAsync(stopping);
                if (next is null)
                {
                    await queue.WaitAsync(IdleCheck, stopping);
                    continue;
                }

                var token = queue.Running(next.Value, stopping);
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<EvpScanRunner>().RunAsync(next.Value, token);
                }
                finally { queue.Done(next.Value); }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                // The runner records its own failures; reaching here means the table itself could
                // not be read. Wait before trying again rather than spinning.
                logger.LogError(ex, "The EVP scan worker hit a problem; trying again shortly");
                await Task.Delay(TimeSpan.FromSeconds(5), stopping);
            }
        }
    }

    private async Task<Guid?> NextQueuedAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        return await db.EvpScanJobs.AsNoTracking()
            .Where(j => j.Status == EvpScanJobStatus.Queued)
            .OrderBy(j => j.CreatedUtc)
            .Select(j => (Guid?)j.Id)
            .FirstOrDefaultAsync(ct);
    }

    internal async Task RecoverAsync(CancellationToken ct)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(ct);
        var interrupted = await db.EvpScanJobs.Include(j => j.Segments)
            .Where(j => j.Status == EvpScanJobStatus.Running)
            .ToListAsync(ct);
        if (interrupted.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var job in interrupted)
        {
            job.Status      = EvpScanJobStatus.Failed;
            job.Error       = RestartedSentence;
            job.FinishedUtc = now;
            foreach (var s in job.Segments.Where(s => s.FinishedUtc is null))
            {
                s.Status      = EvpScanJobStatus.Failed;
                s.Error       = RestartedSentence;
                s.FinishedUtc = now;
            }
        }
        await db.SaveChangesAsync(ct);
        logger.LogWarning("Marked {Count} EVP scan(s) interrupted by a restart as failed", interrupted.Count);
    }
}
