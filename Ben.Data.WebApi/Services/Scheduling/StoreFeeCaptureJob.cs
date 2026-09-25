using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Reads Stripe's fee onto paid orders that don't have it yet (store sellers, backlog 251, P9) — the
/// charge's balance transaction settles a little after the payment, so the read at payment time
/// can find nothing.
/// </summary>
/// <remarks>
/// Only orders paid through Stripe (never a demo seed or a free order), newest first, fifty a run,
/// and only for a fortnight after payment — a fee not there by then is not coming, and the order's
/// economics panel says it isn't known.
/// </remarks>
public sealed class StoreFeeCaptureJob(IDbContextFactory<BenDataContext> dbFactory, StoreOrderPayments payments) : IScheduledJob
{
    internal const int BatchSize = 50;
    internal static readonly TimeSpan GiveUpAfter = TimeSpan.FromDays(14);

    public string Name => "store-fee-capture";

    public async Task RunAsync(CancellationToken ct)
    {
        var since = DateTime.UtcNow - GiveUpAfter;
        List<Guid> missing;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            missing = await db.StoreOrders.AsNoTracking()
                .Where(o => o.PaidUtc != null && o.PaidUtc > since && o.StripeFeeAmount == null && o.Total > 0
                         && o.StripePaymentIntentId != null && !o.StripePaymentIntentId.StartsWith("seed_"))
                .OrderByDescending(o => o.PaidUtc).Select(o => o.Id).Take(BatchSize).ToListAsync(ct);

        foreach (var id in missing)
            await payments.CaptureFeeAsync(id, ct);
    }
}
