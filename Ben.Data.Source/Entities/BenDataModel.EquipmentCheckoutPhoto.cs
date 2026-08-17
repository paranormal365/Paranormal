using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A photo of a piece of equipment's condition at one end of a loan — as it went out, or as it
    /// came back.
    /// </summary>
    /// <remarks>
    /// Hangs off the loan rather than the item, because condition is a fact about a particular
    /// hand-over and not about the gear in general. Rides the existing <see cref="UploadFile"/>
    /// pipeline like every other image in the app, with <c>NoAction</c> on that FK so deleting a
    /// photo can never take the record of the loan with it.
    /// </remarks>
    public class EquipmentCheckoutPhoto : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid EquipmentCheckoutId { get; set; }
        public Guid UploadFileId { get; set; }

        public EquipmentPhotoStage Stage { get; set; }
        public string? Caption { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual EquipmentCheckout EquipmentCheckout { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
