using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A buyable combination of a product's options, with its own SKU, price and stock (storefront).
    /// </summary>
    /// <remarks>
    /// <para><b>Stock is two numbers.</b> <see cref="StockOnHand"/> is what is on the shelf;
    /// <see cref="StockReserved"/> is what open checkouts are holding. Available = on hand −
    /// reserved. A CHECK keeps reserved within on hand, and every change is one conditional
    /// UPDATE, so two buyers racing for the last unit cannot both win.</para>
    ///
    /// <para><b>OptionSignature</b> is the sorted value ids joined with "+" ("" for a product with
    /// no options), unique per product: two variants with the same choices would leave the product
    /// page unable to say which one a buyer picked.</para>
    /// </remarks>
    public class StoreProductVariant : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }

        /// <summary>Stock-keeping unit; unique across the store, stored upper-case.</summary>
        public string Sku { get; set; } = string.Empty;

        /// <summary>The choices in words — "Black / Large" — for carts, orders and letters.</summary>
        public string? Name { get; set; }

        public string OptionSignature { get; set; } = string.Empty;

        public decimal Price { get; set; }

        /// <summary>The struck-through "was" price; must be higher than <see cref="Price"/> when set.</summary>
        public decimal? CompareAtPrice { get; set; }

        public int StockOnHand { get; set; }
        public int StockReserved { get; set; }
        public int UnitsSold { get; set; }

        public bool IsActive { get; set; }

        /// <summary>The variant a product with no options is sold as.</summary>
        public bool IsDefault { get; set; }

        public int SortOrder { get; set; }

        public virtual StoreProduct Product { get; set; } = null!;
        public virtual ICollection<StoreProductVariantOptionValue> OptionValues { get; set; } = [];

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
