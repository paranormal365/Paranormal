using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A person the client has referenced who is not a platform user — e.g. a family member
    /// or neighbor who has had experiences at the property. No account is created; this is
    /// basic info only, for reference in notes/timeline entries. Never returned by any
    /// public-facing endpoint, so it is implicitly scrubbed when a case is made public.
    /// </summary>
    public class CaseRelatedPerson : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }

        public string Name { get; set; } = null!;
        public int? Age { get; set; }
        public string? Relationship { get; set; }
        public bool LivesAtProperty { get; set; }
        public string? Notes { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
