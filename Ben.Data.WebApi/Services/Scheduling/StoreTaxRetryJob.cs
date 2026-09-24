using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Scheduling;

/// <summary>
/// Files the sales tax of paid orders whose filing failed (storefront S4.8), with the same
/// idempotency key each time so Stripe files it once.
/// </summary>
/// <remarks>
/// Stops for an order after <see cref="MaxAttempts"/>, or at once when Stripe refuses it for good
/// (a configuration refusal raises attention on the order) — either way a person is told, and the
/// job does not keep knocking.
/// </remarks>
public sealed class StoreTaxRetryJob(IDbContextFactory<BenDataContext> dbFactory, StoreOrderPayments payments) : IScheduledJob
{
    public const int MaxAttempts = 20;
    internal const int BatchSize = 50;

    public string Name => "store-tax-retry";

    public async Task RunAsync(CancellationToken ct)
    {
        List<Guid> waiting;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            waiting = await db.StoreOrders.AsNoTracking()
                .Where(o => o.PaidUtc != null && o.StripeTaxTransactionId == null && o.StripeTaxCalculationId != null
                         && o.Status != StoreOrderStatus.Cancelled && !o.NeedsAttention && o.TaxCommitAttempts < MaxAttempts)
                .OrderBy(o => o.PaidUtc).Select(o => o.Id).Take(BatchSize).ToListAsync(ct);

        foreach (var id in waiting)
            await payments.CommitTaxAsync(id, null, null, ct);
    }
}
