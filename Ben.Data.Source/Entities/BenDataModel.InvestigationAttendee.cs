using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>A member invited or assigned to participate in a specific investigation.</summary>
    public class InvestigationAttendee
    {
        public Guid Id { get; set; }
        public Guid InvestigationId { get; set; }
        public Guid AppUserId { get; set; }

        /// <summary>e.g. "Lead Investigator", "Audio Technician", "Camera Operator"</summary>
        public string? AssignedRole { get; set; }

        /// <summary>Pre-event RSVP — set by the member once they are notified of the investigation.</summary>
        public RsvpStatus Rsvp { get; set; } = RsvpStatus.Invited;

        /// <summary>
        /// Whether the member actually attended. Null = not yet determined (investigation in future or in progress).
        /// </summary>
        public bool? DidAttend { get; set; }

        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual Investigation Investigation { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
