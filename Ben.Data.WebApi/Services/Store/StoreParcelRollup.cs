using Ben.Data.Common.Enums;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// An order's status, read from its packages' (store sellers, backlog 251, P7).
/// </summary>
/// <remarks>
/// <para>Ben, 09/24/2026: "If one has shipped and another has not shipped yet, the status is
/// Partially Shipped." A cancelled package is left out — an order whose other packages have all
/// arrived is delivered, whatever became of the one that was refunded unsent.</para>
///
/// <para>Pure, so the rule is tested alone and every transition applies the same one.</para>
/// </remarks>
public static class StoreParcelRollup
{
    /// <summary>The order's status for these packages; null when every package is cancelled (the refund decides then).</summary>
    public static StoreOrderStatus? Status(IEnumerable<StoreParcelStatus> parcels)
    {
        var live = parcels.Where(p => p != StoreParcelStatus.Cancelled).ToList();
        if (live.Count == 0) return null;
        if (live.All(p => p == StoreParcelStatus.Delivered)) return StoreOrderStatus.Delivered;
        if (live.All(p => p is StoreParcelStatus.Shipped or StoreParcelStatus.Delivered)) return StoreOrderStatus.Shipped;
        if (live.Any(p => p is StoreParcelStatus.Shipped or StoreParcelStatus.Delivered)) return StoreOrderStatus.PartiallyShipped;
        if (live.All(p => p == StoreParcelStatus.Packed)) return StoreOrderStatus.Packed;
        return StoreOrderStatus.Paid;
    }

    /// <summary>True once any package has left — the order can then no longer be cancelled whole or re-addressed.</summary>
    public static bool AnyOnItsWay(IEnumerable<StoreParcelStatus> parcels)
        => parcels.Any(p => p is StoreParcelStatus.Shipped or StoreParcelStatus.Delivered);
}
