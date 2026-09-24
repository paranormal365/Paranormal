namespace Ben.Data.Common.Enums;

/// <summary>Where a store refund is (storefront).</summary>
/// <remarks>
/// A refund row exists BEFORE Stripe is called, so a crash between the two leaves a Pending row an
/// admin can retry rather than money moved with no record.
/// </remarks>
public enum StoreRefundStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}
