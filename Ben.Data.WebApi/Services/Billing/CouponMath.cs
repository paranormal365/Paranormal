using Ben.Data.Common.Enums;
using Ben.Data.Source.Entities;

namespace Ben.Data.WebApi.Services.Billing;

/// <summary>
/// What an organization actually pays for a period, once a coupon is taken into account.
/// </summary>
/// <param name="ListPrice">The band's price before any discount.</param>
/// <param name="Discount">How much came off. Never more than <paramref name="ListPrice"/>.</param>
/// <param name="Payable">What is charged. Never negative.</param>
public readonly record struct PeriodPrice(decimal ListPrice, decimal Discount, decimal Payable);

/// <summary>
/// Applies a coupon to a period's price, and says when a coupon may not be redeemed at all.
/// </summary>
/// <remarks>
/// Kept apart from the entities and from the database so the arithmetic can be tested directly.
/// Money rounding and "can this be used?" are exactly the rules that are easy to get subtly wrong
/// and hard to notice, because the failure is a slightly wrong number rather than an exception.
/// </remarks>
public static class CouponMath
{
    /// <summary>Why this coupon cannot be redeemed now, or null when it can.</summary>
    public static string? WhyNotRedeemable(Coupon coupon, DateTime utcNow, bool alreadyRedeemedByThisOrg)
    {
        if (!coupon.IsActive)                     return "That code is no longer available.";
        if (alreadyRedeemedByThisOrg)             return "Your group has already used that code.";
        if (coupon.RedeemByUtc is { } by && utcNow > by)
                                                  return "That code has expired.";
        if (coupon.MaxRedemptions is { } max && coupon.RedemptionCount >= max)
                                                  return "That code has been fully claimed.";
        if (Misconfiguration(coupon) is { } bad)  return bad;

        return null;
    }

    /// <summary>
    /// Why the coupon itself does not make sense, independent of who is redeeming it.
    /// </summary>
    /// <remarks>
    /// Checked at redemption as well as at creation. A coupon can be edited after it is issued, and
    /// an edit that empties both discount fields would otherwise produce a 100%-off code by
    /// accident — the discount would simply be zero, and nobody would see a failure.
    /// </remarks>
    public static string? Misconfiguration(Coupon coupon)
    {
        var hasPercent = coupon.PercentOff is > 0;
        var hasAmount  = coupon.AmountOff is > 0;

        if (hasPercent && hasAmount)
            return "That code sets both a percentage and a fixed amount, so it has no single meaning.";
        if (!hasPercent && !hasAmount)
            return "That code takes nothing off.";
        if (coupon.PercentOff is { } pct && pct > 100)
            return "That code takes off more than the whole price.";
        if (coupon.Duration == CouponDuration.Repeating && coupon.DurationPeriods is not > 0)
            return "That code repeats, but for no periods.";

        return null;
    }

    /// <summary>
    /// The price for one period. Pass <paramref name="coupon"/> as null when none applies.
    /// </summary>
    /// <remarks>
    /// <para>Rounded to whole cents away from zero, the convention for money in this codebase's
    /// currency. Banker's rounding — the .NET default — would quietly shave a cent off half the
    /// discounts, which is the kind of thing nobody reports and everybody notices eventually.</para>
    ///
    /// <para>A discount larger than the price yields a payable of zero, not a credit. The platform
    /// does not owe anybody money; a 100%-off coupon on a $15 band is a free month.</para>
    /// </remarks>
    public static PeriodPrice PriceFor(decimal listPrice, Coupon? coupon)
    {
        if (listPrice <= 0 || coupon is null || Misconfiguration(coupon) is not null)
            return new PeriodPrice(listPrice, 0m, listPrice);

        var discount = coupon.PercentOff is { } pct
            ? Math.Round(listPrice * pct / 100m, 2, MidpointRounding.AwayFromZero)
            : coupon.AmountOff ?? 0m;

        discount = Math.Min(discount, listPrice);

        return new PeriodPrice(listPrice, discount, listPrice - discount);
    }

    /// <summary>
    /// Periods a fresh redemption should be granted: one, a set number, or null for forever.
    /// </summary>
    public static int? PeriodsFor(Coupon coupon) => coupon.Duration switch
    {
        CouponDuration.Once      => 1,
        CouponDuration.Repeating => coupon.DurationPeriods,
        CouponDuration.Forever   => null,
        _                        => 1,
    };

    /// <summary>Whether a redemption still has a period left to discount.</summary>
    public static bool IsStillApplying(CouponRedemption redemption) =>
        redemption.PeriodsRemaining is null || redemption.PeriodsRemaining > 0;
}
