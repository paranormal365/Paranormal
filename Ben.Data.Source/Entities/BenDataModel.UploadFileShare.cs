using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Grants one of four share targets (person, investigation team, organization, public) on an
    /// UploadFile, for the universal media library's cross-scope aggregation. Additive alongside
    /// the existing tiered <see cref="UploadFileOrganizationShare"/> — does not replace it.
    /// </summary>
    public class UploadFileShare : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid UploadFileId { get; set; }
        public ShareTargetType TargetType { get; set; }

        // Exactly one of these three is set, matching TargetType. Public sets none.
        public Guid? TargetAppUserId { get; set; }
        public Guid? TargetInvestigationId { get; set; }
        public Guid? TargetOrganizationId { get; set; }

        public Guid SharedByAppUserId { get; set; }
        public bool IsActive { get; set; }
        public Guid? RemovedByAppUserId { get; set; }
        public DateTime? RemovalDate { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser? TargetAppUser { get; set; }
        public virtual Investigation? TargetInvestigation { get; set; }
        public virtual Organization? TargetOrganization { get; set; }
        public virtual AppUser SharedByAppUser { get; set; } = null!;
        public virtual AppUser? RemovedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
