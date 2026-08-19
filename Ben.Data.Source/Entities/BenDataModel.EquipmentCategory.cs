using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Top-level category for paranormal-investigation equipment (e.g. Audio Recorder, EMF Meter).
    /// Global to the platform, not org-scoped. Flat and SuperAdmin-maintained — unlike
    /// <see cref="EquipmentBrand"/>/<see cref="EquipmentModel"/>, categories are seeded rather than
    /// user-proposed, since the useful set is small and doesn't grow with the user base.
    /// </summary>
    public class EquipmentCategory : IAuditableEntity
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;
        public string? Description { get; set; }
        public string? IconClass { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<EquipmentModel> EquipmentModels { get; set; } = new List<EquipmentModel>();
    }
}
