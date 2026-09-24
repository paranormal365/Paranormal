using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A shelf in the gear store — "EMF meters", "Spirit boxes" (storefront).
    /// </summary>
    /// <remarks>
    /// One level of subcategories (Ben, 09/24): <see cref="ParentCategoryId"/> names a top-level
    /// category, and a category that has subcategories is never itself one. The store's own table
    /// rather than <see cref="EquipmentCategory"/>, which has no slug and no picture and is proposed
    /// by members. Hiding a category takes its products off the store — and hiding a parent takes its
    /// subcategories' products with it (the "sellable" rule: variant AND product AND category AND the
    /// category's parent, if any, active).
    /// </remarks>
    public class StoreCategory : IAuditableEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>The address segment, /store/c/{slug}. Admin-editable; never rewritten on rename.</summary>
        public string Slug { get; set; } = string.Empty;

        public string? Description { get; set; }

        /// <summary>The tile and hero picture; a category with one becomes a slide on the store home.</summary>
        public Guid? ImageUploadFileId { get; set; }

        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        /// <summary>Shows the "New" badge beside the name in the category list.</summary>
        public bool IsNew { get; set; }

        /// <summary>The top-level category this is a subcategory of; null for a top-level category.</summary>
        public Guid? ParentCategoryId { get; set; }

        public virtual UploadFile? ImageUploadFile { get; set; }
        public virtual StoreCategory? ParentCategory { get; set; }
        public virtual ICollection<StoreCategory> Subcategories { get; set; } = [];
        public virtual ICollection<StoreProduct> Products { get; set; } = [];

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
