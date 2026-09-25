using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// One package's journey — packed, shipped, delivered — and what it does to its order (store
/// sellers, backlog 251, P7). The store's staff move any package; a seller moves only their own.
/// </summary>
/// <remarks>
/// <para><b>The order row is locked first.</b> Every move runs in one transaction that begins by
/// writing the order row, then moves the package with a conditional update, then reads every
/// package and writes the order's rolled-up status (<see cref="StoreParcelRollup"/>). Two sellers
/// shipping the two packages of one order at the same moment would otherwise each read the other's
/// package as unshipped and both write "Partially shipped" — the order stranded, though everything
/// had gone. With the lock, the second waits and reads the first's package as shipped.</para>
///
/// <para><b>Attention stops every package.</b> An order flagged for a person to look at can't be
/// packed or shipped until an admin has cleared it — a seller included.</para>
///
/// <para><b>One letter per package.</b> "Part of your order is on its way — package 1 of 2", with
/// only that package's items and its own carrier and tracking.</para>
/// </remarks>
public sealed class StoreParcelTransitions(
    IDbContextFactory<BenDataContext> dbFactory, StoreOrderMailer mailer, StoreAlerts alerts, TimeProvider? clock = null)
{
    private DateTime Now => (clock ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    private static string Words(StoreParcelStatus s) => s switch
    {
        StoreParcelStatus.Waiting => "waiting to be packed",
        StoreParcelStatus.Packed => "packed",
        StoreParcelStatus.Shipped => "shipped",
        StoreParcelStatus.Delivered => "delivered",
        _ => "cancelled",
    };

    /// <summary>The order statuses in which a package may be packed or shipped: paid, and not yet finished.</summary>
    private static readonly StoreOrderStatus[] Fulfilling =
        [StoreOrderStatus.Paid, StoreOrderStatus.Packed, StoreOrderStatus.PartiallyShipped, StoreOrderStatus.Shipped];

    /// <summary>What is wrong with a shipment as typed; null when it will do. Also returns the number and link to store.</summary>
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

    public Task<StoreDeskResult> PackAsync(Guid orderId, Guid parcelId, Guid actor, Guid? seller = null, CancellationToken ct = default)
        => MoveAsync(orderId, parcelId, actor, seller, needsFulfilling: true, [StoreParcelStatus.Waiting], StoreParcelStatus.Packed,
            StoreOrderEventKind.Packed, (x, now) => x.PackedUtc ??= now,
            s => $"Only a package waiting to be packed can be packed; this one is {Words(s)}.", note: null, ct);

    public async Task<StoreDeskResult> ShipAsync(Guid orderId, Guid parcelId, StoreShipmentInfo info, Guid actor, Guid? seller = null, CancellationToken ct = default)
    {
        var (problem, number, link) = CheckShipment(info);
        if (problem is not null) return StoreDeskResult.No(problem);
        var note = number is null ? $"{info.Carrier} — {StoreOrderDeskSentences.NoTrackingLabel}" : $"{info.Carrier} {number}";
        if (!string.IsNullOrWhiteSpace(info.Note)) note += $" — {info.Note.Trim()}";

        var result = await MoveAsync(orderId, parcelId, actor, seller, needsFulfilling: true,
            [StoreParcelStatus.Waiting, StoreParcelStatus.Packed], StoreParcelStatus.Shipped, StoreOrderEventKind.Shipped,
            (x, now) =>
            {
                x.PackedUtc ??= now;
                x.ShippedUtc = now;
                x.Carrier = info.Carrier;
                x.TrackingNumber = number;
                x.TrackingUrl = link;
            },
            s => $"Only a package waiting or packed can be shipped; this one is {Words(s)}.", note, ct, letter: true);
        if (result.Ok) await alerts.ParcelShippedAsync(parcelId, ct);
        return result;
    }

    /// <summary>Fixes a mistyped tracking number, or adds one to a package first sent untracked. No letter — re-send it if the buyer needs it.</summary>
    public async Task<StoreDeskResult> CorrectTrackingAsync(Guid orderId, Guid parcelId, StoreShipmentInfo info, Guid actor, Guid? seller = null, CancellationToken ct = default)
    {
        var (problem, number, link) = CheckShipment(info);
        if (problem is not null) return StoreDeskResult.No(problem);

        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!await Scoped(db, orderId, parcelId, seller).AnyAsync(ct)) return StoreDeskResult.Missing;
        var rows = await Scoped(db, orderId, parcelId, seller).Where(x => x.Status == StoreParcelStatus.Shipped)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Carrier, info.Carrier).SetProperty(x => x.TrackingNumber, number)
                .SetProperty(x => x.TrackingUrl, link).SetProperty(x => x.DateUpdated, now), ct);
        if (rows == 0) return StoreDeskResult.No(StoreOrderDeskSentences.TrackingOnlyWhenShipped);

        db.StoreOrderEvents.Add(Event(orderId, parcelId, StoreOrderEventKind.TrackingChanged, null, null, actor, now,
            number is null ? $"{info.Carrier} — {StoreOrderDeskSentences.NoTrackingLabel}" : $"{info.Carrier} {number}"));
        await db.SaveChangesAsync(ct);
        return StoreDeskResult.Done;
    }

    /// <summary>Marks a shipped package arrived. Once every package has, the order is Delivered — and an address waiting to be scrubbed goes.</summary>
    public Task<StoreDeskResult> DeliverAsync(Guid orderId, Guid parcelId, Guid actor, Guid? seller = null, CancellationToken ct = default)
        => MoveAsync(orderId, parcelId, actor, seller, needsFulfilling: false, [StoreParcelStatus.Shipped], StoreParcelStatus.Delivered,
            StoreOrderEventKind.Delivered, (x, now) => x.DeliveredUtc = now,
            _ => StoreOrderDeskSentences.OnlyShippedCanBeDelivered, note: null, ct);

    /// <summary>Sends a shipped package's letter again, through the outbox.</summary>
    public async Task<StoreDeskResult> ResendShippedAsync(Guid orderId, Guid parcelId, Guid actor, string? actorName, CancellationToken ct = default)
    {
        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var parcel = await db.StoreOrderParcels.FirstOrDefaultAsync(x => x.Id == parcelId && x.OrderId == orderId, ct);
        if (parcel is null) return StoreDeskResult.Missing;
        if (parcel.Carrier is null) return StoreDeskResult.No(StoreOrderDeskSentences.ShippedLetterNeedsCarrier);
        var order = await db.StoreOrders.Include(o => o.Items).FirstAsync(o => o.Id == orderId, ct);
        var count = await db.StoreOrderParcels.CountAsync(x => x.OrderId == orderId && x.Status != StoreParcelStatus.Cancelled, ct);
        await mailer.QueueShippedAsync(db, order, parcel, order.Items.Where(i => i.ParcelId == parcel.Id).ToList(), count, now, ct);
        db.StoreOrderEvents.Add(Event(orderId, parcelId, StoreOrderEventKind.NoteAdded, null, null, actor, now,
            $"The shipped letter for package {parcel.Number} was re-sent by {actorName ?? "an admin"}"));
        await db.SaveChangesAsync(ct);
        return StoreDeskResult.Done;
    }

    // ── the one way a package moves ──────────────────────────────────────────

    private static IQueryable<StoreOrderParcel> Scoped(BenDataContext db, Guid orderId, Guid parcelId, Guid? seller)
        => db.StoreOrderParcels.Where(x => x.Id == parcelId && x.OrderId == orderId && (seller == null || x.SellerAppUserId == seller));

    private async Task<StoreDeskResult> MoveAsync(
        Guid orderId, Guid parcelId, Guid actor, Guid? seller, bool needsFulfilling, StoreParcelStatus[] from, StoreParcelStatus to,
        StoreOrderEventKind kind, Action<StoreOrderParcel, DateTime> apply, Func<StoreParcelStatus, string> refusal, string? note,
        CancellationToken ct, bool letter = false)
    {
        var now = Now;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var parcel = await Scoped(db, orderId, parcelId, seller).AsNoTracking().FirstOrDefaultAsync(ct);
        if (parcel is null) return StoreDeskResult.Missing;
        var order = await db.StoreOrders.AsNoTracking().FirstAsync(o => o.Id == orderId, ct);
        if (needsFulfilling)
        {
            if (order.NeedsAttention) return StoreDeskResult.No(StoreOrderDeskSentences.NeedsAttentionFirst(order.AttentionReason ?? "look at it first."));
            if (!Fulfilling.Contains(order.Status)) return StoreDeskResult.No(StoreOrderDeskSentences.NotReadyToFulfil);
        }

        await using var tx = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        // 1. The order row first: every mover of this order's packages queues here.
        await db.StoreOrders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.DateUpdated, now), ct);

        // 2. The package, only from where it may move.
        var tracked = await db.StoreOrderParcels.FirstAsync(x => x.Id == parcelId, ct);
        if (!from.Contains(tracked.Status)) return StoreDeskResult.No(refusal(tracked.Status));
        tracked.Status = to;
        tracked.DateUpdated = now;
        apply(tracked, now);

        // 3. The order, rolled up from every package as they now stand.
        var statuses = await db.StoreOrderParcels.Where(x => x.OrderId == orderId && x.Id != parcelId).Select(x => x.Status).ToListAsync(ct);
        statuses.Add(to);
        var tracked0 = await db.StoreOrders.Include(o => o.Items).FirstAsync(o => o.Id == orderId, ct);
        var was = tracked0.Status;
        if (StoreParcelRollup.Status(statuses) is { } rolled && tracked0.Status is not (StoreOrderStatus.Cancelled or StoreOrderStatus.Refunded or StoreOrderStatus.PendingPayment))
        {
            tracked0.Status = rolled;
            if (rolled >= StoreOrderStatus.Packed) tracked0.PackedUtc ??= now;
            if (rolled is StoreOrderStatus.Shipped or StoreOrderStatus.Delivered) tracked0.ShippedUtc ??= now;
            if (rolled == StoreOrderStatus.Delivered) tracked0.DeliveredUtc ??= now;
        }

        db.StoreOrderEvents.Add(Event(orderId, parcelId, kind, was, tracked0.Status, actor, now,
            $"Package {tracked.Number}" + (note is null ? "" : $": {note}")));

        if (letter)
            await mailer.QueueShippedAsync(db, tracked0, tracked, tracked0.Items.Where(i => i.ParcelId == parcelId).ToList(),
                statuses.Count(s => s != StoreParcelStatus.Cancelled), now, ct);

        // Somebody who closed their account while this was on its way: now all of it has arrived, the address goes.
        if (tracked0.Status == StoreOrderStatus.Delivered && tracked0.PendingAnonymisationSinceUtc is not null)
            await StoreOrderScrub.ScrubAsync(db, tracked0, null, now, ct);

        await db.SaveChangesAsync(ct);
        if (tx is not null) await tx.CommitAsync(ct);
        return StoreDeskResult.Done;
    }

    private static StoreOrderEvent Event(Guid orderId, Guid parcelId, StoreOrderEventKind kind, StoreOrderStatus? from, StoreOrderStatus? to,
        Guid actor, DateTime now, string? note) => new()
    {
        Id = Guid.NewGuid(), OrderId = orderId, ParcelId = parcelId, Kind = kind, FromStatus = from, ToStatus = to,
        ActorAppUserId = actor, Note = note, OccurredUtc = now,
    };
}
