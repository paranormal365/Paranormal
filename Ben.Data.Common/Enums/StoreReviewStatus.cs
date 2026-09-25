namespace Ben.Data.Common.Enums;

/// <summary>Moderation state of a product review (storefront). Only Approved reviews are public.</summary>
public enum StoreReviewStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}
