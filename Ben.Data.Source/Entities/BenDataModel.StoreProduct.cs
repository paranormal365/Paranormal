using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Something the store sells (storefront). Prices, SKUs and stock live on its variants.
    /// </summary>
    /// <remarks>
    /// <para><b>No price here.</b> Every product has at least one <see cref="StoreProductVariant"/>
    /// (a product with no options has one default variant), and the variant is what is priced,
    /// stocked and ordered. <see cref="MinPrice"/>/<see cref="MaxPrice"/> are caches recomputed on
    /// every variant write so a listing can sort and filter in one query.</para>
    ///
    /// <para><b>Descriptions are born sanitised</b> — <see cref="LongDescriptionHtml"/> goes through
    /// the CMS sanitiser on every write, because it is rendered as markup to anonymous visitors.</para>
    /// </remarks>
    public class StoreProduct : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CategoryId { get; set; }

        /// <summary>The equipment-catalogue model this is, when it is one — the product page links to it.</summary>
        public Guid? EquipmentModelId { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>The address segment, /store/p/{slug}. Admin-editable; never rewritten on rename.</summary>
        public string Slug { get; set; } = string.Empty;

        public string? ShortDescription { get; set; }
        public string? LongDescriptionHtml { get; set; }

        public bool IsActive { get; set; }
        public bool IsFeatured { get; set; }
        public int SortOrder { get; set; }

        /// <summary>Shows the "New" badge until this moment.</summary>
        public DateTime? NewUntilUtc { get; set; }

        /// <summary>Stripe Tax product code; null means general tangible goods (txcd_99999999).</summary>
        public string? StripeTaxCode { get; set; }

        /// <summary>Cache: the lowest active variant price.</summary>
        public decimal MinPrice { get; set; }

        /// <summary>Cache: the highest active variant price.</summary>
        public decimal MaxPrice { get; set; }

        /// <summary>Cache for the "popular" sort: units sold across every variant.</summary>
        public int UnitsSold { get; set; }

        public int ViewCount { get; set; }

        /// <summary>Cache: the average of approved review ratings.</summary>
        public decimal AverageRating { get; set; }

        /// <summary>Cache: the number of approved reviews.</summary>
        public int ReviewCount { get; set; }

        public virtual StoreCategory Category { get; set; } = null!;
        public virtual EquipmentModel? EquipmentModel { get; set; }
        public virtual ICollection<StoreProductOption> Options { get; set; } = [];
        public virtual ICollection<StoreProductVariant> Variants { get; set; } = [];
        public virtual ICollection<StoreProductImage> Images { get; set; } = [];
        public virtual ICollection<StoreProductSpec> Specs { get; set; } = [];

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
