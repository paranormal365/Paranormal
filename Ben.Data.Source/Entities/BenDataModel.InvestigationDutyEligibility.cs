using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One cell of the title-by-duty eligibility matrix: this title may hold this duty (item 160).
    /// </summary>
    /// <remarks>
    /// <para><b>Rows mean eligible, absence means not.</b> A duty with no rows at all falls back to
    /// <see cref="InvestigationDuty.MinimumMemberLevelId"/>, which is the single-threshold case the
    /// matrix generalises — so a group that has never opened the matrix keeps exactly the behaviour
    /// item 158 gave it, and nothing had to be backfilled.</para>
    ///
    /// <para>Eligibility stays <b>soft</b> either way: the assignment door refuses and offers an
    /// override that is recorded, because the senior calls in sick and the capable junior steps up.
    /// A hard wall would push the group back to organising by text message.</para>
    /// </remarks>
    public partial class InvestigationDutyEligibility : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid InvestigationDutyId { get; set; }
        public Guid OrganizationMemberLevelId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual InvestigationDuty InvestigationDuty { get; set; } = null!;
        public virtual OrganizationMemberLevel OrganizationMemberLevel { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
