namespace Ben.Data.Common.Enums;

/// <summary>How a part's price was written down (store sellers, backlog 251, P4).</summary>
public enum StorePartPriceBasis
{
    /// <summary>The price of a whole pack — a bag of 100 resistors for $6.99.</summary>
    PerPack = 0,

    /// <summary>The price of one piece.</summary>
    PerPiece = 1,
}
