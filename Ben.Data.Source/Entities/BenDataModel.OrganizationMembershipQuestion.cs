using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A custom question that an organization requires applicants to answer
    /// when submitting a membership request.
    /// </summary>
    public partial class OrganizationMembershipQuestion : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid OrganizationId { get; set; }
        public string QuestionText { get; set; } = null!;
        public bool IsRequired { get; set; } = true;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
