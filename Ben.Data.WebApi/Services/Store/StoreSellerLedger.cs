using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// What sellers are owed and what they have been paid (store sellers, backlog 251, P10).
/// </summary>
/// <remarks>
/// <para><b>Earned when it ships.</b> A package's units are earned — cost basis + asking price each,
/// as fixed at checkout — when the package ships, with the label credit beside them. A refund
/// before then takes units away before anything is earned; a refund after takes the earning back
/// with a negative line. A refund by amount is the store's to bear (Ben's plan default); a staff
/// adjustment can charge a seller for one, with a note.</para>
///
/// <para><b>Paid by claiming.</b> Recording a payment claims every unpaid line up to a date in one
/// conditional update, then totals what it claimed — in C#, from the rows, so the test database can
/// run it too. The admin says what they expect to pay; if the lines changed since the page was
/// opened, the payment is refused with the new figure rather than recorded wrong. A payment never
/// claims a balance that isn't positive: a seller who owes the store carries it forward.</para>
/// </remarks>
public static class StoreSellerLedger
{
    public const string NothingOwed = "Nothing is owed up to that date.";

    public static string OwesTheStore(decimal balance)
        => $"Up to that date the seller owes the store {StoreMoney.Format(-balance)} — it carries forward to their next earnings.";

    public static string AmountChanged(decimal now)
        => $"What's owed changed since you opened this — it's now {StoreMoney.Format(now)}. Check it and record the payment again.";

    // ── writing what is earned ───────────────────────────────────────────────

    /// <summary>
    /// A seller's package has shipped: its units and its label, earned now. Added to the context in the
    /// shipping transaction; a second call for the same package adds nothing.
    /// </summary>
    public static async Task RecordShipmentAsync(BenDataContext db, StoreOrderParcel parcel, IReadOnlyList<StoreOrderItem> items, DateTime now, CancellationToken ct)
    {
        if (parcel.SellerAppUserId is not { } seller) return;

        var itemIds = items.Select(i => i.Id).ToList();
        var already = await db.StoreSellerEarnings.Where(e => e.Kind == StoreEarningKind.Sale && e.OrderItemId != null && itemIds.Contains(e.OrderItemId.Value))
            .Select(e => e.OrderItemId!.Value).ToListAsync(ct);
        foreach (var item in items.Where(i => !already.Contains(i.Id)))
        {
            var units = item.Quantity - item.QuantityRefunded;
            if (units <= 0 || item.UnitSellerEarning <= 0m) continue;
            db.StoreSellerEarnings.Add(new StoreSellerEarning
            {
                Id = Guid.NewGuid(), SellerAppUserId = seller, Kind = StoreEarningKind.Sale, Amount = StoreMoney.Round(item.UnitSellerEarning * units),
                Units = units, OrderId = parcel.OrderId, ParcelId = parcel.Id, OrderItemId = item.Id, ProductId = item.ProductId,
                Note = item.ProductName + (string.IsNullOrWhiteSpace(item.VariantName) ? "" : $" — {item.VariantName}"), OccurredUtc = now,
            });
        }

        if (parcel.SellerShippingCredit > 0m && !await db.StoreSellerEarnings.AnyAsync(e => e.Kind == StoreEarningKind.Shipping && e.ParcelId == parcel.Id, ct))
            db.StoreSellerEarnings.Add(new StoreSellerEarning
            {
                Id = Guid.NewGuid(), SellerAppUserId = seller, Kind = StoreEarningKind.Shipping, Amount = parcel.SellerShippingCredit,
                OrderId = parcel.OrderId, ParcelId = parcel.Id, Note = $"Label for package {parcel.Number}", OccurredUtc = now,
            });
    }

