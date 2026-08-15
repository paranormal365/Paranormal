using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>A calendar event for an organization. Supports recurrence via iCal RRULE.</summary>
    public partial class OrgCalendarEvent : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }

        /// <summary>Optional event type (Meeting, Investigation, etc.). Null = general event.</summary>
        public Guid? EventTypeId { get; set; }

        /// <summary>Optional case this event is associated with (investigation scheduling).</summary>
        public Guid? CaseId { get; set; }

        public string Title { get; set; } = null!;
        public string? Description { get; set; }
        public string? Location { get; set; }

        /// <summary>
        /// One of the organization's own addresses, when the event is somewhere they have on file.
        /// </summary>
        /// <remarks>
        /// Optional, and independent of <see cref="Location"/>: an event can name a saved address,
        /// free text, both, or neither. Kept as a reference rather than copied text so a corrected
        /// address corrects every event held there.
        /// </remarks>
        public Guid? OrganizationAddressId { get; set; }

        /// <summary>Video call link — Zoom, Teams, whatever the group uses. Optional.</summary>
        public string? MeetingUrl { get; set; }
        public DateTime StartDateTime { get; set; }
        public DateTime EndDateTime { get; set; }
        public bool IsAllDay { get; set; }

        /// <summary>Visible to users outside the organization.</summary>
        public bool IsPublic { get; set; }

        /// <summary>
        /// iCal RRULE string for recurring events (e.g. "FREQ=MONTHLY;BYDAY=TU;BYSETPOS=1").
        /// Null = one-time event.
        /// </summary>
        public string? RecurrenceRule { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual OrganizationAddress? OrganizationAddress { get; set; }
        public virtual OrgCalendarEventType? EventType { get; set; }
        public virtual Case? Case { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<OrgCalendarEventAttendee> Attendees { get; set; } = new List<OrgCalendarEventAttendee>();
    }
}
