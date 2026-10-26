using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Deletes background EVP scan jobs a week after they finished (phase 6, 10/26/2026).
/// </summary>
/// <remarks>
/// A job only says how far a scan got. What it found is kept elsewhere, as the recording's markers
/// and its <c>EvpScan</c> record, and neither is touched here. A week covers "what happened to the
/// scan I started on Friday".
/// </remarks>
public sealed class EvpScanJobRetentionJob(IDbContextFactory<BenDataContext> dbFactory, ILogger<EvpScanJobRetentionJob> logger) : IScheduledJob
{
    public string Name => "evp-scan-job-retention";

    internal static readonly TimeSpan Keep = TimeSpan.FromDays(7);
    internal const int BatchSize = 500;

    public async Task RunAsync(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - Keep;
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Tracked deletes, not ExecuteDelete: the segments go with their job through the cascade
        // EF knows about, on every provider the tests use.
        var old = await db.EvpScanJobs.Include(j => j.Segments)
            .Where(j => j.FinishedUtc != null && j.FinishedUtc < cutoff)
            .OrderBy(j => j.FinishedUtc)
            .Take(BatchSize)
            .ToListAsync(ct);
        if (old.Count == 0) return;

        db.EvpScanJobs.RemoveRange(old);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Deleted {Count} EVP scan job(s) finished more than {Days} days ago", old.Count, (int)Keep.TotalDays);
    }
}
