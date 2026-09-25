using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A picture of a product, in gallery order (storefront). The first is the card picture.
    /// </summary>
    /// <remarks>
    /// The <see cref="UploadFile"/> behind it is a site-owned upload: no owner person or group
    /// (<c>UploadFile.AppUserId</c> is the model's one cascading key to AppUsers, so it stays null),
    /// the store image file type, and no expiry — the media retention sweep never takes it.
    /// Optionally shown for one variant (<see cref="VariantId"/>), e.g. the camouflage finish.
    /// </remarks>
    public class StoreProductImage : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid ProductId { get; set; }
        public Guid? VariantId { get; set; }
        public Guid UploadFileId { get; set; }
        public int SortOrder { get; set; }
        public string? AltText { get; set; }

        public virtual StoreProduct Product { get; set; } = null!;
        public virtual StoreProductVariant? Variant { get; set; }
        public virtual UploadFile UploadFile { get; set; } = null!;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
