namespace Ben.Service.Models.Store;

/// <summary>
/// What a sale is worth to each side (store sellers, backlog 251, P9): the seller's earning, the
/// card fee, and the store's markup. Pure, and shared by the admin's panels and checkout's snapshot.
/// </summary>
/// <remarks>
/// <para>Ben, 09/24/2026: the seller earns cost basis + their asking price a unit, fixed at
/// checkout; the store sets the selling price; the store absorbs discount codes and Stripe's fees.
/// So the store's margin on a unit is price − seller's earning (or − cost for its own stock), and
/// the fee comes out of that.</para>
///
/// <para>The fee here is an ESTIMATE, "if bought alone": Stripe charges a percentage plus a fixed
/// amount per payment, so a unit in a larger order carries less than the fixed part. The real fee
/// an order paid is read from Stripe after payment.</para>
/// </remarks>
public static class StoreEconomics
{
    /// <summary>Stripe's fee on a payment of <paramref name="amount"/>, rounded to the cent.</summary>
    public static decimal EstimatedFee(decimal amount, decimal feePercent, decimal feeFixed)
        => amount <= 0m ? 0m : StoreMoney.Round(amount * feePercent / 100m + feeFixed);

    /// <summary>What the seller is paid for a unit: cost basis plus their asking price. Nothing for the store's own stock.</summary>
    public static decimal SellerEarning(bool isSellers, decimal costBasis, decimal? ask)
        => isSellers ? StoreMoney.Round(costBasis + (ask ?? 0m)) : 0m;

    /// <summary>
    /// A price that covers the seller's earning (or the cost of the store's own), the store's markup
    /// on it, and the card fee a payment of that price would pay — rounded up to the cent.
    /// </summary>
    public static decimal SuggestedPrice(decimal owed, decimal markupPercent, decimal feePercent, decimal feeFixed)
    {
        if (owed <= 0m) return 0m;
        var beforeFee = owed * (1m + markupPercent / 100m);
        var gross = feePercent >= 100m ? beforeFee : (beforeFee + feeFixed) / (1m - feePercent / 100m);
        return Math.Ceiling(gross * 100m) / 100m;
    }

    /// <summary>The store's margin on a unit sold at <paramref name="price"/>, before the card fee.</summary>
    public static decimal Markup(decimal price, decimal owed) => StoreMoney.Round(price - owed);

    /// <summary>True when a unit at this price pays less than the seller's earning (or its cost) plus the fee — the store loses on it.</summary>
    public static bool BelowCost(decimal price, decimal owed, decimal feePercent, decimal feeFixed)
        => price > 0m && price < owed + EstimatedFee(price, feePercent, feeFixed);
}
