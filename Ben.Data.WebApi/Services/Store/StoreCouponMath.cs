using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Store;

namespace Ben.Data.WebApi.Services.Store;

/// <summary>
/// What a discount code is worth and whether it may be used (storefront plan §5.4). No database.
/// </summary>
/// <remarks>
/// <para><b>Products only.</b> A discount comes off the products subtotal and is clamped to it: a
/// $50 code on a $39.99 order takes $39.99, and the buyer still pays shipping and tax. It never
/// reaches shipping or tax, and it never makes a total negative (<c>CK_StoreOrders_Total</c>).</para>
///
/// <para><b>The sentences are the buyer's.</b> They are shown as they are at the cart and at
/// checkout, so each says what is wrong with the code in words a shopper uses.</para>
/// </remarks>
public static class StoreCouponMath
{
    public const string NotRecognised = "That code isn't one we recognise.";
    public const string Expired = "That code has expired.";
    public const string NotYetActive = "That code isn't active yet.";
    public const string UsedUp = "That code has been used as many times as it can be.";
    public const string AlreadyUsedByBuyer = "That code has already been used with this email address.";

    public static string NeedsMinimum(decimal minimum) => $"That code needs an order of at least {StoreMoney.Format(minimum)}.";

    /// <summary>What is wrong with how a code is set up, for the admin; null when it is usable.</summary>
    public static string? Misconfiguration(StoreCoupon coupon)
    {
        if (string.IsNullOrWhiteSpace(coupon.Code)) return "Give the code something to type.";
        switch (coupon.Kind)
        {
            case StoreCouponKind.Percent when coupon.PercentOff is not (>= 1 and <= 100):
                return "A percent-off code takes between 1 and 100 percent.";
            case StoreCouponKind.Fixed when coupon.AmountOff is not > 0m:
                return "A dollars-off code needs an amount above $0.";
        }
        if (coupon.MinimumOrderAmount is < 0m) return "The minimum order can't be below $0.";
        if (coupon.StartsUtc is { } starts && coupon.EndsUtc is { } ends && ends <= starts)
            return "The code has to end after it starts.";
        if (coupon.MaxRedemptions is < 1) return "Leave the total uses empty for unlimited, or allow at least one.";
        if (coupon.MaxRedemptionsPerBuyer is < 1) return "Leave uses per buyer empty for unlimited, or allow at least one.";
        return null;
    }

    /// <summary>
    /// Why this buyer cannot use this code on this subtotal now; null when they can.
    /// </summary>
    /// <param name="priorRedemptionsByThisBuyer">
    /// Redemptions already held by this buyer — matched by email OR account, and including orders
    /// still waiting for payment (<see cref="StoreCouponReservations.PriorRedemptionsAsync"/>).
    /// </param>
    public static string? WhyNotRedeemable(StoreCoupon? coupon, decimal subtotal, int priorRedemptionsByThisBuyer, DateTime now)
    {
        if (coupon is null || !coupon.IsActive || Misconfiguration(coupon) is not null) return NotRecognised;
        if (coupon.StartsUtc is { } starts && now < starts) return NotYetActive;
        if (coupon.EndsUtc is { } ends && now >= ends) return Expired;
        if (coupon.MaxRedemptions is { } max && coupon.RedemptionCount >= max) return UsedUp;
        if (coupon.MaxRedemptionsPerBuyer is { } perBuyer && priorRedemptionsByThisBuyer >= perBuyer) return AlreadyUsedByBuyer;
        if (coupon.MinimumOrderAmount is { } minimum && subtotal < minimum) return NeedsMinimum(minimum);
        return null;
    }

    /// <summary>The discount on a products subtotal: rounded to the cent, never below $0 or above the subtotal.</summary>
    public static decimal DiscountFor(StoreCoupon coupon, decimal subtotal)
    {
        if (subtotal <= 0m) return 0m;
        var raw = coupon.Kind switch
        {
            StoreCouponKind.Percent => StoreMoney.Round(subtotal * (coupon.PercentOff ?? 0) / 100m),
            StoreCouponKind.Fixed   => coupon.AmountOff ?? 0m,
            _ => 0m,
        };
        return Math.Clamp(StoreMoney.Round(raw), 0m, subtotal);
    }

    /// <summary>
    /// Spreads a discount over the order's lines in proportion to their totals, to the cent. The
    /// pieces add up to exactly <paramref name="discount"/>: the rounding remainder goes on the
    /// largest line (the first of equals). Stripe Tax taxes each line after its share.
    /// </summary>
    public static IReadOnlyList<decimal> Allocate(IReadOnlyList<decimal> lineTotals, decimal discount)
    {
        var shares = new decimal[lineTotals.Count];
        var sum = lineTotals.Sum();
        if (lineTotals.Count == 0 || sum <= 0m || discount <= 0m) return shares;

        discount = Math.Min(StoreMoney.Round(discount), sum);
        for (var i = 0; i < shares.Length; i++)
            shares[i] = Math.Min(lineTotals[i], StoreMoney.Round(discount * lineTotals[i] / sum));

        var largest = 0;
        for (var i = 1; i < shares.Length; i++)
            if (lineTotals[i] > lineTotals[largest]) largest = i;

        shares[largest] += discount - shares.Sum();
        return shares;
    }
}
