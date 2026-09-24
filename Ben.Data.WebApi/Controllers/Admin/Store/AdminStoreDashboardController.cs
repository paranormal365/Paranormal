using Ben.Data.Common.Constants;
using Ben.Data.Common.Enums;
using Ben.Data.Source.Context;
using Ben.Data.WebApi.Services.Store;
using Ben.Service.Models.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Admin.Store;

/// <summary>
/// The store at a glance: what is waiting to be packed and shipped, what needs a person, sales
/// over a window, and what is running low (storefront S1.7).
/// </summary>
/// <remarks>
/// <para><b>Sales</b> are money actually taken: orders PAID in the window (a checkout nobody paid
/// for is not a sale), less refunds that SUCCEEDED in it. A pending refund has not left yet.</para>
///
/// <para><b>Units held</b> are what open checkouts have reserved and not released — stock that is
/// on the shelf but cannot be sold to anybody else until those buyers pay or time out.</para>
/// </remarks>
[ApiController]
[Authorize(Policy = RoleNames.SuperAdmin)]
[Route("api/admin/store/dashboard")]
public sealed class AdminStoreDashboardController(IDbContextFactory<BenDataContext> dbFactory) : BenControllerBase
{
    public const int MaxDays = 366;

    [HttpGet]
    public async Task<ActionResult<StoreDashboardRecord>> Get([FromQuery] int? days, CancellationToken ct)
    {
        var window = Math.Clamp(days ?? 30, 1, MaxDays);
        var since = DateTime.UtcNow.Date.AddDays(1 - window);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var s = await StoreSettingsReader.ReadAsync(db, ct);

        var paid = db.StoreOrders.AsNoTracking()
            .Where(o => o.PaidUtc != null && o.PaidUtc >= since && o.Status != StoreOrderStatus.PendingPayment);
        var gross = await paid.SumAsync(o => (decimal?)o.Total, ct) ?? 0m;
        var refunded = await db.StoreRefunds.AsNoTracking()
            .Where(r => r.Status == StoreRefundStatus.Succeeded && r.CompletedUtc >= since)
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

        var held = await db.StoreOrderItems.AsNoTracking()
            .Where(i => i.Order.Status == StoreOrderStatus.PendingPayment && i.Order.ReservationReleasedUtc == null)
            .SumAsync(i => (int?)i.Quantity, ct) ?? 0;

        var threshold = s.LowStockThreshold;
        var low = await db.StoreProductVariants.AsNoTracking()
            .Where(v => v.IsActive && v.Product.IsActive && v.StockOnHand - v.StockReserved <= threshold)
            .OrderBy(v => v.StockOnHand - v.StockReserved).ThenBy(v => v.Product.Name)
            .Select(v => new { v.ProductId, Product = v.Product.Name, v.Id, v.Sku, v.Name, Available = v.StockOnHand - v.StockReserved })
            .Take(50)
            .ToListAsync(ct);

        return Ok(new StoreDashboardRecord(
            s.Enabled, s.CheckoutEnabled, window,
            OrdersToPack: await db.StoreOrders.CountAsync(o => o.Status == StoreOrderStatus.Paid, ct),
            OrdersToShip: await db.StoreOrders.CountAsync(o => o.Status == StoreOrderStatus.Packed, ct),
            OrdersNeedingAttention: await db.StoreOrders.CountAsync(o => o.NeedsAttention, ct),
            ReviewsPending: await db.StoreReviews.CountAsync(r => r.Status == StoreReviewStatus.Pending, ct),
            UnitsHeldByOpenCheckouts: held,
            OrdersInRange: await paid.CountAsync(ct),
            Sales: new StoreSales(gross, refunded, gross - refunded),
            LowStock: low.Select(v => new StoreLowStockRow(
                v.ProductId, v.Product, v.Id, v.Sku, StorePriceCaches.Label(v.Name), v.Available, threshold)).ToList()));
    }
}
