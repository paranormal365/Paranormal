namespace Ben.Data.Common.Enums;

/// <summary>What a store discount code takes off (storefront).</summary>
/// <remarks>Separate from the subscription <see cref="CouponKind"/>, which is about how codes are issued, not what they take off.</remarks>
public enum StoreCouponKind
{
    /// <summary>A percentage of the products subtotal.</summary>
    Percent = 0,

    /// <summary>A fixed amount off the products subtotal, never more than the subtotal.</summary>
    Fixed = 1,
}
