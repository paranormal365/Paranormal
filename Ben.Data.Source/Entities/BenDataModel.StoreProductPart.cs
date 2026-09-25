using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One part an item is built from, and what it costs (store sellers, backlog 251, P4).
    /// </summary>
    /// <remarks>
    /// <para>The parts' costs, plus the item's "other" line, are its cost basis — half of what a seller
    /// is paid for each unit (Ben, 09/24/2026: cost basis + asking price). Never shown to shoppers.</para>
    ///
    /// <para>Prices are kept to the hundredth of a cent: a resistor is $0.0699 a piece, and rounding
    /// each part to the cent would drift the total. <c>StoreCostMath</c> rounds once, at the end.</para>
    /// </remarks>
    public class StoreProductPart
    {
        public const int MaxNameLength = 200;
        public const int MaxUrlLength = 500;

        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public StorePartPriceBasis PriceBasis { get; set; }
        public decimal Price { get; set; }
        public int PiecesPerPack { get; set; } = 1;
        public decimal QuantityPerUnit { get; set; }
        public string? InfoUrl { get; set; }
        public string? BuyUrl { get; set; }
        public Guid? ThumbnailUploadFileId { get; set; }

        /// <summary>Pieces in the workshop, when the seller counts them; null when not.</summary>
        public int? OnHand { get; set; }

        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        public virtual StoreProduct Product { get; set; } = null!;
    }
}
