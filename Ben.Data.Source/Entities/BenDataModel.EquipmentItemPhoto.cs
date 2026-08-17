using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A photo of an <see cref="EquipmentItem"/> (its gallery — not a loan's condition photos,
    /// see <c>EquipmentCheckoutPhoto</c> in a later phase). Points at an <see cref="UploadFile"/>
    /// like every other image in the system.
    /// </summary>
    public class EquipmentItemPhoto : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid EquipmentItemId { get; set; }
        public Guid UploadFileId { get; set; }

        public bool IsPrimary { get; set; }
        public string? Caption { get; set; }
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual EquipmentItem EquipmentItem { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
