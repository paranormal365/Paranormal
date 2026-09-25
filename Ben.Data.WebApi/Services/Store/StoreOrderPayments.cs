using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// What happens to an order when Stripe says something about its payment (storefront S4.6, plan
/// §5.5 step 13 and §5.6): paid, processing, failed, cancelled, or never finished.
/// </summary>
/// <remarks>
/// <para><b>Every transition is one conditional UPDATE, and its row count referees.</b> Webhooks
/// arrive late, twice and out of order — <c>processing</c> after <c>succeeded</c>, the same
/// <c>succeeded</c> twice — and the expiry job, a cancel and a webhook can all reach one order at
/// once. The statement that finds the order still in the state it expects is the one that acts;
/// every other caller finds nothing to do.</para>
///
/// <para><b>Paid means the money moved, whatever else is wrong.</b> A payment that arrives for a
/// cancelled order, with a different amount, or on an intent this order never had, is recorded and
/// raised for a person to look at — it is never silently dropped, and never trusted blindly.</para>
///
/// <para><b>Stripe is never called inside a database transaction</b>: filing the tax happens after
/// the commit, and a failure there leaves the order paid with its letter queued, for
/// <c>StoreTaxRetryJob</c> to finish.</para>
/// </remarks>
public sealed class StoreOrderPayments(
    IDbContextFactory<BenDataContext> dbFactory, IStoreStripeGateway gateway, IStoreTaxService tax,
    StoreOrderMailer mailer, StoreAlerts alerts, ILogger<StoreOrderPayments> log, TimeProvider? clock = null)
{
    /// <summary>What trying to cancel an unpaid order came to.</summary>
    public enum CancelOutcome
    {
        /// <summary>Cancelled at Stripe, and the stock and code given back.</summary>
        Cancelled,
        /// <summary>There was never an intent: given back without asking Stripe.</summary>
        NoIntent,
        /// <summary>Stripe would not cancel — the payment is going through. Nothing was given back.</summary>
        StillProcessing,
    }

    public const string PaidAfterCancelWithStock = "Paid after the checkout was cancelled; stock was re-taken — check before packing.";
    public const string PaidAfterCancelNoStock = "Paid after the checkout was cancelled and the stock is gone — refund it from this page; no tax transaction was filed.";
    public const string ChargedOnEarlierCalculation = "Charged on an earlier calculation; totals rewritten from Stripe.";
    public static string DifferentIntent(string pi) => $"A payment naming this order arrived on a different PaymentIntent {pi}.";
    public static string AmountMismatch(long received, decimal total) => $"Stripe reports {StoreMoney.Format(received / 100m)} received; the order is {StoreMoney.Format(total)}.";

    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static readonly StoreOrderStatus[] PaidStates =
        [StoreOrderStatus.Paid, StoreOrderStatus.Packed, StoreOrderStatus.Shipped, StoreOrderStatus.Delivered, StoreOrderStatus.Refunded,
         StoreOrderStatus.PartiallyShipped];

    // ── paid ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// The one door by which an order becomes Paid: the <c>payment_intent.succeeded</c> webhook, a
    /// test checkout's simulated payment, or a $0 order paid on the spot.
    /// </summary>
    /// <param name="paymentIntentId">Null only for an order that owed nothing.</param>
    /// <param name="amountReceivedCents">Null when the event carries no amount (a checkout-session event) — then it is not checked.</param>
    /// <param name="taxCalculationId">The intent's <c>ih_tax_calc</c>: the calculation that matches what was charged.</param>
    public async Task MarkPaidAsync(Guid orderId, string? paymentIntentId, string? chargeId, string reference,
        long? amountReceivedCents, string? taxCalculationId, CancellationToken ct = default)
    {
        var now = Now;
        var attention = new List<string>();
        int orderNumber;

        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

            var flipped = await db.StoreOrders
                .Where(o => o.Id == orderId && o.Status == StoreOrderStatus.PendingPayment && o.StripePaymentIntentId == paymentIntentId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, StoreOrderStatus.Paid)
                    .SetProperty(o => o.PaidUtc, now)
                    .SetProperty(o => o.StripeChargeId, chargeId)
                    .SetProperty(o => o.ReservationExpiresUtc, (DateTime?)null)
                    .SetProperty(o => o.DateUpdated, now), ct);

            var order = await db.StoreOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order is null)
            {
                log.LogError("A payment {Reference} names store order {OrderId}, which does not exist.", reference, orderId);
                return;
            }
            orderNumber = order.OrderNumber;

            if (flipped == 0)
            {
                // (a) The same payment delivered again: already done.
                if (PaidStates.Contains(order.Status) && order.StripePaymentIntentId == paymentIntentId)
                {
                    log.LogInformation("Payment {Reference} for store order {Number} delivered again — already paid.", reference, order.OrderNumber);
                    return;
                }

                // (b) A payment on an intent this order never had: a person looks; nothing moves.
                if (paymentIntentId is not null && order.StripePaymentIntentId != paymentIntentId)
                {
                    Raise(db, order, DifferentIntent(paymentIntentId), now);
                    await db.SaveChangesAsync(ct);
                    if (tx is not null) await tx.CommitAsync(ct);
                    log.LogError("Store order {Number}: {Reason}", order.OrderNumber, DifferentIntent(paymentIntentId));
                    await alerts.OrderNeedsAttentionAsync(order.OrderNumber, order.Id, DifferentIntent(paymentIntentId), ct);
                    return;
                }

                // (c) Paid after it was cancelled: take the stock back if it is still there.
                if (order.Status == StoreOrderStatus.Cancelled)
                {
                    db.StoreOrderEvents.Add(Event(order, StoreOrderEventKind.PaymentSucceeded, now, "Paid after this order was cancelled — needs attention.",
                        StoreOrderStatus.Cancelled, StoreOrderStatus.Cancelled));
                    var retaken = true;
                    foreach (var item in order.Items.OrderBy(i => i.VariantId))
                        if (!await StoreStock.TryReserveAsync(db, item.VariantId, item.Quantity, ct)) { retaken = false; break; }

                    if (!retaken)
                    {
                        if (tx is not null) await tx.RollbackAsync(ct);
                        await RecordPaidWithoutStockAsync(orderId, now, ct);
                        await alerts.OrderNeedsAttentionAsync(order.OrderNumber, order.Id, PaidAfterCancelNoStock, ct);
                        return;
                    }

                    order.Status = StoreOrderStatus.Paid;
                    order.PaidUtc = now;
                    order.StripeChargeId = chargeId;
                    order.CancelledUtc = null;
                    order.CancellationReason = null;
                    order.ReservationExpiresUtc = null;
                    Raise(db, order, PaidAfterCancelWithStock, now);
                    attention.Add(PaidAfterCancelWithStock);
                    log.LogError("Store order {Number}: {Reason}", order.OrderNumber, PaidAfterCancelWithStock);
                }
                else
                {
                    log.LogWarning("Payment {Reference} for store order {Number} found it {Status}; nothing done.", reference, order.OrderNumber, order.Status);
                    return;
                }
            }
            else
            {
                db.StoreOrderEvents.Add(Event(order, StoreOrderEventKind.PaymentSucceeded, now, reference,
                    StoreOrderStatus.PendingPayment, StoreOrderStatus.Paid, order.Total));
            }

            // The money is taken; it is on record whatever the amount — but a wrong amount is raised.
            if (amountReceivedCents is { } received && received != StoreMoney.Cents(order.Total))
            {
                var why = AmountMismatch(received, order.Total);
                Raise(db, order, why, now);
                attention.Add(why);
                log.LogError("Store order {Number}: {Reason}", order.OrderNumber, why);
            }

            foreach (var item in order.Items)
                if (!await StoreStock.CommitSaleAsync(db, item.VariantId, item.Quantity, order.Id, now, ct))
                {
                    var why = $"The stock for {item.Sku} could not be taken as sold — check the stock log.";
                    Raise(db, order, why, now);
                    attention.Add(why);
                }

            await ClearPaidLinesFromTheCartAsync(db, order, ct);
            await mailer.QueueConfirmationAsync(db, order, order.Items.ToList(), now, ct);

            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }

        // ── outside any transaction: file the tax, and read what Stripe kept ──
        await CommitTaxAsync(orderId, taxCalculationId, attention, ct);
        await CaptureFeeAsync(orderId, ct);

        await alerts.OrderPaidAsync(orderId, ct);
        foreach (var why in attention) await alerts.OrderNeedsAttentionAsync(orderNumber, orderId, why, ct);
    }

    /// <summary>The one early return: paid, still cancelled, stock gone.</summary>
    private async Task RecordPaidWithoutStockAsync(Guid orderId, DateTime now, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.FirstAsync(o => o.Id == orderId, ct);
        order.PaidUtc = now;
        db.StoreOrderEvents.Add(Event(order, StoreOrderEventKind.PaymentSucceeded, now, "Paid after this order was cancelled — needs attention.",
            StoreOrderStatus.Cancelled, StoreOrderStatus.Cancelled, order.Total));
        Raise(db, order, PaidAfterCancelNoStock, now);
        await db.SaveChangesAsync(ct);
        log.LogError("Store order {Number}: {Reason}", order.OrderNumber, PaidAfterCancelNoStock);
    }

    /// <summary>
    /// Takes out of the cart exactly what was paid for. A line added in another tab after the
    /// checkout began stays — unpaid, and still counted in the badge.
    /// </summary>
    private static async Task ClearPaidLinesFromTheCartAsync(BenDataContext db, StoreOrder order, CancellationToken ct)
    {
        if (order.StoreCartId is not { } cartId) return;
        foreach (var item in order.Items)
        {
            var qty = item.Quantity;
            await db.StoreCartItems.Where(i => i.CartId == cartId && i.VariantId == item.VariantId && i.Quantity <= qty).ExecuteDeleteAsync(ct);
            await db.StoreCartItems.Where(i => i.CartId == cartId && i.VariantId == item.VariantId && i.Quantity > qty)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.Quantity, i => i.Quantity - qty), ct);
        }
        if (order.CouponId is { } couponId)
            await db.StoreCarts.Where(c => c.Id == cartId && c.CouponId == couponId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.CouponId, (Guid?)null), ct);
    }

    /// <summary>
    /// Files the tax. The calculation filed is the one the intent carried — the one that matches
    /// the amount charged — even when a second tab has since rewritten the order to another.
    /// </summary>
    public async Task CommitTaxAsync(Guid orderId, string? chargedCalculationId, List<string>? attention, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || order.StripeTaxTransactionId is not null || order.PaidUtc is null) return;
        if (order.Status == StoreOrderStatus.Cancelled) return;   // paid-after-cancel without stock: nothing filed
        var calculationId = chargedCalculationId ?? order.StripeTaxCalculationId;
        if (calculationId is null) return;
        var now = Now;
        string? refusedForGood = null;

        try
        {
            if (calculationId != order.StripeTaxCalculationId)
            {
                var charged = await tax.GetAsync(calculationId, ct);
                order.TaxAmount = charged.TaxCents / 100m;
                order.ShippingTaxAmount = charged.ShippingTaxCents / 100m;
                order.Total = order.Subtotal - order.DiscountAmount + order.ShippingAmount + order.TaxAmount;
                foreach (var item in order.Items)
                    if (charged.LineTaxCents.TryGetValue(item.Sku, out var lineTax)) item.TaxAmount = lineTax / 100m;
                // Each package's share of the shipping tax follows the charged figure (store sellers P5).
                var parcels = await db.StoreOrderParcels.Where(x => x.OrderId == order.Id).OrderBy(x => x.Number).ToListAsync(ct);
                var split = StoreParcelPlan.SplitShippingTax(parcels.Select(x => x.ShippingAmount).ToList(), order.ShippingTaxAmount);
                for (var i = 0; i < parcels.Count; i++) parcels[i].ShippingTaxAmount = split[i];
                order.StripeTaxCalculationId = calculationId;
                Raise(db, order, ChargedOnEarlierCalculation, now);
                attention?.Add(ChargedOnEarlierCalculation);
            }

            order.StripeTaxTransactionId = await tax.CommitAsync(calculationId, TaxReference(order), $"store-tax-{order.Id:N}", ct);
        }
        catch (StoreTaxException ex)
        {
            order.TaxCommitAttempts++;
            db.StoreOrderEvents.Add(Event(order, StoreOrderEventKind.TaxTransactionFailed, now, ex.Message));
            // A refusal Stripe will give every time: raise it and stop — the retry job skips an order
            // needing attention — rather than knocking twenty times.
            if (ex.Failure == StoreTaxFailure.Configuration)
            {
                var why = $"Stripe refused to file this order's sales tax: {ex.Message}";
                Raise(db, order, why, now);
                attention?.Add(why);
                refusedForGood = why;
            }
            log.LogError(ex, "The sales tax for store order {Number} could not be filed; the retry job will try again.", order.OrderNumber);
        }
        await db.SaveChangesAsync(ct);
        if (refusedForGood is not null && attention is null)
            await alerts.OrderNeedsAttentionAsync(order.OrderNumber, order.Id, refusedForGood, ct);
    }

    /// <summary>
    /// Reads what Stripe kept of the payment onto the order (store sellers P9). Best effort: a fee
    /// Stripe hasn't settled yet, or a failure, leaves it for <c>StoreFeeCaptureJob</c>. A refund
    /// never changes it — Stripe keeps the fee.
    /// </summary>
    public async Task<bool> CaptureFeeAsync(Guid orderId, CancellationToken ct = default)
    {
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var order = await db.StoreOrders.AsNoTracking().Where(o => o.Id == orderId)
                .Select(o => new { o.StripePaymentIntentId, o.StripeFeeAmount, o.PaidUtc }).FirstOrDefaultAsync(ct);
            if (order is null || order.PaidUtc is null || order.StripeFeeAmount is not null) return false;
            if (order.StripePaymentIntentId is not { Length: > 0 } pi || pi.StartsWith("seed_", StringComparison.Ordinal)) return false;

            if (await gateway.GetChargeFeeAsync(pi, ct) is not { } fee) return false;
            var (feeAmount, net) = (fee.FeeCents / 100m, fee.NetCents / 100m);
            return await db.StoreOrders.Where(o => o.Id == orderId && o.StripeFeeAmount == null)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.StripeFeeAmount, feeAmount).SetProperty(o => o.StripeNetAmount, net), ct) == 1;
        }
        catch (Exception ex) when (ex is HttpRequestException or Stripe.StripeException or TaskCanceledException)
        {
            log.LogWarning(ex, "Stripe's fee on store order {OrderId} could not be read yet; the sweep will ask again.", orderId);
            return false;
        }
    }

    public static string TaxReference(StoreOrder order) => $"{order.OrderNumber}-{order.Id.ToString("N")[..8]}";

    // ── processing / failed / cancelled / expired ────────────────────────────

    /// <summary>The payment is on its way (bank transfers, some cards): hold the stock — the intent cannot be cancelled now.</summary>
    public async Task RecordProcessingAsync(string paymentIntentId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var id = await OrderIdForAsync(db, paymentIntentId, ct);
        if (id is null) return;
        var rows = await db.StoreOrders.Where(o => o.Id == id && o.Status == StoreOrderStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.ReservationExpiresUtc, (DateTime?)null), ct);
        if (rows == 1)
        {
            db.StoreOrderEvents.Add(new StoreOrderEvent { Id = Guid.NewGuid(), OrderId = id.Value, Kind = StoreOrderEventKind.PaymentProcessing, OccurredUtc = Now });
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// A card was declined. The buyer may try another on the same intent — unless the
    /// reservation is already over, in which case the order is cancelled AT STRIPE first.
    /// </summary>
    public async Task RecordFailureAsync(string paymentIntentId, string? code, CancellationToken ct = default)
    {
        Guid? id;
        bool pastReservation;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            id = await OrderIdForAsync(db, paymentIntentId, ct);
            if (id is null) return;
            var order = await db.StoreOrders.FirstAsync(o => o.Id == id, ct);
            if (order.Status != StoreOrderStatus.PendingPayment) return;
            db.StoreOrderEvents.Add(Event(order, StoreOrderEventKind.PaymentFailed, Now, code ?? "declined"));
            await db.SaveChangesAsync(ct);
            pastReservation = order.ReservationExpiresUtc is null || order.ReservationExpiresUtc < Now;
        }
        if (pastReservation) await CancelPendingOrderAsync(id.Value, "Payment failed after the reservation expired", ct);
    }

    /// <summary>
    /// Ends an unpaid order: cancel the intent at Stripe first, and give the stock and code back
    /// only when Stripe agrees — or at once when there never was an intent.
    /// </summary>
    public async Task<CancelOutcome> CancelPendingOrderAsync(Guid orderId, string reason, CancellationToken ct = default,
        StoreOrderEventKind kind = StoreOrderEventKind.Cancelled)
    {
        string? pi;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var order = await db.StoreOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order is null || order.Status != StoreOrderStatus.PendingPayment || order.ReservationReleasedUtc is not null)
                return CancelOutcome.Cancelled;   // nothing left to cancel
            pi = order.StripePaymentIntentId;
        }

        if (pi is null)
        {
            await ReleaseAsync(orderId, reason, kind, ct);
            return CancelOutcome.NoIntent;
        }

        if (await gateway.CancelPaymentIntentAsync(pi, ct) == StripeCancelOutcome.StillProcessing)
        {
            log.LogInformation("Store order {OrderId}: Stripe would not cancel — the payment is going through; left for the webhook.", orderId);
            return CancelOutcome.StillProcessing;
        }
        await ReleaseAsync(orderId, reason, kind, ct);
        return CancelOutcome.Cancelled;
    }

    /// <summary>The intent was cancelled at Stripe (by us or in the dashboard).</summary>
    public async Task RecordCancelledAtStripeAsync(string paymentIntentId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await OrderIdForAsync(db, paymentIntentId, ct) is { } id)
            await ReleaseAsync(id, "Payment cancelled at Stripe", StoreOrderEventKind.Cancelled, ct);
    }

    /// <summary>
    /// Gives an unpaid order's stock and code back and marks it cancelled — once, however many of
    /// the four ways to get here arrive (our cancel, the expiry job, the cancel webhook our cancel
    /// sets off, Stripe's redelivery). The status UPDATE comes first; only its winner releases.
    /// </summary>
    public async Task<bool> ReleaseAsync(Guid orderId, string reason, StoreOrderEventKind kind = StoreOrderEventKind.Cancelled, CancellationToken ct = default)
    {
        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        var rows = await db.StoreOrders
            .Where(o => o.Id == orderId && o.ReservationReleasedUtc == null && o.Status == StoreOrderStatus.PendingPayment)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.ReservationReleasedUtc, now)
                .SetProperty(o => o.Status, StoreOrderStatus.Cancelled)
                .SetProperty(o => o.CancelledUtc, now)
                .SetProperty(o => o.CancellationReason, reason)
                .SetProperty(o => o.DateUpdated, now), ct);
        if (rows == 0)
        {
            log.LogInformation("Store order {OrderId} was already released.", orderId);
            return false;
        }
        // Its packages were never going anywhere (store sellers P7).
        await db.StoreOrderParcels.Where(x => x.OrderId == orderId && x.Status == StoreParcelStatus.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, StoreParcelStatus.Cancelled).SetProperty(x => x.CancelledUtc, now), ct);

        var items = await db.StoreOrderItems.AsNoTracking().Where(i => i.OrderId == orderId).ToListAsync(ct);
        foreach (var item in items)
            await StoreStock.ReleaseReservationAsync(db, item.VariantId, item.Quantity, ct);
        await StoreCouponReservations.ReleaseAsync(db, orderId, ct);

        db.StoreOrderEvents.Add(new StoreOrderEvent
        {
            Id = Guid.NewGuid(), OrderId = orderId, Kind = kind, Note = reason, OccurredUtc = now,
            FromStatus = StoreOrderStatus.PendingPayment, ToStatus = StoreOrderStatus.Cancelled,
        });
        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>Unpaid orders past their reservation (the expiry job's list) — none needing attention.</summary>
    public async Task<int> ExpireReservationsAsync(CancellationToken ct = default)
    {
        List<Guid> expired;
        var now = Now;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            expired = await db.StoreOrders.AsNoTracking()
                .Where(o => o.Status == StoreOrderStatus.PendingPayment && o.ReservationReleasedUtc == null
                         && o.ReservationExpiresUtc != null && o.ReservationExpiresUtc < now && !o.NeedsAttention)
                .Select(o => o.Id).ToListAsync(ct);

        // One at a time, and one that fails is left for the next pass — never the rest with it. A single
        // payment Stripe would not answer about used to stop every release, so holds piled up until the
        // three-open-checkouts limit refused every buyer on the address (the real-Stripe run, 09/24).
        var done = 0;
        foreach (var id in expired)
        {
            try
            {
                if (await CancelPendingOrderAsync(id, "Checkout expired", ct, StoreOrderEventKind.ReservationExpired) != CancelOutcome.StillProcessing)
                    done++;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Store order {OrderId}: its expired checkout could not be released this pass; trying again next minute.", id);
            }
        }
        return done;
    }

    private static async Task<Guid?> OrderIdForAsync(BenDataContext db, string paymentIntentId, CancellationToken ct)
        => await db.StoreOrders.AsNoTracking().Where(o => o.StripePaymentIntentId == paymentIntentId)
            .Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);

    private static void Raise(BenDataContext db, StoreOrder order, string reason, DateTime now)
    {
        order.NeedsAttention = true;
        order.AttentionReason = reason;
        db.StoreOrderEvents.Add(Event(order, StoreOrderEventKind.AttentionRaised, now, reason));
    }

    private static StoreOrderEvent Event(StoreOrder order, StoreOrderEventKind kind, DateTime now, string? note = null,
        StoreOrderStatus? from = null, StoreOrderStatus? to = null, decimal? amount = null) => new()
    {
        Id = Guid.NewGuid(), OrderId = order.Id, Kind = kind, Note = note, OccurredUtc = now, FromStatus = from, ToStatus = to, Amount = amount,
    };
}
