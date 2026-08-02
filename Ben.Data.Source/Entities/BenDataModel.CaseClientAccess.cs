using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>Grants a secondary user client-level access to a case (read/write occurrences).</summary>
    public class CaseClientAccess : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }

        /// <summary>The secondary user being granted access.</summary>
        public Guid AppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
