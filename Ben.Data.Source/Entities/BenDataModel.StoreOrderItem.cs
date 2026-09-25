
namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One line of an order, frozen at checkout (storefront).
    /// </summary>
    /// <remarks>
    /// Names, SKU and prices are copied so an order reads the same after the product changes. The
    /// keys to the product and variant are NoAction: a product that has been sold cannot be deleted,
    /// only deactivated — a database fact, not a convention.
    /// </remarks>
    public class StoreOrderItem
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Guid ProductId { get; set; }
        public Guid VariantId { get; set; }

        public string ProductName { get; set; } = string.Empty;
        public string? VariantName { get; set; }
        public string Sku { get; set; } = string.Empty;

        public decimal UnitPrice { get; set; }
        public decimal? CompareAtPrice { get; set; }
        public int Quantity { get; set; }

        /// <summary>UnitPrice × Quantity.</summary>
        public decimal LineTotal { get; set; }

        /// <summary>This line's share of the coupon, allocated before tax was calculated.</summary>
        public decimal LineDiscount { get; set; }

        /// <summary>Stripe's tax for this line (on LineTotal − LineDiscount).</summary>
        public decimal TaxAmount { get; set; }

        public string? StripeTaxCode { get; set; }
        public int QuantityRefunded { get; set; }
        public int QuantityRestocked { get; set; }
        public Guid? ImageUploadFileId { get; set; }

        /// <summary>The package it ships in (store sellers P5). Null only on a row written before packages existed and not backfilled.</summary>
        public Guid? ParcelId { get; set; }
        public DateTime DateCreated { get; set; }

        public virtual StoreOrder Order { get; set; } = null!;
        public virtual StoreProduct Product { get; set; } = null!;
        public virtual StoreProductVariant Variant { get; set; } = null!;
        public virtual UploadFile? ImageUploadFile { get; set; }
        public virtual StoreOrderParcel? Parcel { get; set; }
    }
}
