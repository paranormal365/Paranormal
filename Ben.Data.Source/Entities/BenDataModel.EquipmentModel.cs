using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A specific gear model under an <see cref="EquipmentBrand"/>, tagged with an
    /// <see cref="EquipmentCategory"/>. Same accumulate-and-moderate shape as the brand it
    /// belongs to — see <see cref="EquipmentBrand"/> for the approval rules, which this mirrors.
    /// </summary>
    public class EquipmentModel : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid EquipmentBrandId { get; set; }
        public Guid EquipmentCategoryId { get; set; }

        public string Name { get; set; } = null!;

        /// <summary>The readable part of this model's address, as in <c>/equipment/zoom/h1n</c>.</summary>
        /// <remarks>Unique within the make, and regenerated on rename — see <see cref="EquipmentBrand.UrlName"/>.</remarks>
        public string? UrlName { get; set; }

        public string? ModelNumber { get; set; }
        public string? Description { get; set; }

        public bool IsApproved { get; set; }
        public Guid? ProposedByOrganizationId { get; set; }
        public Guid? ProposedByAppUserId { get; set; }
        public Guid? ApprovedByAppUserId { get; set; }
        public DateTime? DateApproved { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual EquipmentBrand EquipmentBrand { get; set; } = null!;
        public virtual EquipmentCategory EquipmentCategory { get; set; } = null!;
        public virtual Organization? ProposedByOrganization { get; set; }
        public virtual AppUser? ProposedByAppUser { get; set; }
        public virtual AppUser? ApprovedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<EquipmentItem> EquipmentItems { get; set; } = new List<EquipmentItem>();
    }
}
