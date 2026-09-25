using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// What happens to a person's store orders when their account is closed or deleted (storefront
/// plan §5.8).
/// </summary>
/// <remarks>
/// <para><b>Orders are kept.</b> An order is a sale, a tax record and a refund's anchor, so it
/// outlives the buyer's account — as every other money table does. What goes is the person: their
/// name, email, phone, street and IP. City, state and zip stay, because that is where the tax was
/// owed.</para>
///
/// <para><b>Except while it is on its way.</b> Ben, 09/24/2026: an order not yet delivered keeps
/// its delivery address until it is — scrubbing the street off a parcel in transit helps nobody.
/// Such an order is detached from the account now and marked
/// <see cref="StoreOrder.PendingAnonymisationSinceUtc"/>; <c>StoreOrderTransitions</c> scrubs it on
/// its last transition (delivered, cancelled, refunded), and the cart sweep scrubs anything still
/// waiting after 90 days.</para>
///
/// <para><b>Tracked entities, one save, no ExecuteUpdate.</b> The callers — <c>AppUserPurge</c>
/// and <c>AccountClosureService</c> — hold the transaction; this must be inside it.</para>
/// </remarks>
public static class StoreOrderScrub
{
    public const string RemovedStreet = "(removed)";

    /// <summary>After this long waiting, the sweep scrubs an in-flight order whatever its state.</summary>
    public static readonly TimeSpan PendingBackstop = TimeSpan.FromDays(90);

    public static bool IsTerminal(StoreOrderStatus status)
        => status is StoreOrderStatus.Delivered or StoreOrderStatus.Cancelled or StoreOrderStatus.Refunded;

    /// <summary>
    /// Detaches every order from <paramref name="userId"/>: finished orders are scrubbed now, orders
    /// in flight keep their address until they finish. Saves once.
    /// </summary>
    /// <returns>(scrubbed now, kept until delivered)</returns>
    public static async Task<(int Scrubbed, int StillShipping)> DetachAndScrubAsync(
        BenDataContext db, Guid userId, DateTime now, CancellationToken ct = default)
    {
        var orders = await db.StoreOrders.Where(o => o.BuyerAppUserId == userId).ToListAsync(ct);
        var closedEmail = AccountClosure.ClosedEmailFor(userId);

        int scrubbed = 0, stillShipping = 0;
        foreach (var order in orders)
        {
            order.BuyerAppUserId = null;
            if (IsTerminal(order.Status))
            {
                await ScrubAsync(db, order, closedEmail, now, ct);
                scrubbed++;
            }
            else
            {
                order.PendingAnonymisationSinceUtc ??= now;
                order.DateUpdated = now;
                stillShipping++;
            }
        }

        // The redemptions the account holds stop pointing at it; the email on them goes with the
        // order's (above, for the finished ones).
        foreach (var redemption in await db.StoreCouponRedemptions.Where(r => r.BuyerAppUserId == userId).ToListAsync(ct))
            redemption.BuyerAppUserId = null;

        await db.SaveChangesAsync(ct);
        return (scrubbed, stillShipping);
    }

    /// <summary>
    /// Removes the person from one order (tracked; the caller saves). Idempotent: an order
    /// already scrubbed is left alone.
    /// </summary>
    /// <param name="closedEmail">
    /// The address the order is left with — the closed account's
    /// (<see cref="AccountClosure.ClosedEmailFor"/>) when the account is known; the order's own id
    /// in the same form when it is scrubbed later and the account is long gone.
    /// </param>
    public static async Task ScrubAsync(BenDataContext db, StoreOrder order, string? closedEmail, DateTime now, CancellationToken ct = default)
    {
        if (order.ShipStreet1 == RemovedStreet && order.PendingAnonymisationSinceUtc is null) return;

        closedEmail ??= AccountClosure.ClosedEmailFor(order.Id);
        var closedNormalized = StoreEmail.Normalize(closedEmail);
        var oldNormalized = order.BuyerEmailNormalized;

        order.BuyerAppUserId = null;
        order.BuyerName = AccountClosure.FormerMemberName;
        order.BuyerEmail = closedEmail;
        order.BuyerEmailNormalized = closedNormalized;
        order.ShipName = AccountClosure.FormerMemberName;
        order.ShipPhone = string.Empty;
        order.ShipStreet1 = RemovedStreet;
        order.ShipStreet2 = null;
        order.BillName = null;
        order.BillCompany = null;
        order.BillStreet1 = null;
        order.BillStreet2 = null;
        order.PlacedFromIp = null;
        order.PendingAnonymisationSinceUtc = null;
        order.DateUpdated = now;

        foreach (var redemption in await db.StoreCouponRedemptions
                     .Where(r => r.OrderId == order.Id && r.BuyerEmailNormalized == oldNormalized).ToListAsync(ct))
        {
            redemption.BuyerEmailNormalized = closedNormalized;
            redemption.BuyerAppUserId = null;
        }

        db.StoreOrderEvents.Add(new StoreOrderEvent
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Kind = StoreOrderEventKind.Anonymised,
            Note = "The buyer's account was closed; their name and address were removed from this order.",
            OccurredUtc = now,
        });
    }
}
