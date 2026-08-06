using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>Links an UploadFile to a case's general Files/Evidence tab. Deleting this row un-links the file — it does not delete the UploadFile.</summary>
    public class CaseFile : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }
        public Guid UploadFileId { get; set; }

        public string? Description { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
