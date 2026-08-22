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
/// Everything about the moment a code is being typed that a coupon might care about.
/// </summary>
/// <param name="UtcNow">The moment, for the redemption window.</param>
/// <param name="RedeemingAppUserId">Who is signed in, for a code addressed to one person.</param>
/// <param name="Interval">The cadence being bought, for a cadence-restricted coupon.</param>
/// <param name="IsRenewal">Whether this organization has ever had a paid period.</param>
/// <param name="AlreadyRedeemedByThisOrg">Whether this campaign has been used here before.</param>
/// <remarks>
/// A record rather than five parameters. The three booleans and the two ids are all easy to pass
/// in the wrong order, and every one of them changes whether somebody gets a discount — the sort
/// of mistake that produces a wrong answer rather than a compiler error.
/// </remarks>
public readonly record struct CouponRedemptionContext(
    DateTime UtcNow,
    Guid RedeemingAppUserId,
    BillingInterval Interval,
    bool IsRenewal,
    bool AlreadyRedeemedByThisOrg);

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
    /// <summary>Whether a subscription's next period counts as a renewal rather than a first buy.</summary>
    /// <remarks>
    /// "Has ever paid", not "is paying now". A group that lapsed in March is who a renewal coupon
    /// is written for, and reading this as "currently active" would shut them out of it.
    /// </remarks>
    public static bool IsRenewal(OrganizationSubscription subscription) =>
        subscription.FirstPaidPeriodStartUtc is not null;

    /// <summary>Why this code cannot be redeemed now, or null when it can.</summary>
    /// <remarks>
    /// <para>The order of these checks is the order the sentences should be read in, not an
    /// arbitrary one. "Your group has already used that code" comes before "that code has been
    /// fully claimed" because the first is actionable and the second is not, and a person who sees
    /// only the second will go looking for a different code they do not need.</para>
    ///
    /// <para>Every message is written for the person typing, and none of them says which internal
    /// rule fired. The one place that leaks anything is the addressed-code message, which admits
    /// the code exists — but they are holding it, so there is nothing there to learn.</para>
    /// </remarks>
    public static string? WhyNotRedeemable(Coupon coupon, CouponCode code, CouponRedemptionContext ctx)
    {
        if (!coupon.IsActive || !code.IsActive)
            return "That code is no longer available.";

        if (ctx.AlreadyRedeemedByThisOrg)
            return "Your group has already used that code.";

        if (coupon.ValidFromUtc is { } from && ctx.UtcNow < from)
            return "That code cannot be used yet.";

        if (coupon.RedeemByUtc is { } by && ctx.UtcNow > by)
            return "That code has expired.";

        if (code.RestrictedToAppUserId is { } owner && owner != ctx.RedeemingAppUserId)
            return "That code was issued to a different account.";

        if (code.MaxRedemptions is { } perCode && code.RedemptionCount >= perCode)
            return "That code has already been used.";

        if (coupon.MaxRedemptions is { } max && coupon.RedemptionCount >= max)
            return "That code has been fully claimed.";

        if (coupon.AppliesToInterval is { } only && only != ctx.Interval)
            return $"That code only applies to {Describe(only)} billing.";

        if (coupon.AppliesTo == CouponApplicability.NewSubscriptionsOnly && ctx.IsRenewal)
            return "That code is for groups subscribing for the first time.";

        if (coupon.AppliesTo == CouponApplicability.RenewalsOnly && !ctx.IsRenewal)
            return "That code applies to a renewal, and this would be your group's first period.";

        if (Misconfiguration(coupon) is { } bad)
            return bad;

        return null;
    }

    /// <summary>The cadence in the words a person would use, for a message rather than a label.</summary>
    private static string Describe(BillingInterval interval) => interval switch
    {
        BillingInterval.Monthly    => "monthly",
        BillingInterval.Quarterly  => "quarterly",
        BillingInterval.HalfYearly => "six-monthly",
        BillingInterval.Yearly     => "yearly",
        _                          => interval.ToString().ToLowerInvariant(),
    };

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
        if (coupon.ValidFromUtc is { } from && coupon.RedeemByUtc is { } by && from > by)
            return "That code stops being valid before it starts.";

        return null;
    }

    /// <summary>
    /// Why a generated batch does not make sense as a batch, independent of any one code.
    /// </summary>
    /// <remarks>
    /// A <see cref="CouponKind.Generated"/> campaign whose codes are unlimited is a shared code
    /// with extra steps, and a <see cref="CouponKind.Shared"/> campaign with several codes has no
    /// single code to print. Neither is caught by <see cref="Misconfiguration"/>, because both are
    /// about the codes rather than the discount — and both produce a coupon that works but does
    /// not do what the person who made it meant.
    /// </remarks>
    public static string? BatchMisconfiguration(Coupon coupon, IReadOnlyList<CouponCode> codes)
    {
        if (codes.Count == 0)
            return "That campaign has no codes, so there is nothing to redeem.";

        if (coupon.Kind == CouponKind.Shared && codes.Count > 1)
            return $"A shared campaign has one code; this one has {codes.Count}. "
                 + "Make it a generated batch, or remove the extra codes.";

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
    ///
    /// <para>A percentage applies to whatever the period costs, so 20% off a yearly period is a
    /// fifth of the year — which is why <see cref="Coupon.AppliesToInterval"/> exists. A fixed
    /// amount does not scale, and "$5 off" against a yearly price is $5, not $60.</para>
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