    /// <summary>
    /// A refund has gone through: units of a seller's that had already shipped — and been earned —
    /// take their earning back. Units refunded before shipping were never earned, so write nothing.
    /// </summary>
    public static async Task RecordRefundAsync(BenDataContext db, Guid refundId, DateTime now, CancellationToken ct)
    {
        var lines = await db.StoreRefundItems.AsNoTracking().Where(r => r.RefundId == refundId)
            .Select(r => new { r.OrderItemId, r.Quantity, Item = r.OrderItem }).ToListAsync(ct);
        foreach (var line in lines)
        {
            var sale = await db.StoreSellerEarnings.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Kind == StoreEarningKind.Sale && e.OrderItemId == line.OrderItemId, ct);
            if (sale is null) continue;   // not shipped yet, or the store's own
            if (await db.StoreSellerEarnings.AnyAsync(e => e.Kind == StoreEarningKind.Refund && e.RefundId == refundId && e.OrderItemId == line.OrderItemId, ct)) continue;
            db.StoreSellerEarnings.Add(new StoreSellerEarning
            {
                Id = Guid.NewGuid(), SellerAppUserId = sale.SellerAppUserId, Kind = StoreEarningKind.Refund,
                Amount = -StoreMoney.Round(line.Item.UnitSellerEarning * line.Quantity), Units = -line.Quantity,
                OrderId = sale.OrderId, ParcelId = sale.ParcelId, OrderItemId = line.OrderItemId, RefundId = refundId, ProductId = sale.ProductId,
                Note = $"Refunded: {line.Item.ProductName}", OccurredUtc = now,
            });
        }
    }

    /// <summary>A correction by the store's staff: either sign, never zero, always with a note.</summary>
    public static async Task<string?> AdjustAsync(BenDataContext db, Guid seller, decimal amount, string? note, Guid admin, DateTime now, CancellationToken ct)
    {
        var text = note?.Trim();
        if (string.IsNullOrEmpty(text)) return "Say what the adjustment is for — the seller reads it.";
        if (text.Length > StoreSellerPayout.MaxNoteLength) return $"A note is {StoreSellerPayout.MaxNoteLength} characters at most.";
        if (amount == 0m || amount != StoreMoney.Round(amount)) return "An adjustment is dollars and cents, and not $0.00.";
        db.StoreSellerEarnings.Add(new StoreSellerEarning
        {
            Id = Guid.NewGuid(), SellerAppUserId = seller, Kind = StoreEarningKind.Adjustment, Amount = amount, Note = text,
            OccurredUtc = now, CreatedByAppUserId = admin,
        });
        await db.SaveChangesAsync(ct);
        return null;
    }

    // ── paying ───────────────────────────────────────────────────────────────

    /// <summary>The unpaid lines a payment up to <paramref name="cutoff"/> would claim, totalled.</summary>
    public static async Task<decimal> OwedUpToAsync(BenDataContext db, Guid seller, DateTime cutoff, CancellationToken ct)
        => (await db.StoreSellerEarnings.AsNoTracking().Where(e => e.SellerAppUserId == seller && e.PayoutId == null && e.OccurredUtc <= cutoff)
            .Select(e => e.Amount).ToListAsync(ct)).Sum();

    /// <summary>
    /// Records a payment made outside the site: claims every unpaid line up to <paramref name="cutoff"/>
    /// and totals them. Refused, with nothing claimed, when the total isn't what the admin expected or
    /// isn't positive.
    /// </summary>
    public static async Task<(StoreSellerPayout? Payout, string? Refusal)> RecordPaymentAsync(
        BenDataContext db, Guid seller, DateTime cutoff, decimal expected, DateTime paidOn, string? reference, Guid admin, DateTime now, CancellationToken ct)
    {
        var text = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        if (text?.Length > StoreSellerPayout.MaxNoteLength) return (null, $"A reference is {StoreSellerPayout.MaxNoteLength} characters at most.");

        var relational = db.Database.IsRelational();
        await using var tx = relational ? await db.Database.BeginTransactionAsync(ct) : null;

        var payout = new StoreSellerPayout
        {
            Id = Guid.NewGuid(), SellerAppUserId = seller, CutoffUtc = cutoff, PaidOnUtc = paidOn, Reference = text,
            RecordedByAppUserId = admin, RecordedUtc = now,
        };
        db.StoreSellerPayouts.Add(payout);
        await db.SaveChangesAsync(ct);

        var payoutId = payout.Id;
        await db.StoreSellerEarnings.Where(e => e.SellerAppUserId == seller && e.PayoutId == null && e.OccurredUtc <= cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.PayoutId, (Guid?)payoutId), ct);
        var claimed = (await db.StoreSellerEarnings.AsNoTracking().Where(e => e.PayoutId == payoutId).Select(e => e.Amount).ToListAsync(ct)).Sum();

        string? refusal = claimed switch
        {
            0m => NothingOwed,
            < 0m => OwesTheStore(claimed),
            _ when claimed != expected => AmountChanged(claimed),
            _ => null,
        };
        if (refusal is not null)
        {
            if (tx is not null) await tx.RollbackAsync(ct);
            else
            {
                await db.StoreSellerEarnings.Where(e => e.PayoutId == payoutId).ExecuteUpdateAsync(s => s.SetProperty(e => e.PayoutId, (Guid?)null), ct);
                db.StoreSellerPayouts.Remove(payout);
                await db.SaveChangesAsync(ct);
            }
            return (null, refusal);
        }

        payout.Amount = claimed;
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return (payout, null);
    }

    /// <summary>A payment recorded in error: voided, and its lines owed again.</summary>
    public static async Task<string?> VoidAsync(BenDataContext db, Guid payoutId, string? reason, Guid admin, DateTime now, CancellationToken ct)
    {
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text)) return "Say why it is being voided.";
        var relational = db.Database.IsRelational();
        await using var tx = relational ? await db.Database.BeginTransactionAsync(ct) : null;

        var voided = await db.StoreSellerPayouts.Where(p => p.Id == payoutId && p.VoidedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.VoidedUtc, now).SetProperty(p => p.VoidedByAppUserId, admin)
                .SetProperty(p => p.VoidReason, text), ct);
        if (voided == 0) return "That payment has already been voided.";
        await db.StoreSellerEarnings.Where(e => e.PayoutId == payoutId).ExecuteUpdateAsync(s => s.SetProperty(e => e.PayoutId, (Guid?)null), ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return null;
    }

    // ── reading ──────────────────────────────────────────────────────────────

    /// <summary>The cutoff a payment defaults to: today less the returns window, so a return can still come back first.</summary>
    public static DateTime DefaultCutoff(DateTime now, int returnsWindowDays) => now.Date.AddDays(-returnsWindowDays).AddDays(1).AddTicks(-1);

    /// <summary>One seller's earnings, whole: what is owed, payable now, waiting, paid, and the breakdowns.</summary>
    public static async Task<SellerEarningsRecord> SummaryAsync(BenDataContext db, Guid seller, DateTime cutoff, CancellationToken ct)
    {
        var rows = await db.StoreSellerEarnings.AsNoTracking().Where(e => e.SellerAppUserId == seller)
            .OrderByDescending(e => e.OccurredUtc).ToListAsync(ct);
        var payouts = await db.StoreSellerPayouts.AsNoTracking().Where(p => p.SellerAppUserId == seller)
            .OrderByDescending(p => p.PaidOnUtc).ToListAsync(ct);
        var orderIds = rows.Where(r => r.OrderId is not null).Select(r => r.OrderId!.Value).Distinct().ToList();
        var numbers = await db.StoreOrders.AsNoTracking().Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, o.OrderNumber, o.PaidUtc }).ToDictionaryAsync(o => o.Id, ct);
        var productIds = rows.Where(r => r.ProductId is not null).Select(r => r.ProductId!.Value).Distinct().ToList();
        var products = await db.StoreProducts.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Name }).ToDictionaryAsync(p => p.Id, p => p.Name, ct);

        var unpaid = rows.Where(r => r.PayoutId == null).ToList();
        var paidPayouts = payouts.Where(p => p.VoidedUtc is null).ToList();

        return new SellerEarningsRecord(
            Owed: unpaid.Sum(r => r.Amount),
            PayableNow: unpaid.Where(r => r.OccurredUtc <= cutoff).Sum(r => r.Amount),
            Waiting: unpaid.Where(r => r.OccurredUtc > cutoff).Sum(r => r.Amount),
            PaidToDate: paidPayouts.Sum(p => p.Amount),
            Cutoff: cutoff,
            Items: rows.Where(r => r.ProductId is not null).GroupBy(r => r.ProductId!.Value)
                .Select(g => new SellerEarningsItemRecord(g.Key, products.GetValueOrDefault(g.Key) ?? "An item no longer listed",
                    g.Sum(r => r.Units ?? 0), g.Sum(r => r.Amount))).OrderByDescending(i => i.Earned).ToList(),
            Orders: rows.Where(r => r.OrderId is not null).GroupBy(r => r.OrderId!.Value)
                .Select(g => new SellerEarningsOrderRecord(g.Key, numbers.GetValueOrDefault(g.Key)?.OrderNumber ?? 0,
                    g.Min(r => r.OccurredUtc), g.Sum(r => r.Amount), g.All(r => r.PayoutId != null)))
                .OrderByDescending(o => o.EarnedUtc).ToList(),
            Lines: rows.Take(500).Select(r => new SellerEarningLineRecord(r.Id, r.Kind, r.Amount, r.Units, r.Note,
                r.OrderId is { } o ? numbers.GetValueOrDefault(o)?.OrderNumber : null, r.OccurredUtc, r.PayoutId)).ToList(),
            Payouts: payouts.Select(p => new SellerPayoutRecord(p.Id, p.Amount, p.CutoffUtc, p.PaidOnUtc, p.Reference, p.VoidedUtc, p.VoidReason)).ToList(),
            Years: rows.GroupBy(r => r.OccurredUtc.Year).Select(g => new SellerEarningsYearRecord(g.Key, g.Sum(r => r.Amount),
                    paidPayouts.Where(p => p.PaidOnUtc.Year == g.Key).Sum(p => p.Amount)))
                .Union(paidPayouts.Select(p => p.PaidOnUtc.Year).Distinct().Where(y => rows.All(r => r.OccurredUtc.Year != y))
                    .Select(y => new SellerEarningsYearRecord(y, 0m, paidPayouts.Where(p => p.PaidOnUtc.Year == y).Sum(p => p.Amount))))
                .OrderByDescending(y => y.Year).ToList());
    }
}
