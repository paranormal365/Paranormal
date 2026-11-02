using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Free months a group earned on its plan: a referral that paid, or a prize in the founding groups promotion
    /// (11/02/2026).
    /// </summary>
    /// <remarks>
    /// <para><b>Used by moving the plan's period end.</b> A group on a free year has the months added after it;
    /// a paying group's next renewal comes that much later. A group with no plan running keeps them waiting until
    /// it has one.</para>
    ///
    /// <para><b><see cref="Key"/> is unique</b>, so a job that runs every few minutes can never give the same
    /// month twice: "referral:{group}", "month:{promotion}:{yyyy-MM}", "reached:{entry}", "second-year:{entry}".</para>
    /// </remarks>
    public partial class OrganizationFreeMonths : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public FreeMonthReason Reason { get; set; }
        public int Months { get; set; }
        public string Key { get; set; } = string.Empty;

        public DateTime EarnedUtc { get; set; }

        /// <summary>When they were added to the plan, and the period end that made.</summary>
        public DateTime? AppliedUtc { get; set; }
        public DateTime? AppliedToPeriodEndUtc { get; set; }

        /// <summary>When they were taken back, before being used, and why.</summary>
        public DateTime? RevokedUtc { get; set; }
        public string? RevokedReason { get; set; }

        /// <summary>The group whose payment earned a referral month.</summary>
        public Guid? SourceOrganizationId { get; set; }
        public Guid? PromotionEntryId { get; set; }
        public string? Note { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual Organization? SourceOrganization { get; set; }
    }
}
