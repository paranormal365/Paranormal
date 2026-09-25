namespace Ben.Data.Common.Enums;

/// <summary>What a line of a seller's earnings is for (store sellers, backlog 251, P10).</summary>
public enum StoreEarningKind
{
    /// <summary>Units of theirs that shipped: cost basis + asking price, each.</summary>
    Sale = 0,

    /// <summary>The label for a package they shipped: the store's flat rate, even when it shipped free.</summary>
    Shipping = 1,

    /// <summary>Units refunded after they shipped — the sale's earning, taken back. Negative.</summary>
    Refund = 2,

    /// <summary>A correction by the store's staff, with a note; either sign.</summary>
    Adjustment = 4,
}
