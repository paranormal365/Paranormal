using Ben.Data.Common.Enums;
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

        /// <summary>When a unit of it last sold; null for a product that never has.</summary>
        public DateTime? LastSoldUtc { get; set; }

        public int ViewCount { get; set; }

        /// <summary>Cache: the average of approved review ratings.</summary>
        public decimal AverageRating { get; set; }

        /// <summary>Cache: the number of approved reviews.</summary>
        public int ReviewCount { get; set; }

        /// <summary>
        /// The member who makes and sells this item — a holder of the Seller role. Null for the
        /// site's own stock. Never shown to shoppers.
        /// </summary>
        public Guid? SellerAppUserId { get; set; }

        /// <summary>
        /// When it first went on sale; null for a draft that never has (store sellers, backlog 251).
        /// A seller may delete only an item that never has.
        /// </summary>
        public DateTime? FirstOnSaleUtc { get; set; }

        /// <summary>
        /// What the seller is paid per unit on top of cost, fixed when the store approves their sale
        /// request (store sellers, backlog 251). Null for the site's own stock.
        /// </summary>
        public decimal? SellerAskPerUnit { get; set; }

        /// <summary>
        /// The cost of a unit not in its parts list — glue, solder, packaging — added to the parts
        /// for its cost basis (store sellers, backlog 251, P4). Never shown to shoppers.
        /// </summary>
        public decimal OtherCostPerUnit { get; set; }

        /// <summary>What the "other" line covers, in the seller's words.</summary>
        public string? OtherCostNote { get; set; }

        /// <summary>
        /// Whether the product page shows its FAQ (store sellers P12). On unless whoever looks after the
        /// product turns it off; the page shows nothing either way until there's an entry.
        /// </summary>
        public bool FaqEnabled { get; set; } = true;

        // ── versions (store sellers P13) ─────────────────────────────────────
        // Ben's list: "A new version links back to the old one and the old one links forward. The
        // seller chooses what happens to the old one: sell out the remaining stock, keep offering it,
        // or discontinue it." A product has at most one newer version (a unique key).

        /// <summary>The version this one replaces; null for a first version.</summary>
        public Guid? PreviousVersionProductId { get; set; }

        /// <summary>What this version is called — "v2", "2026 edition". Shown beside the name.</summary>
        public string? VersionLabel { get; set; }

        /// <summary>What happens to the previous version when this one first goes on sale.</summary>
        public StoreSupersededPolicy SupersededPolicy { get; set; }

        /// <summary>When this version's policy was applied to the previous one — once, on its first day on sale.</summary>
        public DateTime? SupersededAppliedUtc { get; set; }

        /// <summary>Replaced, and selling what's left: taken off sale when its stock runs out.</summary>
        public DateTime? SellingOutSinceUtc { get; set; }

        /// <summary>
        /// Taken off sale because a newer version replaced it. Its page stays up — "no longer made,
        /// replaced by" — so a link or a search doesn't end on nothing.
        /// </summary>
        public DateTime? DiscontinuedUtc { get; set; }

        public virtual StoreProduct? PreviousVersion { get; set; }

        // ── page extras (store sellers P14) ──────────────────────────────────

        public const int MaxPolicyTextLength = 2000;

        /// <summary>
        /// Whether the page takes and shows reviews. The store's switch alone (a seller can't turn off
        /// the reviews of their own item); off hides the ones it has, and its stars everywhere.
        /// </summary>
        public bool ReviewsEnabled { get; set; } = true;

        /// <summary>This item's own words on returns, shown with the store's returns window. Plain text.</summary>
        public string? ReturnPolicyText { get; set; }

        /// <summary>This item's warranty, when it has one. Plain text.</summary>
        public string? WarrantyText { get; set; }

        public virtual StoreCategory Category { get; set; } = null!;
        public virtual AppUser? SellerAppUser { get; set; }
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
