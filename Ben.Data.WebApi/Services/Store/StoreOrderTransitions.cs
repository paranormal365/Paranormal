using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>What an order-desk action came to: done, no such order, or refused in words.</summary>
public sealed record StoreDeskResult(bool NotFound, string? Refusal)
{
    public static readonly StoreDeskResult Done = new(false, null);
    public static readonly StoreDeskResult Missing = new(true, null);
    public static StoreDeskResult No(string sentence) => new(false, sentence);
    public bool Ok => !NotFound && Refusal is null;
}

/// <summary>
/// The order desk's work on an order (storefront S5.3): notes, the address, attention, letters
/// again, cancelling, releasing a checkout. Packing, shipping and delivering are per package since
/// store sellers P7 — <see cref="StoreParcelTransitions"/>.
/// </summary>
/// <remarks>
/// <para><b>Every move is one conditional update</b> (<c>WHERE Status = @from</c>), and the row count
/// decides: two admins pressing the same button at once do it once, and the loser is told.</para>
/// </remarks>
public sealed class StoreOrderTransitions(
    IDbContextFactory<BenDataContext> dbFactory, StoreOrderMailer mailer, StoreAlerts alerts,
    StoreRefundService refunds, StoreOrderPayments payments, TimeProvider? clock = null)
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static string Words(StoreOrderStatus s) => s switch
    {
        StoreOrderStatus.PendingPayment => "waiting for payment",
        StoreOrderStatus.Paid => "paid",
        StoreOrderStatus.Packed => "packed",
        StoreOrderStatus.Shipped => "shipped",
        StoreOrderStatus.Delivered => "delivered",
        StoreOrderStatus.Cancelled => "cancelled",
        StoreOrderStatus.Refunded => "refunded",
        StoreOrderStatus.PartiallyShipped => "partly shipped",
        _ => s.ToString().ToLowerInvariant(),
    };

    /// <summary>Cancels a paid order that has not shipped: a full refund of what is left, and Cancelled when it goes through.</summary>
    public async Task<(StoreDeskResult Result, StoreRefundAttempt? Refund)> CancelAsync(Guid orderId, CancelStoreOrderRequest request, Guid actor, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.AsNoTracking().Include(o => o.Items).Include(o => o.Refunds).ThenInclude(r => r.Items)
            .AsSplitQuery().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return (StoreDeskResult.Missing, null);
        if (order.Status is StoreOrderStatus.Shipped or StoreOrderStatus.Delivered or StoreOrderStatus.PartiallyShipped)
            return (StoreDeskResult.No(StoreOrderDeskSentences.AlreadyOnItsWay), null);
        if (order.Status is not (StoreOrderStatus.Paid or StoreOrderStatus.Packed)) return (StoreDeskResult.No(StoreOrderDeskSentences.CannotCancel(Words(order.Status))), null);
        if (string.IsNullOrWhiteSpace(request.Reason)) return (StoreDeskResult.No(StoreOrderDeskSentences.RefundNeedsReason), null);

        // Every unit still refundable, so restocking puts back exactly what was bought and not yet refunded.
        var lines = order.Items.Select(i => new StoreRefundLine(i.Id, StoreRefundService.RefundableUnits(i, order.Refunds)))
            .Where(l => l.Quantity > 0).ToList();
        var attempt = lines.Count > 0
            ? await refunds.RefundAsync(orderId, new StoreRefundRequest(null, lines, request.Reason, request.Restock), actor, cancelling: true, ct)
            : await refunds.RefundAsync(orderId, new StoreRefundRequest(StoreRefundService.Remaining(order, order.Refunds), null, request.Reason, false), actor, cancelling: true, ct);
        return attempt.Kind switch
        {
            StoreRefundOutcomeKind.Completed or StoreRefundOutcomeKind.Pending => (StoreDeskResult.Done, attempt),
            StoreRefundOutcomeKind.NotFound => (StoreDeskResult.Missing, attempt),
            _ => (StoreDeskResult.No(attempt.Sentence ?? "The order could not be cancelled."), attempt),
        };
    }

    /// <summary>
    /// Cancels one package that has not gone (store sellers P8): its items and its shipping are
    /// refunded — restocked if asked — and when the refund goes through it is marked Cancelled and
    /// the order reads what is left.
    /// </summary>
    public async Task<(StoreDeskResult Result, StoreRefundAttempt? Refund)> CancelParcelAsync(
        Guid orderId, Guid parcelId, CancelStoreOrderRequest request, Guid actor, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.AsNoTracking().Include(o => o.Items).Include(o => o.Parcels).Include(o => o.Refunds).ThenInclude(r => r.Items)
            .AsSplitQuery().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        var parcel = order?.Parcels.FirstOrDefault(x => x.Id == parcelId);
        if (order is null || parcel is null) return (StoreDeskResult.Missing, null);
        if (parcel.Status is not (StoreParcelStatus.Waiting or StoreParcelStatus.Packed))
            return (StoreDeskResult.No(StoreOrderDeskSentences.PackageAlreadyGone), null);
        if (string.IsNullOrWhiteSpace(request.Reason)) return (StoreDeskResult.No(StoreOrderDeskSentences.RefundNeedsReason), null);
        if (order.Parcels.Count(x => x.Status != StoreParcelStatus.Cancelled) == 1)
            return (StoreDeskResult.No(StoreOrderDeskSentences.LastPackageCancelsOrder), null);

        var lines = order.Items.Where(i => i.ParcelId == parcelId)
            .Select(i => new StoreRefundLine(i.Id, StoreRefundService.RefundableUnits(i, order.Refunds))).Where(l => l.Quantity > 0).ToList();
        var attempt = await refunds.RefundAsync(orderId, new StoreRefundRequest(null, lines, request.Reason, request.Restock, [parcelId]),
            actor, cancelling: false, ct, cancellingParcel: parcelId);
        return attempt.Kind switch
        {
            StoreRefundOutcomeKind.Completed or StoreRefundOutcomeKind.Pending => (StoreDeskResult.Done, attempt),
            StoreRefundOutcomeKind.NotFound => (StoreDeskResult.Missing, attempt),
            _ => (StoreDeskResult.No(attempt.Sentence ?? "The package could not be cancelled."), attempt),
        };
    }

    public async Task<StoreDeskResult> AddNoteAsync(Guid orderId, string? note, Guid actor, CancellationToken ct = default)
    {
        var text = note?.Trim();
        if (string.IsNullOrEmpty(text)) return StoreDeskResult.No(StoreOrderDeskSentences.NoteEmpty);
        if (text.Length > 2000) return StoreDeskResult.No("A note is 2,000 characters at most.");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreOrders.AnyAsync(o => o.Id == orderId, ct)) return StoreDeskResult.Missing;
        db.StoreOrderEvents.Add(Moved(orderId, StoreOrderEventKind.NoteAdded, null, null, actor, Now, text));
        await db.SaveChangesAsync(ct);
        return StoreDeskResult.Done;
    }

    /// <summary>
    /// Corrects where a paid, unshipped order is going — and the buyer's email. A new email replaces
    /// the order's private link (the old one stops working) and the receipt goes to the new address.
    /// Tax is never recalculated: a changed state is noted.
    /// </summary>
    public async Task<StoreDeskResult> ChangeAddressAsync(Guid orderId, ChangeStoreOrderAddressRequest request, Guid actor, CancellationToken ct = default)
    {
        if (StoreCheckoutRules.AddressProblem(request.Shipping) is { } problem) return StoreDeskResult.No(problem);
        var newEmail = string.IsNullOrWhiteSpace(request.BuyerEmail) ? null : request.BuyerEmail.Trim();
        if (newEmail is not null && StoreCheckoutRules.EmailProblem(newEmail) is { } emailProblem) return StoreDeskResult.No(emailProblem);

        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return StoreDeskResult.Missing;
        if (order.Status is not (StoreOrderStatus.Paid or StoreOrderStatus.Packed)) return StoreDeskResult.No(StoreOrderDeskSentences.AddressAfterShipping);

        var a = request.Shipping;
        var oldLines = string.Join(", ", new[] { order.ShipName, order.ShipStreet1, order.ShipStreet2, $"{order.ShipCity}, {order.ShipState} {order.ShipZip}" }.Where(l => !string.IsNullOrWhiteSpace(l)));
        var oldState = order.ShipState;
        order.ShipName = a.FullName.Trim();
        order.ShipPhone = a.Phone.Trim();
        order.ShipStreet1 = a.Street1.Trim();
        order.ShipStreet2 = string.IsNullOrWhiteSpace(a.Street2) ? null : a.Street2.Trim();
        order.ShipCity = a.City.Trim();
        order.ShipState = UsStates.Normalize(a.State)!;
        order.ShipZip = a.Zip.Trim();
        order.DateUpdated = now;
        var newLines = string.Join(", ", new[] { order.ShipName, order.ShipStreet1, order.ShipStreet2, $"{order.ShipCity}, {order.ShipState} {order.ShipZip}" }.Where(l => !string.IsNullOrWhiteSpace(l)));
        db.StoreOrderEvents.Add(Moved(orderId, StoreOrderEventKind.AddressChanged, null, null, actor, now, $"From {oldLines} to {newLines}"));
        if (!string.Equals(oldState, order.ShipState, StringComparison.OrdinalIgnoreCase))
            db.StoreOrderEvents.Add(Moved(orderId, StoreOrderEventKind.NoteAdded, null, null, actor, now,
                $"Sales tax was calculated for {oldState}; it was not recalculated for {order.ShipState}."));

        var emailChanged = newEmail is not null && StoreEmail.Normalize(newEmail) != order.BuyerEmailNormalized;
        if (emailChanged)
        {
            order.BuyerEmail = newEmail!;
            order.BuyerEmailNormalized = StoreEmail.Normalize(newEmail);
            // The emailed link went to the old address: a new one replaces it, and the old one stops opening the order.
            order.AccessToken = Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            db.StoreOrderEvents.Add(Moved(orderId, StoreOrderEventKind.NoteAdded, null, null, actor, now, "The emailed link was replaced"));
            await mailer.QueueConfirmationAsync(db, order, order.Items.ToList(), now, ct);
        }
        await db.SaveChangesAsync(ct);
        return StoreDeskResult.Done;
    }

    public async Task<StoreDeskResult> ClearAttentionAsync(Guid orderId, Guid actor, CancellationToken ct = default)
    {
        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreOrders.AnyAsync(o => o.Id == orderId, ct)) return StoreDeskResult.Missing;
        var rows = await db.StoreOrders.Where(o => o.Id == orderId && o.NeedsAttention)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.NeedsAttention, false).SetProperty(o => o.DateUpdated, now), ct);
        if (rows == 0) return StoreDeskResult.No(StoreOrderDeskSentences.NothingToClear);
        db.StoreOrderEvents.Add(Moved(orderId, StoreOrderEventKind.AttentionCleared, null, null, actor, now));
        await db.SaveChangesAsync(ct);
        return StoreDeskResult.Done;
    }

    /// <summary>Sends the buyer's receipt or shipped letter again, through the outbox.</summary>
    public async Task<StoreDeskResult> ResendLetterAsync(Guid orderId, string? kind, Guid actor, string? actorName, CancellationToken ct = default)
    {
        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return StoreDeskResult.Missing;
        switch (kind)
        {
            case "confirmation":
                if (order.PaidUtc is null) return StoreDeskResult.No(StoreOrderDeskSentences.ConfirmationNeedsPayment);
                await mailer.QueueConfirmationAsync(db, order, order.Items.ToList(), now, ct);
                break;
            case "shipped":
                // Every package that has gone, each with its own letter (store sellers P7).
                var parcels = await db.StoreOrderParcels.Where(x => x.OrderId == orderId).ToListAsync(ct);
                var sent = parcels.Where(x => x.Carrier is not null).OrderBy(x => x.Number).ToList();
                if (sent.Count == 0) return StoreDeskResult.No(StoreOrderDeskSentences.ShippedLetterNeedsCarrier);
                var count = parcels.Count(x => x.Status != StoreParcelStatus.Cancelled);
                foreach (var parcel in sent)
                    await mailer.QueueShippedAsync(db, order, parcel, order.Items.Where(i => i.ParcelId == parcel.Id).ToList(), count, now, ct);
                break;
            default:
                return StoreDeskResult.No(StoreOrderDeskSentences.UnknownLetter);
        }
        db.StoreOrderEvents.Add(Moved(orderId, StoreOrderEventKind.NoteAdded, null, null, actor, now,
            $"The {(kind == "shipped" ? "shipped letter" : "receipt")} was re-sent by {actorName ?? "an admin"}"));
        await db.SaveChangesAsync(ct);
        return StoreDeskResult.Done;
    }

    /// <summary>Lets go of a checkout still waiting for payment — at Stripe first, then its stock and code.</summary>
    public async Task<StoreDeskResult> ReleaseAsync(Guid orderId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return StoreDeskResult.Missing;
        if (order.Status != StoreOrderStatus.PendingPayment || order.ReservationReleasedUtc is not null)
            return StoreDeskResult.No(StoreOrderDeskSentences.OnlyOpenCheckoutsRelease);
        var outcome = await payments.CancelPendingOrderAsync(orderId, "Released by an admin", ct);
        return outcome == StoreOrderPayments.CancelOutcome.StillProcessing ? StoreDeskResult.No(StoreOrderDeskSentences.StillProcessing) : StoreDeskResult.Done;
    }

    private static async Task<StoreOrderStatus> StatusAsync(BenDataContext db, Guid orderId, CancellationToken ct)
        => await db.StoreOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => o.Status).FirstAsync(ct);

    private static StoreOrderEvent Moved(Guid orderId, StoreOrderEventKind kind, StoreOrderStatus? from, StoreOrderStatus? to, Guid actor, DateTime now, string? note = null) => new()
    {
        Id = Guid.NewGuid(), OrderId = orderId, Kind = kind, FromStatus = from, ToStatus = to, ActorAppUserId = actor, Note = note, OccurredUtc = now,
    };
}
