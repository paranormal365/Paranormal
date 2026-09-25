using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// Holding a discount code for an order, and letting it go (storefront plan §5.4).
/// </summary>
/// <remarks>
/// <para><b>Held when the order is PLACED</b>, inside the same transaction as the stock, not when
/// it is paid: a single-use code in N parallel checkouts must succeed once, and a code checked at
/// placement and counted at payment would let all N through. The hold is a redemption row plus a
/// conditional <c>UPDATE StoreCoupons SET RedemptionCount += 1 WHERE … RedemptionCount &lt;
/// MaxRedemptions</c>; the row count referees.</para>
///
/// <para><b>Released with the stock</b> when a checkout is abandoned or cancelled unpaid. The
/// count comes down only when the row delete actually removed a row, so releasing the same order
/// twice — the expiry job and a cancel racing — gives back one use, not two.</para>
/// </remarks>
public static class StoreCouponReservations
{
    /// <summary>
    /// How many holds this buyer already has on a code — by email OR by account, pending orders
    /// included — optionally leaving one order out (the order being re-placed).
    /// </summary>
    public static Task<int> PriorRedemptionsAsync(
        BenDataContext db, Guid couponId, string emailNormalized, Guid? buyerAppUserId,
        Guid? exceptOrderId = null, CancellationToken ct = default)
        => db.StoreCouponRedemptions
            .Where(r => r.CouponId == couponId
                     && (r.BuyerEmailNormalized == emailNormalized
                         || (buyerAppUserId != null && r.BuyerAppUserId == buyerAppUserId))
                     && (exceptOrderId == null || r.OrderId != exceptOrderId))
            .CountAsync(ct);

    /// <summary>
    /// Holds one use of <paramref name="coupon"/> for a saved <paramref name="order"/>. Null when
    /// held; otherwise the buyer's sentence and nothing has changed.
    /// </summary>
    public static async Task<string?> TryReserveAsync(
        BenDataContext db, StoreCoupon coupon, StoreOrder order, decimal discount, DateTime now, CancellationToken ct = default)
    {
        if (coupon.MaxRedemptionsPerBuyer is { } perBuyer
            && await PriorRedemptionsAsync(db, coupon.Id, order.BuyerEmailNormalized, order.BuyerAppUserId, order.Id, ct) >= perBuyer)
            return StoreCouponMath.AlreadyUsedByBuyer;

        var couponId = coupon.Id;
        var rows = await db.StoreCoupons
            .Where(c => c.Id == couponId && c.IsActive && (c.MaxRedemptions == null || c.RedemptionCount < c.MaxRedemptions))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RedemptionCount, c => c.RedemptionCount + 1), ct);
        if (rows != 1) return StoreCouponMath.UsedUp;

        db.StoreCouponRedemptions.Add(new StoreCouponRedemption
        {
            Id = Guid.NewGuid(),
            CouponId = couponId,
            OrderId = order.Id,
            BuyerEmailNormalized = order.BuyerEmailNormalized,
            BuyerAppUserId = order.BuyerAppUserId,
            DiscountAmount = discount,
            RedeemedUtc = now,
        });
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// Gives back the use an order was holding. True when there was one to give back.
    /// </summary>
    public static async Task<bool> ReleaseAsync(BenDataContext db, Guid orderId, CancellationToken ct = default)
    {
        var couponId = await db.StoreCouponRedemptions
            .Where(r => r.OrderId == orderId).Select(r => (Guid?)r.CouponId).FirstOrDefaultAsync(ct);
        if (couponId is null) return false;

        var deleted = await db.StoreCouponRedemptions
            .Where(r => r.OrderId == orderId && r.CouponId == couponId)
            .ExecuteDeleteAsync(ct);
        if (deleted != 1) return false;

        await db.StoreCoupons
            .Where(c => c.Id == couponId && c.RedemptionCount > 0)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.RedemptionCount, c => c.RedemptionCount - 1), ct);
        return true;
    }
}
