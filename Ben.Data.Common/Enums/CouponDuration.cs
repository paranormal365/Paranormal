namespace Ben.Data.Common.Enums;

/// <summary>How long a coupon keeps discounting once an organization has redeemed it.</summary>
public enum CouponDuration
{
    /// <summary>One billing period only.</summary>
    Once = 0,

    /// <summary>A set number of billing periods — see <c>Coupon.DurationPeriods</c>.</summary>
    Repeating = 1,

    /// <summary>
    /// Every period, for as long as the subscription runs.
    /// </summary>
    /// <remarks>
    /// Worth granting deliberately: a forever coupon is a permanent price change for that
    /// organization, and no later edit to the price list reaches them.
    /// </remarks>
    Forever = 2,
}
