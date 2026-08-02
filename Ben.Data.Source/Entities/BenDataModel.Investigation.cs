using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A scheduled investigation visit associated with a Case.
    /// One case can have multiple investigations over time.
    /// </summary>
    public partial class Investigation : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }

        /// <summary>Optional link to the org calendar event for this investigation.</summary>
        public Guid? OrgCalendarEventId { get; set; }

        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string? Location { get; set; }
        public DateTime ScheduledDateTime { get; set; }
        public DateTime? EndDateTime { get; set; }
        public InvestigationStatus Status { get; set; } = InvestigationStatus.Scheduled;

        /// <summary>Post-investigation notes and summary (HTML).</summary>
        public string? Notes { get; set; }

        /// <summary>Deadline after which no new evidence submissions are accepted for this investigation.</summary>
        public DateTime? EvidenceDueDate { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual OrgCalendarEvent? OrgCalendarEvent { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<InvestigationAttendee> Attendees { get; set; } = new List<InvestigationAttendee>();
    }
}
