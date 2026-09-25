namespace Ben.Data.Common.Enums;

/// <summary>Where one package of an order stands (store sellers, backlog 251, P5).</summary>
/// <remarks>The order's own status is rolled up from its packages' (<c>StoreParcelRollup</c>, P7).</remarks>
public enum StoreParcelStatus
{
    /// <summary>Placed or paid; not packed yet.</summary>
    Waiting = 0,

    Packed = 1,

    /// <summary>Handed to the carrier, with tracking or "No tracking provided".</summary>
    Shipped = 2,

    Delivered = 3,

    /// <summary>Refunded and not sent; left out of the order's roll-up.</summary>
    Cancelled = 4,
}
