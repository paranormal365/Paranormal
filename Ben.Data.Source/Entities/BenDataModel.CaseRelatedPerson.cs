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

        /// <summary>
        /// Optional photo of this person, uploaded by the client. Nullable because most witnesses
        /// will never have one, and a missing photo must never block recording that someone was
        /// there. Points at an <see cref="UploadFile"/> like every other image in the system, so
        /// it inherits the existing storage and access machinery rather than inventing its own.
        /// </summary>
        public Guid? UploadFileId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual UploadFile? UploadFile { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
