using Ben.Data.Common.Enums;
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
        /// <remarks>
        /// Written since the calendar was built and read by nothing until 2026-08-17 — an
        /// organization could tick it and nothing happened. It now means what it says: the event
        /// appears on the public site and any site user may say they are coming.
        /// </remarks>
        public bool IsPublic { get; set; }

        /// <summary>
        /// Where this event is, as a shared location rather than free text.
        /// </summary>
        /// <remarks>
        /// Carries two things a public listing needs and <see cref="Location"/> cannot give:
        /// coordinates to put it on a map, and <see cref="PlaceKind"/> to answer the question that
        /// decides whether it may be public at all.
        /// </remarks>
        public Guid? PlaceId { get; set; }

        /// <summary>
        /// Show the area but not the street address until somebody is actually coming.
        /// </summary>
        /// <remarks>
        /// The established pattern for public events at a location that should not be advertised to
        /// the world — a venue that does not want visitors outside the event, a site with access
        /// arranged in advance. The exact address is withheld <b>at the projection</b>, not hidden
        /// in the page, so a reader who is not attending never receives it.
        /// </remarks>
        public bool HideExactLocation { get; set; }

        /// <summary>How many people may say they are coming, or null for no limit.</summary>
        public int? AttendeeCapacity { get; set; }

        /// <summary>After this, no new attendees. Null means right up to the start.</summary>
        public DateTime? RsvpClosesAt { get; set; }

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
        /// <summary>
        /// The readable part of this event's public URL — <c>/o/{org}/events/{UrlName}</c>.
        /// </summary>
        /// <remarks>
        /// Generated from the date and title when an event is first made public, and then left
        /// alone. A GUID in a URL is a link nobody shares, and a slug that changed when somebody
        /// fixed a typo in the title would break every link already shared. Null while the event is
        /// private, because a private event has no URL to promise.
        /// </remarks>
        public string? UrlName { get; set; }

        /// <summary>Where this event is, when it names a shared place.</summary>
        public virtual Place? Place { get; set; }

    }
}
