namespace Ben.Data.Common.Enums;

/// <summary>Why a variant's stock on hand changed (storefront).</summary>
/// <remarks>
/// The first three are an admin's; the last three are written by the system and refused if an admin
/// tries to use them by hand, so "why is it 3?" always has an honest answer.
/// </remarks>
public enum StoreStockReason
{
    /// <summary>New stock arrived.</summary>
    Received = 0,

    /// <summary>A count was wrong.</summary>
    Correction = 1,

    /// <summary>Stock written off.</summary>
    Damaged = 2,

    /// <summary>Written when an order is paid.</summary>
    Sold = 3,

    /// <summary>Written when a refund puts items back.</summary>
    Refunded = 4,

    /// <summary>Written when a cancelled order puts items back.</summary>
    Cancelled = 5,
}
