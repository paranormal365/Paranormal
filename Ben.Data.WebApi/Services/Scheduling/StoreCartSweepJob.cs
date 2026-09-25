using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Clears away carts nobody is coming back to (storefront S4.8): any cart untouched for 90 days,
/// and an EMPTY browser cart after 7 — the cookie lives a year, but an empty row is only noise.
/// </summary>
/// <remarks>An order that came from a cart keeps its record: the order's link to it is SetNull.</remarks>
public sealed class StoreCartSweepJob(IDbContextFactory<BenDataContext> dbFactory, TimeProvider? clock = null) : IScheduledJob
{
    public static readonly TimeSpan Idle = TimeSpan.FromDays(90);
    public static readonly TimeSpan EmptyGuestIdle = TimeSpan.FromDays(7);

    public string Name => "store-cart-sweep";

    public async Task RunAsync(CancellationToken ct)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var idle = now - Idle;
        var emptyIdle = now - EmptyGuestIdle;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.StoreCarts.Where(c => c.LastActivityUtc < idle).ExecuteDeleteAsync(ct);
        await db.StoreCarts.Where(c => c.AppUserId == null && c.LastActivityUtc < emptyIdle && !c.Items.Any()).ExecuteDeleteAsync(ct);
    }
}
