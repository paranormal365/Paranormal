using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>An internal org-side note on a case. Never visible to clients.</summary>
    public class CaseNote : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }
        public Guid AuthorAppUserId { get; set; }

        public string? Title { get; set; }
        public string Body { get; set; } = null!;
        public bool IsPinned { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual AppUser AuthorAppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
