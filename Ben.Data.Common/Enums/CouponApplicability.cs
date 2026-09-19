namespace Ben.Data.Common.Enums;

/// <summary>Which billing events a coupon may be redeemed against.</summary>
/// <remarks>
/// The two ends of this are different offers with different purposes and they are easy to confuse
/// on a form: <see cref="NewSubscriptionsOnly"/> buys new groups, <see cref="RenewalsOnly"/> keeps
/// existing ones. Issuing the first when you meant the second gives a discount to everybody except
/// the people you were trying to keep.
/// </remarks>
public enum CouponApplicability
{
    /// <summary>Redeemable whenever — a first period or any renewal.</summary>
    Any = 0,

    /// <summary>Only when the organization has never had a paid period. An acquisition offer.</summary>
    NewSubscriptionsOnly = 1,

    /// <summary>
    /// Only against a renewal of an existing subscription — a retention or win-back offer.
    /// </summary>
    /// <remarks>
    /// "Has had a paid period before" is the test, not "is currently paying". A group that lapsed
    /// last month is exactly who a renewal coupon is for, and reading this as "currently active"
    /// would exclude them.
    /// </remarks>
    RenewalsOnly = 2,
}
