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

        // ── Research pages (beta feedback, 2026-09-14) ─────────────────────────────────────────────
        // A note is a page of blocks. The draft is the author's until Publish; the published copy is what
        // everybody else in the group reads. Both are block-document JSON (Ben.Data.Common.Blocks).

        /// <summary>When what the page is about happened, if it happened at a time — puts the page on the timeline.</summary>
        public DateTime? EventDateTime { get; set; }

        public string? DraftBlocksJson { get; set; }
        public string? PublishedBlocksJson { get; set; }

        /// <summary>Goes up by one with every saved draft; a save made against an older one is refused.</summary>
        public int DraftRevision { get; set; }

        /// <summary>The draft revision that was last published.</summary>
        public int? PublishedRevision { get; set; }

        public DateTime? DraftSavedUtc { get; set; }

        /// <summary>Whoever holds the unpublished draft. Nobody else may save over it until it is published.</summary>
        public Guid? DraftAuthorAppUserId { get; set; }

        /// <summary>The editor's id for the last save it sent, so a retried save is recognised rather than counted twice.</summary>
        public Guid? DraftSaveId { get; set; }

        public DateTime? PublishedUtc { get; set; }
        public Guid? PublishedByAppUserId { get; set; }

        /// <summary>Plain text from the published page, for lists and the timeline.</summary>
        public string? Excerpt { get; set; }

        public virtual ICollection<CaseResearchAttachment> Attachments { get; set; } = new List<CaseResearchAttachment>();

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
