using Ben.Data.Common.Enums;

namespace Ben.Web.Website.Library.Store.Shared;

/// <summary>
/// How an order's status reads to its buyer, and its colour (storefront S4.13) — one place, so
/// My Orders, the order page and the admin desk never call the same state two different things.
/// </summary>
public static class StoreOrderStatusText
{
    public static string Label(StoreOrderStatus status) => status switch
    {
        StoreOrderStatus.PendingPayment => "Awaiting payment",
        StoreOrderStatus.Paid => "Paid — being prepared",
        StoreOrderStatus.Packed => "Packed",
        StoreOrderStatus.Shipped => "Shipped",
        StoreOrderStatus.Delivered => "Delivered",
        StoreOrderStatus.Cancelled => "Cancelled",
        StoreOrderStatus.Refunded => "Refunded",
        _ => status.ToString(),
    };

    /// <summary>The Bootstrap colour name: text-{tone}, bg-{tone}-subtle.</summary>
    public static string Tone(StoreOrderStatus status) => status switch
    {
        StoreOrderStatus.Delivered => "success",
        StoreOrderStatus.Shipped => "primary",
        StoreOrderStatus.Paid or StoreOrderStatus.Packed => "info",
        StoreOrderStatus.Cancelled => "danger",
        StoreOrderStatus.PendingPayment => "warning",
        _ => "secondary",
    };
}
