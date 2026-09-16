using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A file or link kept beside a research page, in its side rail (beta feedback, 2026-09-14).
    /// </summary>
    /// <remarks>
    /// The rail exists independently of the blocks: a file can be uploaded and kept for later without being placed on the
    /// page, and a block that shows it refers to the upload itself, never to this row — so removing a block never loses
    /// the file, and removing the file is a deliberate act in the rail.
    /// </remarks>
    public class CaseResearchAttachment : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid ResearchEntryId { get; set; }
        public CaseResearchAttachmentKind Kind { get; set; }

        public Guid? UploadFileId { get; set; }

        /// <summary>Link: the address.</summary>
        public string? Url { get; set; }

        /// <summary>Link: the preview this address has, when one was made.</summary>
        public Guid? LinkPreviewId { get; set; }

        public string Title { get; set; } = null!;
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual CaseResearchEntry ResearchEntry { get; set; } = null!;
        public virtual UploadFile? UploadFile { get; set; }
        public virtual StoredLinkPreview? LinkPreview { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
