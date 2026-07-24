using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>An attendee invited to an org calendar event, with RSVP status and optional task assignment.</summary>
    public class OrgCalendarEventAttendee
    {
        public Guid Id { get; set; }
        public Guid OrgCalendarEventId { get; set; }
        public Guid AppUserId { get; set; }
        public RsvpStatus RsvpStatus { get; set; } = RsvpStatus.Invited;
        public string? AssignedTask { get; set; }
        public DateTime? DateRsvp { get; set; }
        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual OrgCalendarEvent OrgCalendarEvent { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
