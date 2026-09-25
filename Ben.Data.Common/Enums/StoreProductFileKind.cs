namespace Ben.Data.Common.Enums;

/// <summary>What a product's file is (store sellers, backlog 251, P11).</summary>
public enum StoreProductFileKind
{
    Manual = 0,
    Firmware = 1,
    Software = 2,
    Document = 3,
    Other = 4,
}

/// <summary>Who may have a product's file (store sellers, backlog 251, P11).</summary>
public enum StoreFileAudience
{
    /// <summary>The seller and the store's staff only — build notes, a supplier's datasheet.</summary>
    Private = 0,

    /// <summary>Anybody who has paid for the product: downloadable from their order.</summary>
    Buyers = 1,
}

/// <summary>Where a shopper's question about a product stands (store sellers P12).</summary>
public enum StoreQuestionStatus
{
    Open = 0,
    Answered = 1,
    Declined = 2,
}
