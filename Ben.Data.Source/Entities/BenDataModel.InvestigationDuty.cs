using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One duty a group hands out per investigation — "Lead Investigator", "Equipment",
    /// "Evidence Collection" (item 158).
    /// </summary>
    /// <remarks>
    /// <para>The third people-concept, distinct from both: titles (item 157) say what a member
    /// IS, roles (item 156) say what they MAY DO, duties say what they are DOING TONIGHT. A duty
    /// grants nothing beyond the visit it is assigned on; the one exception is the Lead duty,
    /// which writes through to <see cref="InvestigationAttendee.IsLead"/> so the existing
    /// manage-this-investigation logic keeps working unchanged.</para>
    ///
    /// <para><see cref="MinimumMemberLevelId"/> is the one sanctioned title→responsibility
    /// bridge: "the higher the title, the more responsibility they can take on." Compared by the
    /// ladder's SortOrder at assignment time, soft-enforced (the assigner may override with an
    /// explicit confirm), and null means anyone. Deleting the rung nulls the requirement.</para>
    /// </remarks>
    public partial class InvestigationDuty : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public string Name { get; set; } = null!;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;

        /// <summary>One holder at a time (the Lead), or a crew (Equipment, Evidence).</summary>
        public bool IsSingleHolder { get; set; }

        /// <summary>Minimum ladder rung to be handed this duty, or null for anyone. Soft:
        /// assignment below it needs an explicit override, never silently blocks.</summary>
        public Guid? MinimumMemberLevelId { get; set; }

        /// <summary>
        /// What holding this duty lets somebody do on the visit it was assigned for (item 160).
        /// Scoped to that visit and gone when the assignment is — duties still grant nothing
        /// standing.
        /// </summary>
        public Ben.Data.Common.Enums.InvestigationDutyCapabilities Capabilities { get; set; }

        /// <summary>
        /// Which titles may hold this duty (item 160). Empty means the matrix was never set for
        /// this duty, and <see cref="MinimumMemberLevelId"/> answers instead.
        /// </summary>
        public virtual ICollection<InvestigationDutyEligibility> Eligibility { get; set; }
            = new List<InvestigationDutyEligibility>();

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual OrganizationMemberLevel? MinimumMemberLevel { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
