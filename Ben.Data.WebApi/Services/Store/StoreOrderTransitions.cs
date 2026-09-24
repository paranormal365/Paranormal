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
/// An order's journey after payment (storefront S5.3): packed, shipped, delivered — and the desk's
/// other work on it: notes, the address, attention, letters again, cancelling, releasing a checkout.
/// </summary>
/// <remarks>
/// <para><b>Every move is one conditional update</b> (<c>WHERE Status = @from</c>), and the row count
/// decides: two admins pressing Ship at once ship it once, and the loser is told what it already is.</para>
///
/// <para><b>Shipping (Ben, 09/24):</b> a carrier from the list and its tracking number, the link built
/// from the two — or "No tracking provided", when a parcel goes without one. The buyer's letter is
/// queued in the same transaction as the shipment.</para>
///
/// <para><b>Attention stops the parcel.</b> An order flagged for attention (paid after its checkout
/// was cancelled, an amount that did not match…) cannot be packed or shipped until an admin has
/// looked and cleared it.</para>
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
        _ => s.ToString().ToLowerInvariant(),
    };

    public async Task<StoreDeskResult> PackAsync(Guid orderId, Guid actor, CancellationToken ct = default)
    {
        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return StoreDeskResult.Missing;
        if (order.NeedsAttention) return StoreDeskResult.No(StoreOrderDeskSentences.NeedsAttentionFirst(order.AttentionReason ?? "look at it first."));

        var rows = await db.StoreOrders.Where(o => o.Id == orderId && o.Status == StoreOrderStatus.Paid && !o.NeedsAttention)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, StoreOrderStatus.Packed).SetProperty(o => o.PackedUtc, now)
                .SetProperty(o => o.DateUpdated, now), ct);
        if (rows == 0) return StoreDeskResult.No(StoreOrderDeskSentences.OnlyPaidCanBePacked(Words(await StatusAsync(db, orderId, ct))));

        db.StoreOrderEvents.Add(Moved(orderId, StoreOrderEventKind.Packed, StoreOrderStatus.Paid, StoreOrderStatus.Packed, actor, now));
        await db.SaveChangesAsync(ct);
        return StoreDeskResult.Done;
    }

    /// <summary>What is wrong with a shipment as typed; null when it will do. Also returns the tracking link to store.</summary>
    public static (string? Problem, string? Number, string? Link) CheckShipment(StoreShipmentInfo info)
    {
        if (!StoreCarriers.IsKnown(info.Carrier)) return (StoreOrderDeskSentences.PickACarrier, null, null);
        if (info.NoTracking) return (null, null, null);

        var number = info.TrackingNumber?.Trim();
        if (string.IsNullOrEmpty(number)) return (StoreOrderDeskSentences.NeedsTracking, null, null);
        if (number.Length > 64) return ("That tracking number is longer than any carrier's.", null, null);

        var link = StoreCarriers.TrackingUrl(info.Carrier, number);
        if (link is null && info.Carrier == StoreCarriers.Other && !string.IsNullOrWhiteSpace(info.TrackingUrl))
        {
            var typed = info.TrackingUrl.Trim();
            if (!Uri.TryCreate(typed, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                return (StoreOrderDeskSentences.TrackingLinkHttps, null, null);
            link = uri.ToString();
        }
        return (null, number, link);
    }

    public async Task<StoreDeskResult> ShipAsync(Guid orderId, StoreShipmentInfo info, Guid actor, CancellationToken ct = default)
    {
        var (problem, number, link) = CheckShipment(info);
        if (problem is not null) return StoreDeskResult.No(problem);

        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return StoreDeskResult.Missing;
        if (order.NeedsAttention) return StoreDeskResult.No(StoreOrderDeskSentences.NeedsAttentionFirst(order.AttentionReason ?? "look at it first."));

        await using (var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null)
        {
            var from = order.Status;
            var rows = await db.StoreOrders
                .Where(o => o.Id == orderId && (o.Status == StoreOrderStatus.Paid || o.Status == StoreOrderStatus.Packed) && !o.NeedsAttention)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, StoreOrderStatus.Shipped).SetProperty(o => o.ShippedUtc, now)
                    .SetProperty(o => o.PackedUtc, o => o.PackedUtc ?? now)
                    .SetProperty(o => o.Carrier, info.Carrier).SetProperty(o => o.TrackingNumber, number)
                    .SetProperty(o => o.TrackingUrl, link).SetProperty(o => o.DateUpdated, now), ct);
            if (rows == 0) return StoreDeskResult.No(StoreOrderDeskSentences.OnlyPaidOrPackedCanShip(Words(await StatusAsync(db, orderId, ct))));

            order.Status = StoreOrderStatus.Shipped;
            order.Carrier = info.Carrier;
            order.TrackingNumber = number;
            order.TrackingUrl = link;
            var note = number is null ? $"{info.Carrier} — {StoreOrderDeskSentences.NoTrackingLabel}" : $"{info.Carrier} {number}";
            if (!string.IsNullOrWhiteSpace(info.Note)) note += $" — {info.Note.Trim()}";
            db.StoreOrderEvents.Add(Moved(orderId, StoreOrderEventKind.Shipped, from, StoreOrderStatus.Shipped, actor, now, note));
            await mailer.QueueShippedAsync(db, order, order.Items.ToList(), now, ct);
            await db.SaveChangesAsync(ct);
            if (tx is not null) await tx.CommitAsync(ct);
        }
        await alerts.OrderShippedAsync(orderId, ct);
        return StoreDeskResult.Done;
    }

    /// <summary>Fixes a mistyped tracking number (or adds one to a parcel first sent as untracked). No letter: re-send it if the buyer needs it.</summary>
    public async Task<StoreDeskResult> CorrectTrackingAsync(Guid orderId, StoreShipmentInfo info, Guid actor, CancellationToken ct = default)
    {
        var (problem, number, link) = CheckShipment(info);
        if (problem is not null) return StoreDeskResult.No(problem);

        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreOrders.AnyAsync(o => o.Id == orderId, ct)) return StoreDeskResult.Missing;
        var rows = await db.StoreOrders.Where(o => o.Id == orderId && o.Status == StoreOrderStatus.Shipped)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Carrier, info.Carrier).SetProperty(o => o.TrackingNumber, number)
                .SetProperty(o => o.TrackingUrl, link).SetProperty(o => o.DateUpdated, now), ct);
        if (rows == 0) return StoreDeskResult.No(StoreOrderDeskSentences.TrackingOnlyWhenShipped);

        db.StoreOrderEvents.Add(Moved(orderId, StoreOrderEventKind.TrackingChanged, null, null, actor, now,
            number is null ? $"{info.Carrier} — {StoreOrderDeskSentences.NoTrackingLabel}" : $"{info.Carrier} {number}"));
        await db.SaveChangesAsync(ct);
        return StoreDeskResult.Done;
    }

    public async Task<StoreDeskResult> DeliverAsync(Guid orderId, Guid actor, CancellationToken ct = default)
    {
        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await db.StoreOrders.AnyAsync(o => o.Id == orderId, ct)) return StoreDeskResult.Missing;
        var rows = await db.StoreOrders.Where(o => o.Id == orderId && o.Status == StoreOrderStatus.Shipped)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, StoreOrderStatus.Delivered).SetProperty(o => o.DeliveredUtc, now)
                .SetProperty(o => o.DateUpdated, now), ct);
        if (rows == 0) return StoreDeskResult.No(StoreOrderDeskSentences.OnlyShippedCanBeDelivered);

        db.StoreOrderEvents.Add(Moved(orderId, StoreOrderEventKind.Delivered, StoreOrderStatus.Shipped, StoreOrderStatus.Delivered, actor, now));
        // A person who closed their account while this was on its way: now it has arrived, the address goes.
        var order = await db.StoreOrders.FirstAsync(o => o.Id == orderId, ct);
        if (order.PendingAnonymisationSinceUtc is not null) await StoreOrderScrub.ScrubAsync(db, order, null, now, ct);
        await db.SaveChangesAsync(ct);
        return StoreDeskResult.Done;
    }

    /// <summary>Cancels a paid order that has not shipped: a full refund of what is left, and Cancelled when it goes through.</summary>
    public async Task<(StoreDeskResult Result, StoreRefundAttempt? Refund)> CancelAsync(Guid orderId, CancelStoreOrderRequest request, Guid actor, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var order = await db.StoreOrders.AsNoTracking().Include(o => o.Items).Include(o => o.Refunds).ThenInclude(r => r.Items)
            .AsSplitQuery().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return (StoreDeskResult.Missing, null);
        if (order.Status is StoreOrderStatus.Shipped or StoreOrderStatus.Delivered) return (StoreDeskResult.No(StoreOrderDeskSentences.AlreadyOnItsWay), null);
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
                if (order.Carrier is null) return StoreDeskResult.No(StoreOrderDeskSentences.ShippedLetterNeedsCarrier);
                await mailer.QueueShippedAsync(db, order, order.Items.ToList(), now, ct);
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
