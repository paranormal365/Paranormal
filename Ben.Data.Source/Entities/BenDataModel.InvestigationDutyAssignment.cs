using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>One attendee holding one duty on one investigation (item 158).</summary>
    /// <remarks>
    /// Keyed by attendee rather than user: the duty belongs to this visit, and an attendee row
    /// is already "this person, on this visit". <see cref="EligibilityOverridden"/> records that
    /// the assigner confirmed past the duty's minimum title — the senior called in sick and the
    /// capable junior stepped up; the override is deliberate and traceable, never silent.
    /// </remarks>
    public partial class InvestigationDutyAssignment : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid InvestigationAttendeeId { get; set; }
        public Guid InvestigationDutyId { get; set; }
        public bool EligibilityOverridden { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual InvestigationAttendee InvestigationAttendee { get; set; } = null!;
        public virtual InvestigationDuty InvestigationDuty { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
