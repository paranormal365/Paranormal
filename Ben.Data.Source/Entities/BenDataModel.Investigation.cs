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

        /// <summary>
        /// The organization that ran this investigation.
        /// </summary>
        /// <remarks>
        /// Required, and held directly rather than reached through the case. Once
        /// <see cref="CaseId"/> became optional there was nothing else tying a visit to an
        /// organization at all, and every org-scoped query that joined through the case would
        /// quietly stop returning case-less ones — a filter that silently drops rows is worse than
        /// one that fails.
        /// </remarks>
        public Guid OrganizationId { get; set; }

        /// <summary>
        /// The case this investigation belongs to, or null for a visit that has no client case —
        /// a group going to a landmark on its own account.
        /// </summary>
        /// <remarks>
        /// A case-less investigation must still say where it happened, so the invariant is
        /// <c>CaseId is not null || PlaceId is not null</c>. It is enforced in the controller
        /// rather than as a check constraint, because the InMemory provider used by the tests
        /// ignores check constraints entirely and a rule only the database knows is a rule the
        /// tests cannot see.
        /// </remarks>
        public Guid? CaseId { get; set; }

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

        /// <summary>
        /// Where this investigation happened, as a shared location rather than free text.
        /// </summary>
        /// <remarks>
        /// Nullable for now: today every investigation reaches a location through its case, and
        /// P2 is what makes a case-less investigation possible. Until then this is the map's
        /// identity for the visit, and it does not replace <see cref="Location"/> — a team often
        /// works somewhere other than the address on file, and that free text is still how they
        /// say so.
        /// </remarks>
        public Guid? PlaceId { get; set; }

        /// <summary>
        /// How widely this investigation's findings may be shared. See
        /// <see cref="InvestigationVisibility"/>; the default follows the place's kind.
        /// </summary>
        public InvestigationVisibility Visibility { get; set; } = InvestigationVisibility.GroupOnly;

        public virtual Place? Place { get; set; }

        public virtual Case? Case { get; set; }
        public virtual Organization Organization { get; set; } = null!;
        public virtual OrgCalendarEvent? OrgCalendarEvent { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<InvestigationAttendee> Attendees { get; set; } = new List<InvestigationAttendee>();

        /// <summary>One account per person who was there. See <see cref="InvestigationFinding"/>.</summary>
        public virtual ICollection<InvestigationFinding> Findings { get; set; } = new List<InvestigationFinding>();
    }
}
