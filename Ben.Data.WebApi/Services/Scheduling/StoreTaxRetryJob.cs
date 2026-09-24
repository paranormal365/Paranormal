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
public sealed class StoreTaxRetryJob(IDbContextFactory<BenDataContext> dbFactory, StoreOrderPayments payments,
    StoreRefundService? refunds = null) : IScheduledJob
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

        // Then the reversals still owed (S5.3): refunds made before the order's own filing landed,
        // and reversals whose first try failed. Each refund files at most one, keyed by its id.
        if (refunds is null) return;
        List<Guid> owed;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            owed = await db.StoreOrders.AsNoTracking()
                .Where(o => o.StripeTaxTransactionId != null
                         && o.Refunds.Any(r => r.Status == StoreRefundStatus.Succeeded && r.StripeTaxReversalId == null))
                .Select(o => o.Id).Take(BatchSize).ToListAsync(ct);
        foreach (var id in owed)
            await refunds.ReverseWaitingRefundsAsync(id, ct);
    }
}
