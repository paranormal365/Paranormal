using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>An applicant's answer to a custom membership question.</summary>
    public partial class OrganizationMembershipAnswer : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrganizationMembershipRequestId { get; set; }
        public Guid OrganizationMembershipQuestionId { get; set; }

        /// <summary>HTML-formatted answer text (from Telerik editor).</summary>
        public string? AnswerText { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual OrganizationMembershipRequest MembershipRequest { get; set; } = null!;
        public virtual OrganizationMembershipQuestion Question { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
