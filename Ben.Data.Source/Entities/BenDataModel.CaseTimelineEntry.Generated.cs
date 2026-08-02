using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A single entry on a case's timeline — may be a client experience report,
    /// investigator note, evidence submission, or research finding.
    /// </summary>
    public partial class CaseTimelineEntry
    {
        public Guid CaseId { get; set; }
        public Guid AuthorAppUserId { get; set; }
        public CaseTimelineEntryType EntryType { get; set; }

        /// <summary>When the event occurred (not when it was logged). Null = date unknown.</summary>
        public DateTime? EventDateTime { get; set; }

        public string? Title { get; set; }

        /// <summary>HTML-formatted description of the event or evidence.</summary>
        public string? Body { get; set; }

        /// <summary>When true, this entry is visible on the public case page.</summary>
        public bool IsPublic { get; set; }

        /// <summary>Client IP at submission time. SuperAdmin-only — never returned in public responses.</summary>
        public string? IpAddress { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual AppUser AuthorAppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<CaseTimelineEntryExperienceType> ExperienceTypes { get; set; } = new List<CaseTimelineEntryExperienceType>();
        public virtual ICollection<CaseTimelineEntryFile> Files { get; set; } = new List<CaseTimelineEntryFile>();
    }
}
