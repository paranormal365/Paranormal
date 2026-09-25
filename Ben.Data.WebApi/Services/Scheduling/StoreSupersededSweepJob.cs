using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Takes a replaced version off sale once it has sold out (store sellers, backlog 251, P13) — the
/// second half of the "sell out what's left" policy. Its page stays, saying what replaced it.
/// </summary>
public sealed class StoreSupersededSweepJob(IDbContextFactory<BenDataContext> dbFactory) : IScheduledJob
{
    public string Name => "store-superseded-sweep";

    public async Task RunAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await StoreProductVersions.SweepSoldOutAsync(db, DateTime.UtcNow, ct);
    }
}
