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

        /// <summary>
        /// Whether this person is running <i>this</i> investigation.
        /// </summary>
        /// <remarks>
        /// <para>Deliberately separate from <see cref="AssignedRole"/> and from
        /// <c>OrganizationRole</c>, which are different things that are easy to conflate.
        /// <c>AssignedRole</c> is free text describing the job somebody is doing on the night
        /// ("Audio Technician") and grants nothing. <c>OrganizationRole</c> is standing rank in the
        /// group and outlives any one visit. This is neither: it is authority over one
        /// investigation, delegated for it and expiring with it.</para>
        ///
        /// <para>That is why it is a flag on the attendee rather than a rank on the member. A
        /// senior investigator is senior everywhere; the lead of Tuesday's visit is the lead of
        /// Tuesday's visit, and next week it is somebody else's turn. Read by
        /// <c>InvestigationAccess.CanManageAsync</c>.</para>
        /// </remarks>
        public bool IsLead { get; set; }

        /// <summary>Pre-event RSVP — set by the member once they are notified of the investigation.</summary>
        public RsvpStatus Rsvp { get; set; } = RsvpStatus.Invited;

        /// <summary>
        /// Whether the member actually attended. Null = not yet determined (investigation in future or in progress).
        /// </summary>
        public bool? DidAttend { get; set; }

        /// <summary>
        /// When they got there, as stated. Null until somebody says.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="DateCreated"/> — that is when the invitation was issued — and
        /// from the moment the record was written, because those are routinely hours or days apart.
        /// Investigations happen in cellars and woodland; checking in the following morning is the
        /// ordinary case, not an edge one, so the arrival time is stated rather than assumed from
        /// when the button was pressed.
        /// </remarks>
        public DateTime? DateArrived { get; set; }

        /// <summary>
        /// Who recorded the attendance. <b>Null means the person recorded it themselves.</b>
        /// </summary>
        /// <remarks>
        /// <para>This is the provenance, and it is the reason check-in is its own endpoint rather
        /// than a field anybody can set. "Checked in on site at 21:04" and "a manager ticked a box
        /// the following Tuesday" are different grades of evidence, and a single boolean cannot
        /// tell them apart.</para>
        ///
        /// <para>Null-means-self rather than storing the person's own id: the two states are then
        /// impossible to confuse, and an override always names somebody other than the attendee.</para>
        /// </remarks>
        public Guid? AttendanceRecordedByAppUserId { get; set; }

        public virtual AppUser? AttendanceRecordedByAppUser { get; set; }

        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual Investigation Investigation { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
