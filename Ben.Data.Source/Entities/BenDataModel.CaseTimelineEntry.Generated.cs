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

        /// <summary>
        /// The investigation this entry belongs to, or null for entries that aren't tied to one
        /// (client reports, background research). A "binder" is just this case's timeline filtered
        /// to one investigation — reusing the timeline rather than a parallel store means binder
        /// entries appear on the case timeline for free, with the same visibility rules and the
        /// same file attachments.
        /// </summary>
        public Guid? InvestigationId { get; set; }
        public CaseTimelineEntryType EntryType { get; set; }

        /// <summary>When the event occurred (not when it was logged). Null = date unknown.</summary>
        public DateTime? EventDateTime { get; set; }

        public string? Title { get; set; }

        /// <summary>HTML-formatted description of the event or evidence.</summary>
        public string? Body { get; set; }

        /// <summary>
        /// Who can see this entry. Replaces the old binary <c>IsPublic</c>, which conflated
        /// "the client may see it" with "the whole internet may see it".
        /// </summary>
        public CaseTimelineVisibility Visibility { get; set; }

        /// <summary>Client IP at submission time. SuperAdmin-only — never returned in public responses.</summary>
        public string? IpAddress { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual AppUser AuthorAppUser { get; set; } = null!;
        public virtual Investigation? Investigation { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<CaseTimelineEntryExperienceType> ExperienceTypes { get; set; } = new List<CaseTimelineEntryExperienceType>();
        public virtual ICollection<CaseTimelineEntryFile> Files { get; set; } = new List<CaseTimelineEntryFile>();
    }
}
