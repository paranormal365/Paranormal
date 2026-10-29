using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Clears the room holds whose twenty minutes are up (LiDAR plan, phase 1).
/// </summary>
/// <remarks>
/// Housekeeping only. An expired hold already means nothing — every reader asks for holds that
/// have not expired — so this keeps the table small and nothing waits on it.
/// </remarks>
public sealed class PlaceScanHoldSweepJob(
    IDbContextFactory<BenDataContext> dbFactory, ILogger<PlaceScanHoldSweepJob> logger,
    TimeProvider? clock = null) : IScheduledJob
{
    public string Name => "place-scan-hold-sweep";

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

        var swept = await db.PlaceScanHolds.Where(h => h.ExpiresUtc <= now).ExecuteDeleteAsync(ct);
        if (swept > 0) logger.LogInformation("Cleared {Count} expired room scan holds.", swept);
    }
}
