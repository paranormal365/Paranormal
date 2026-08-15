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

        /// <summary>
        /// Where the investigation actually happened, resolved from <see cref="Location"/> when
        /// one is given and from the case's own address otherwise.
        /// </summary>
        /// <remarks>
        /// Carried on the investigation rather than read from the case, because a team often works
        /// somewhere other than the address on file — a cemetery, a second building, the woods
        /// behind the property — and the map should show where they were, not where the paperwork
        /// says the case is.
        /// </remarks>
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }

        /// <summary>
        /// Why this investigation has no coordinates, or null when it has them.
        /// </summary>
        /// <remarks>
        /// Recorded rather than left as a silent pair of nulls. A missing dot on the map is
        /// otherwise indistinguishable from an investigation nobody has looked at, and somebody
        /// needs to be able to see that the address simply could not be found and fix it.
        /// </remarks>
        public string? GeocodeNote { get; set; }

        /// <summary>When the coordinates were last resolved.</summary>
        public DateTime? DateGeocoded { get; set; }
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
