using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Data.WebApi.Services.Billing.StripeIntegration;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>How a refund request came out, for the controller to turn into a status.</summary>
public enum StoreRefundOutcomeKind { Completed, Pending, Refused, NotFound, Unavailable, StripeRefused }

public sealed record StoreRefundAttempt(StoreRefundOutcomeKind Kind, Guid? RefundId, string? Sentence)
{
    public static StoreRefundAttempt Refused(string sentence) => new(StoreRefundOutcomeKind.Refused, null, sentence);
}

/// <summary>
/// Refunds (storefront S5.3, plan §5.7): money back through Stripe, the stock and the tax filing
/// following only once Stripe says the money has actually gone.
/// </summary>
/// <remarks>
/// <para><b>The row is saved before Stripe is asked.</b> A refund row exists (Pending) before the
/// call, so a crash between Stripe's "yes" and our save leaves a row the listing can adopt — never a
/// refund Stripe made that the order does not know about.</para>
///
/// <para><b>Nothing moves until <c>succeeded</c>.</b> A refund Stripe is still processing restocks
/// nothing, sends no letter and files no reversal; it finishes on its webhook (or a retry that finds
/// it in Stripe's listing). <see cref="CompleteAsync"/> is one conditional update, so the admin's
/// branch and the webhook can both arrive and only one of them does the work.</para>
///
/// <para><b>A key is never re-sent.</b> Inside Stripe's 24 hours a repeated idempotency key replays
/// the first answer (a refusal stays a refusal); after it, the same key is a SECOND refund. A retry
/// first looks for the refund in Stripe's list by our id in its metadata, and only when it is not
/// there bumps <see cref="StoreRefund.Attempt"/> — saved first — and asks with a fresh key.</para>
///
/// <para><b>The tax filing follows outside the transaction.</b> A reversal (full only when it is the
/// first and the whole order; otherwise a negative flat amount) is filed after the refund commits; if
/// the order's own tax transaction is not filed yet, StoreTaxRetryJob reverses waiting refunds right
/// after it lands (<see cref="ReverseWaitingRefundsAsync"/>).</para>
/// </remarks>
public sealed class StoreRefundService(
    IDbContextFactory<BenDataContext> dbFactory, IStoreStripeGateway gateway, IStoreTaxService tax,
    StoreOrderMailer mailer, StoreAlerts alerts, ILogger<StoreRefundService> log, TimeProvider? clock = null)
{
    public const string DashboardReason = "Refunded from the Stripe dashboard";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    /// <summary>The event note that marks a refund made to cancel the order (so completing it cancels).</summary>
    public static string CancellationPendingNote(Guid refundId) => $"Cancellation pending: {refundId:N}";

    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    public static string IdempotencyKey(StoreRefund refund) => $"store-refund-{refund.Id:N}-{refund.Attempt}";

    /// <summary>What is still refundable: the total less refunds made and refunds going through.</summary>
    public static decimal Remaining(StoreOrder order, IEnumerable<StoreRefund> refunds)
        => order.Total - refunds.Where(r => r.Status is StoreRefundStatus.Succeeded or StoreRefundStatus.Pending).Sum(r => r.Amount);

    /// <summary>Units of an order line still refundable: less those refunded and those in a pending refund.</summary>
    public static int RefundableUnits(StoreOrderItem item, IEnumerable<StoreRefund> refunds)
        => item.Quantity - item.QuantityRefunded
           - refunds.Where(r => r.Status == StoreRefundStatus.Pending).SelectMany(r => r.Items).Where(i => i.OrderItemId == item.Id).Sum(i => i.Quantity);

    // ── Asking ───────────────────────────────────────────────────────────────

    /// <param name="cancelling">This refund cancels the order: completing it sets Cancelled (see <see cref="CancellationPendingNote"/>).</param>
    public async Task<StoreRefundAttempt> RefundAsync(Guid orderId, StoreRefundRequest request, Guid? actor,
        bool cancelling = false, CancellationToken ct = default)
    {
        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.Include(o => o.Items).Include(o => o.Refunds).ThenInclude(r => r.Items)
            .AsSplitQuery().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return new StoreRefundAttempt(StoreRefundOutcomeKind.NotFound, null, null);

        if (order.PaidUtc is null) return StoreRefundAttempt.Refused(StoreOrderDeskSentences.OnlyPaidRefunded);
        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason)) return StoreRefundAttempt.Refused(StoreOrderDeskSentences.RefundNeedsReason);
        if (order.StripePaymentIntentId is not { Length: > 0 } pi || pi.StartsWith("seed_", StringComparison.Ordinal))
            return StoreRefundAttempt.Refused(StoreOrderDeskSentences.NotPaidThroughStripe);
        if (!gateway.IsConfigured) return new StoreRefundAttempt(StoreRefundOutcomeKind.Unavailable, null, StoreCheckoutSentences.PaymentsNotSetUp);

        var lines = (request.Items ?? []).Where(l => l.Quantity > 0).ToList();
        if (request.Amount is not null && lines.Count > 0) return StoreRefundAttempt.Refused(StoreOrderDeskSentences.AmountOrItems);
        if (request.Amount is null && lines.Count == 0) return StoreRefundAttempt.Refused(StoreOrderDeskSentences.AmountOrItems);
        if (request.Amount is not null && request.Restock) return StoreRefundAttempt.Refused(StoreOrderDeskSentences.AmountCannotRestock);

        var remaining = Remaining(order, order.Refunds);
        var refund = new StoreRefund
        {
            Id = Guid.NewGuid(), OrderId = order.Id, Reason = reason, Restock = request.Restock, Status = StoreRefundStatus.Pending,
            Attempt = 1, RequestedByAppUserId = actor, DateCreated = now,
        };

        if (request.Amount is { } amount)
        {
            amount = StoreMoney.Round(amount);
            if (amount <= 0) return StoreRefundAttempt.Refused(StoreOrderDeskSentences.AmountPositive);
            refund.Amount = amount;
        }
        else
        {
            foreach (var line in lines.GroupBy(l => l.OrderItemId).Select(g => new StoreRefundLine(g.Key, g.Sum(l => l.Quantity))))
            {
                var item = order.Items.FirstOrDefault(i => i.Id == line.OrderItemId);
                if (item is null) return StoreRefundAttempt.Refused("That item isn't on this order.");
                var left = RefundableUnits(item, order.Refunds);
                if (line.Quantity > left) return StoreRefundAttempt.Refused(StoreOrderDeskSentences.OnlyNLeft(left, item.ProductName));
                var value = LineValue(item, line.Quantity, order.Refunds);
                refund.Items.Add(new StoreRefundItem { Id = Guid.NewGuid(), RefundId = refund.Id, OrderItemId = item.Id, Quantity = line.Quantity, Amount = value });
                refund.Amount += value;
            }
            // Cancelling gives everything back — shipping included — not just the items' share.
            refund.Amount = cancelling ? remaining : Math.Min(refund.Amount, remaining);
        }
        if (refund.Amount > remaining) return StoreRefundAttempt.Refused(StoreOrderDeskSentences.MoreThanRemaining(remaining));

        // 1. The row first.
        db.StoreRefunds.Add(refund);
        db.StoreOrderEvents.Add(Event(order.Id, StoreOrderEventKind.RefundRequested, reason, refund.Amount, actor, now));
        if (cancelling)
            db.StoreOrderEvents.Add(Event(order.Id, StoreOrderEventKind.NoteAdded, CancellationPendingNote(refund.Id), null, actor, now));
        await db.SaveChangesAsync(ct);

        // 2. Stripe.
        return await AskStripeAsync(order, refund, actor, ct);
    }

    /// <summary>What <paramref name="quantity"/> units of a line are worth back: their share of the line after its discount, with their tax.</summary>
    /// <remarks>The last units of a line take whatever of it is left, so the pennies always add up to the line.</remarks>
    public static decimal LineValue(StoreOrderItem item, int quantity, IEnumerable<StoreRefund> refunds)
    {
        var lineValue = item.LineTotal - item.LineDiscount + item.TaxAmount;
        var alreadyUnits = item.QuantityRefunded + refunds.Where(r => r.Status == StoreRefundStatus.Pending)
            .SelectMany(r => r.Items).Where(i => i.OrderItemId == item.Id).Sum(i => i.Quantity);
        if (alreadyUnits + quantity >= item.Quantity)
        {
            var alreadyAmount = refunds.Where(r => r.Status is StoreRefundStatus.Succeeded or StoreRefundStatus.Pending)
                .SelectMany(r => r.Items).Where(i => i.OrderItemId == item.Id).Sum(i => i.Amount);
            return Math.Max(0, lineValue - alreadyAmount);
        }
        return StoreMoney.Round(lineValue / item.Quantity * quantity);
    }

    private async Task<StoreRefundAttempt> AskStripeAsync(StoreOrder order, StoreRefund refund, Guid? actor, CancellationToken ct)
    {
        StoreRefundOutcome answer;
        try
        {
            answer = await gateway.CreateRefundAsync(new StoreRefundSpec(
                order.StripePaymentIntentId!, StoreMoney.Cents(refund.Amount), "requested_by_customer",
                new Dictionary<string, string> { [StoreStripeKeys.Refund] = refund.Id.ToString(), [StoreStripeKeys.Order] = order.Id.ToString() },
                IdempotencyKey(refund)), ct);
        }
        catch (Exception ex) when (ex is StoreStripeRefusedException or Stripe.StripeException or HttpRequestException or TaskCanceledException)
        {
            var why = ex is StoreStripeRefusedException refused ? refused.Message : ex is Stripe.StripeException se ? se.StripeError?.Message ?? se.Message : "Stripe didn't answer";
            await MarkFailedAsync(refund.Id, why, ct);
            return new StoreRefundAttempt(StoreRefundOutcomeKind.StripeRefused, refund.Id, StoreOrderDeskSentences.StripeRefused(why));
        }
        return await BranchAsync(refund.Id, answer, actor, ct);
    }

    private async Task<StoreRefundAttempt> BranchAsync(Guid refundId, StoreRefundOutcome answer, Guid? actor, CancellationToken ct)
    {
        switch (answer.Status)
        {
            case "succeeded":
                await CompleteAsync(refundId, answer.StripeRefundId, actor, ct);
                return new StoreRefundAttempt(StoreRefundOutcomeKind.Completed, refundId, null);
            case "pending" or "requires_action":
                await RecordPendingAsync(refundId, answer.StripeRefundId, ct);
                return new StoreRefundAttempt(StoreRefundOutcomeKind.Pending, refundId, StoreOrderDeskSentences.RefundPending);
            default:
                var why = answer.FailureReason ?? answer.Status;
                await MarkFailedAsync(refundId, why, ct, answer.StripeRefundId);
                return new StoreRefundAttempt(StoreRefundOutcomeKind.StripeRefused, refundId, StoreOrderDeskSentences.StripeRefused(why));
        }
    }

    // ── What Stripe said ─────────────────────────────────────────────────────

    /// <summary>
    /// The money has gone back: restock, count it, cancel or mark Refunded when that is the end of
    /// it, and queue the letter — in one transaction, once. False when somebody got there first.
    /// </summary>
    public async Task<bool> CompleteAsync(Guid refundId, string stripeRefundId, Guid? actor, CancellationToken ct = default)
    {
        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await using (var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            var rows = await db.StoreRefunds
                .Where(r => r.Id == refundId && r.Status == StoreRefundStatus.Pending && (r.StripeRefundId == null || r.StripeRefundId == stripeRefundId))
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, StoreRefundStatus.Succeeded)
                    .SetProperty(r => r.StripeRefundId, stripeRefundId).SetProperty(r => r.CompletedUtc, now), ct);
            if (rows == 0) return false;   // the webhook or the admin's branch already finished it

            var refund = await db.StoreRefunds.AsNoTracking().Include(r => r.Items).ThenInclude(i => i.OrderItem).SingleAsync(r => r.Id == refundId, ct);
            var order = await db.StoreOrders.Include(o => o.Items).SingleAsync(o => o.Id == refund.OrderId, ct);
            var cancelling = await db.StoreOrderEvents.AnyAsync(e => e.OrderId == order.Id && e.Note == CancellationPendingNote(refundId), ct);
            var movement = cancelling ? StoreStockReason.Cancelled : StoreStockReason.Refunded;

            var lines = new List<(string, int)>();
            foreach (var line in refund.Items)
            {
                var restocked = 0;
                if (refund.Restock && await StoreStock.RestockAsync(db, line.OrderItem.VariantId, line.Quantity, movement, order.Id, refund.Id, actor, now, ct))
                    restocked = line.Quantity;
                await db.StoreOrderItems.Where(i => i.Id == line.OrderItemId)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.QuantityRefunded, i => i.QuantityRefunded + line.Quantity)
                        .SetProperty(i => i.QuantityRestocked, i => i.QuantityRestocked + restocked), ct);
                lines.Add((line.OrderItem.ProductName, line.Quantity));
                if (restocked > 0)
                    db.StoreOrderEvents.Add(Event(order.Id, StoreOrderEventKind.Restocked, $"{restocked} × {line.OrderItem.ProductName} back on the shelf", null, actor, now));
            }

            var before = order.Status;
            order.RefundedAmount += refund.Amount;
            order.DateUpdated = now;
            if (cancelling)
            {
                order.Status = StoreOrderStatus.Cancelled;
                order.CancelledUtc = now;
                order.CancellationReason ??= refund.Reason;
            }
            else if (order.RefundedAmount >= order.Total && order.Status is not StoreOrderStatus.Cancelled)
            {
                order.Status = StoreOrderStatus.Refunded;
            }
            db.StoreOrderEvents.Add(new StoreOrderEvent
            {
                Id = Guid.NewGuid(), OrderId = order.Id, Kind = StoreOrderEventKind.RefundSucceeded, FromStatus = before,
                ToStatus = order.Status == before ? null : order.Status, Note = refund.Reason, Amount = refund.Amount,
                ActorAppUserId = actor, OccurredUtc = now,
            });

            await mailer.QueueRefundedAsync(db, order, refund.Amount, refund.Reason, lines, now, ct);
            if (StoreOrderScrub.IsTerminal(order.Status) && order.PendingAnonymisationSinceUtc is not null)
                await StoreOrderScrub.ScrubAsync(db, order, null, now, ct);
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }

        await ReverseTaxAsync(refundId, ct);
        await alerts.OrderRefundedAsync(refundId, ct);
        return true;
    }

    /// <summary>Stripe has it and is still processing: note its id; nothing else moves.</summary>
    public async Task RecordPendingAsync(Guid refundId, string stripeRefundId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.StoreRefunds.Where(r => r.Id == refundId && r.Status == StoreRefundStatus.Pending && r.StripeRefundId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.StripeRefundId, stripeRefundId), ct);
        if (rows == 0) return;
        var orderId = await db.StoreRefunds.Where(r => r.Id == refundId).Select(r => r.OrderId).SingleAsync(ct);
        db.StoreOrderEvents.Add(Event(orderId, StoreOrderEventKind.RefundPending, "Stripe is processing the refund", null, null, Now));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>It did not go through: the row says why; the stock never moved, no letter, no reversal; SuperAdmins hear.</summary>
    public async Task MarkFailedAsync(Guid refundId, string reason, CancellationToken ct = default, string? stripeRefundId = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.StoreRefunds.Where(r => r.Id == refundId && r.Status == StoreRefundStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, StoreRefundStatus.Failed).SetProperty(r => r.FailureReason, reason)
                .SetProperty(r => r.StripeRefundId, r => stripeRefundId ?? r.StripeRefundId), ct);
        if (rows == 0) return;
        var order = await db.StoreRefunds.Where(r => r.Id == refundId).Select(r => new { r.OrderId, r.Order.OrderNumber }).SingleAsync(ct);
        db.StoreOrderEvents.Add(Event(order.OrderId, StoreOrderEventKind.RefundFailed, reason, null, null, Now));
        await db.SaveChangesAsync(ct);
        await alerts.RefundFailedAsync(order.OrderNumber, order.OrderId, reason, ct);
    }

    // ── Trying again ─────────────────────────────────────────────────────────

    public static bool IsRetryable(StoreRefund r, DateTime now)
        => r.Status == StoreRefundStatus.Failed
        || (r.Status == StoreRefundStatus.Pending && r.StripeRefundId is null && now - r.DateCreated > StaleAfter);

    /// <summary>A failed refund, or one that never reached Stripe: adopt what Stripe already has, else ask again with a fresh key.</summary>
    public async Task<StoreRefundAttempt> RetryAsync(Guid refundId, Guid? actor, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var refund = await db.StoreRefunds.Include(r => r.Order).FirstOrDefaultAsync(r => r.Id == refundId, ct);
        if (refund is null) return new StoreRefundAttempt(StoreRefundOutcomeKind.NotFound, null, null);
        if (!IsRetryable(refund, Now)) return StoreRefundAttempt.Refused(StoreOrderDeskSentences.NotRetryable);
        if (!gateway.IsConfigured) return new StoreRefundAttempt(StoreRefundOutcomeKind.Unavailable, null, StoreCheckoutSentences.PaymentsNotSetUp);

        // 1. Stripe may already have it (the crash came after its yes): adopt, never create.
        var existing = (await gateway.ListRefundsAsync(refund.Order.StripePaymentIntentId!, ct))
            .FirstOrDefault(r => r.Metadata.TryGetValue(StoreStripeKeys.Refund, out var ours) && ours == refund.Id.ToString());
        if (refund.Status == StoreRefundStatus.Failed && existing is null or { Status: "failed" or "canceled" })
        {
            // Failed for good at Stripe (or never there): back to Pending for a fresh attempt.
            refund.Status = StoreRefundStatus.Pending;
            refund.FailureReason = null;
            refund.StripeRefundId = null;
            existing = null;
        }
        if (existing is not null)
            return await BranchAsync(refund.Id, existing, actor, ct);

        // 2. A fresh key, saved before the call — the old one is never sent again.
        refund.Attempt += 1;
        await db.SaveChangesAsync(ct);
        return await AskStripeAsync(refund.Order, refund, actor, ct);
    }

    /// <summary>Files the tax reversal a refund is still missing (the order's filing came late, or the first try failed).</summary>
    public async Task<bool> RetryTaxAsync(Guid refundId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var ok = await db.StoreRefunds.AnyAsync(r => r.Id == refundId && r.Status == StoreRefundStatus.Succeeded
            && r.StripeTaxReversalId == null && r.Order.StripeTaxTransactionId != null, ct);
        return ok && await ReverseTaxAsync(refundId, ct);
    }

    /// <summary>After the order's own tax filing lands: reverse every refund that was waiting for it.</summary>
    public async Task ReverseWaitingRefundsAsync(Guid orderId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var waiting = await db.StoreRefunds.Where(r => r.OrderId == orderId && r.Status == StoreRefundStatus.Succeeded && r.StripeTaxReversalId == null)
            .OrderBy(r => r.CompletedUtc).Select(r => r.Id).ToListAsync(ct);
        foreach (var id in waiting) await ReverseTaxAsync(id, ct);
    }

    /// <summary>Files the reversal for one completed refund, outside any transaction. False when there is nothing to file yet or Stripe refused.</summary>
    private async Task<bool> ReverseTaxAsync(Guid refundId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var refund = await db.StoreRefunds.Include(r => r.Order).FirstAsync(r => r.Id == refundId, ct);
        var order = refund.Order;
        if (refund.StripeTaxReversalId is not null || order.StripeTaxTransactionId is not { } transaction) return false;

        var earlier = await db.StoreRefunds.CountAsync(r => r.OrderId == order.Id && r.StripeTaxReversalId != null, ct);
        var number = await db.StoreRefunds.CountAsync(r => r.OrderId == order.Id && r.Status == StoreRefundStatus.Succeeded && r.CompletedUtc <= refund.CompletedUtc, ct);
        var full = earlier == 0 && refund.Amount == order.Total;
        var cents = StoreMoney.Cents(refund.Amount);
        try
        {
            var reversal = await tax.ReverseAsync(transaction, $"{StoreOrderPayments.TaxReference(order)}-R{number}",
                full ? StoreTaxReversalMode.Full : StoreTaxReversalMode.Partial, cents, $"store-tax-reversal-{refund.Id:N}", ct);
            refund.StripeTaxReversalId = reversal;
            refund.TaxReversed = TaxShare(order, refund.Amount);
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (Exception ex) when (ex is StoreTaxException or HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "The tax reversal for refund {RefundId} on order {Number} could not be filed; it will be tried again.", refundId, order.OrderNumber);
            db.StoreOrderEvents.Add(Event(order.Id, StoreOrderEventKind.TaxTransactionFailed, $"The sales tax reversal for a refund could not be filed: {ex.Message}", null, null, Now));
            await db.SaveChangesAsync(ct);
            return false;
        }
    }

    /// <summary>The tax share of an amount refunded, in proportion to the order.</summary>
    private static decimal TaxShare(StoreOrder order, decimal amount)
        => order.Total == 0 ? 0 : StoreMoney.Round(order.TaxAmount * amount / order.Total);

    // ── Webhook ──────────────────────────────────────────────────────────────

    /// <summary>
    /// <c>refund.created</c>, <c>refund.updated</c>, <c>refund.failed</c>: finishes our own refunds, and
    /// records a refund made in Stripe's dashboard against the order its payment belongs to.
    /// </summary>
    public async Task RecordDashboardRefundAsync(StripeWebhookEvent e, CancellationToken ct = default)
    {
        if (e.RefundId is not { } stripeRefundId) return;
        var status = e.Type == "refund.failed" ? "failed" : e.RefundStatus ?? "pending";

        // Ours: by the id we wrote into its metadata.
        if (e.RefundMetadata.TryGetValue(StoreStripeKeys.Refund, out var raw) && Guid.TryParse(raw, out var ours))
        {
            await ApplyAsync(ours, stripeRefundId, status, e.RefundFailureReason, ct);
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        // Seen before (a redelivery, or its later refund.updated): move the row we already have.
        if (await db.StoreRefunds.AsNoTracking().Where(r => r.StripeRefundId == stripeRefundId).Select(r => (Guid?)r.Id).FirstOrDefaultAsync(ct) is { } known)
        {
            await ApplyAsync(known, stripeRefundId, status, e.RefundFailureReason, ct);
            return;
        }

        // New, made in the dashboard: find the order by its payment.
        var order = e.PaymentIntentId is { } pi ? await db.StoreOrders.FirstOrDefaultAsync(o => o.StripePaymentIntentId == pi, ct) : null;
        if (order is null)
        {
            log.LogInformation("Refund {RefundId} is not on a store order (a subscription charge, perhaps); ignored.", stripeRefundId);
            return;
        }

        var refund = new StoreRefund
        {
            Id = Guid.NewGuid(), OrderId = order.Id, Amount = (e.RefundAmountCents ?? 0) / 100m, Reason = DashboardReason,
            Restock = false, Status = StoreRefundStatus.Pending, Attempt = 1, StripeRefundId = stripeRefundId,
            RequestedByAppUserId = null, DateCreated = Now,
        };
        db.StoreRefunds.Add(refund);
        db.StoreOrderEvents.Add(Event(order.Id, StoreOrderEventKind.RefundFromStripe, DashboardReason, refund.Amount, null, Now));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return;   // the same refund, delivered twice at once: the unique StripeRefundId kept one row
        }
        await ApplyAsync(refund.Id, stripeRefundId, status, e.RefundFailureReason, ct);
    }

    private async Task ApplyAsync(Guid refundId, string stripeRefundId, string status, string? failure, CancellationToken ct)
    {
        switch (status)
        {
            case "succeeded": await CompleteAsync(refundId, stripeRefundId, null, ct); break;
            case "failed" or "canceled": await MarkFailedAsync(refundId, failure ?? status, ct, stripeRefundId); break;
            default: await RecordPendingAsync(refundId, stripeRefundId, ct); break;
        }
    }

    private static StoreOrderEvent Event(Guid orderId, StoreOrderEventKind kind, string? note, decimal? amount, Guid? actor, DateTime now) => new()
    {
        Id = Guid.NewGuid(), OrderId = orderId, Kind = kind, Note = note, Amount = amount, ActorAppUserId = actor, OccurredUtc = now,
    };
}
