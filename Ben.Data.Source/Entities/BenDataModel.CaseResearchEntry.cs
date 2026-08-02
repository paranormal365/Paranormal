using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>A single research item (note, URL, or file) collected for a case.</summary>
    public class CaseResearchEntry : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }

        public CaseResearchType ResearchType { get; set; } = CaseResearchType.Note;

        public string Title { get; set; } = null!;

        /// <summary>HTML body for notes; description text for links and files.</summary>
        public string? Body { get; set; }

        /// <summary>External URL for Link-type entries.</summary>
        public string? Url { get; set; }

        /// <summary>Linked upload file for File-type entries.</summary>
        public Guid? UploadFileId { get; set; }

        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual UploadFile? UploadFile { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
