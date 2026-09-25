using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One choice of an option — "Black", "Large" (storefront). At most 24 per option.
    /// </summary>
    /// <remarks>
    /// Retired rather than deleted while a variant still uses it: the variant's link to a value is
    /// NoAction, so a used value can never vanish from under an ordered variant.
    /// </remarks>
    public class StoreProductOptionValue : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OptionId { get; set; }
        public string Value { get; set; } = string.Empty;

        /// <summary>"#1a2b3c" for a swatch option; unused for pills.</summary>
        public string? SwatchHex { get; set; }

        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        public virtual StoreProductOption Option { get; set; } = null!;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
