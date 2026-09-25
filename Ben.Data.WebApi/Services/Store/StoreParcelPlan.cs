namespace Ben.Data.WebApi.Services.Store;

/// <summary>One cart line as the package plan needs it: which variant, whose it is, what it comes to.</summary>
/// <param name="SellerAppUserId">The member who sells it; null for the site's own stock.</param>
public sealed record StoreParcelLine(Guid VariantId, Guid? SellerAppUserId, decimal LineTotal);

/// <summary>One package: everything one seller (or the site) ships, and what its shipping costs.</summary>
/// <param name="Number">1 for the site's own stock when there is any, then each seller in turn.</param>
/// <param name="Discount">The package's share of the code's discount, summed from its lines' shares.</param>
/// <param name="MoreForFree">How much more of this seller's items would make this package ship free; null when it already does, or never can.</param>
public sealed record StoreParcelQuote(
    int Number, Guid? SellerAppUserId, IReadOnlyList<Guid> VariantIds, decimal ItemsSubtotal, decimal Discount,
    decimal Shipping, bool IsFree, decimal? MoreForFree, decimal FlatRate = 0m)
{
    public decimal AfterDiscount => ItemsSubtotal - Discount;
}

/// <summary>What the plan came to: every line's share of the discount, the packages, and all their shipping.</summary>
public sealed record StoreParcelPlanResult(
    IReadOnlyDictionary<Guid, decimal> DiscountShares, IReadOnlyList<StoreParcelQuote> Parcels)
{
    public decimal Shipping => Parcels.Sum(p => p.Shipping);
    public bool AllFree => Parcels.All(p => p.IsFree);
}

/// <summary>
/// How a cart becomes packages and what each one's shipping costs (store sellers, P0): each seller
/// ships their own package, the buyer pays the store's flat rate per package, and a package ships
/// free once its own items — after their share of any code — reach the free-shipping amount
/// (Ben, 09/24/2026).
/// </summary>
/// <remarks>
/// <para><b>One function for the cart and the checkout.</b> Whether a package ships free turns on
/// its share of the discount, and <see cref="StoreCouponMath.Allocate"/> puts the rounding cent on
/// the largest line — the first of equals, so the answer depends on the lines' order. The cart
/// listed lines as they were added, the checkout by variant id; priced apart, the cart could say
/// "Free" and the checkout charge the flat rate. So the lines are ordered here, by variant id, and
/// both call this.</para>
///
/// <para><b>Package order</b>: the site's own stock first, then sellers by id — not by name, which a
/// person can change between the cart and the checkout.</para>
/// </remarks>
public static class StoreParcelPlan
{
    public static StoreParcelPlanResult Build(IEnumerable<StoreParcelLine> lines, decimal discount, decimal flatRate, decimal freeOver)
    {
        var sorted = lines.OrderBy(l => l.VariantId).ToList();
        var shares = StoreCouponMath.Allocate(sorted.Select(l => l.LineTotal).ToList(), discount);
        var shareOf = sorted.Select((l, i) => (l.VariantId, Share: shares[i])).ToDictionary(x => x.VariantId, x => x.Share);

        var parcels = sorted
            .GroupBy(l => l.SellerAppUserId)
            .OrderBy(g => g.Key.HasValue).ThenBy(g => g.Key)
            .Select((g, i) =>
            {
                var subtotal = g.Sum(l => l.LineTotal);
                var off = g.Sum(l => shareOf[l.VariantId]);
                var after = subtotal - off;
                var free = flatRate <= 0m || (freeOver > 0m && after >= freeOver);
                return new StoreParcelQuote(i + 1, g.Key, g.Select(l => l.VariantId).ToList(), subtotal, off,
                    free ? 0m : flatRate, free, !free && freeOver > 0m ? freeOver - after : null, Math.Max(flatRate, 0m));
            })
            .ToList();

        return new StoreParcelPlanResult(shareOf, parcels);
    }

    /// <summary>
    /// Shares the order's shipping tax out over its packages by their shipping, to the cent: the
    /// pieces add up exactly (the remainder on the package with the most shipping). Stripe Tax is
    /// asked about one shipping amount — every package goes to the same address under the same
    /// tax code from the site's one ship-from address, so the split is exact but for rounding.
    /// </summary>
    public static IReadOnlyList<decimal> SplitShippingTax(IReadOnlyList<decimal> parcelShipping, decimal shippingTax)
        => StoreCouponMath.Allocate(parcelShipping, shippingTax);
}
