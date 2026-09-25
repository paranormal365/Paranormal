using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One way a product varies — "Colour", "Size" (storefront). At most three per product.
    /// </summary>
    public class StoreProductOption : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public string Name { get; set; } = string.Empty;
        public StoreOptionKind Kind { get; set; }
        public int SortOrder { get; set; }

        public virtual StoreProduct Product { get; set; } = null!;
        public virtual ICollection<StoreProductOptionValue> Values { get; set; } = [];

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
