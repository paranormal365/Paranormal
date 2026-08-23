using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>A member who is a point of contact for one case (item 158).</summary>
    /// <remarks>
    /// Ben's rule: every case has at least one contact besides the case manager. Modelled as
    /// zero-or-more explicit rows with a fallback — when a case has none, the case manager IS
    /// the contact — so the client-facing "who do I talk to" surface can never render empty.
    /// Shown to the client by display name; routing client-message notifications to contacts is
    /// what makes the designation real rather than decorative.
    /// </remarks>
    public partial class CaseContact : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid CaseId { get; set; }
        public Guid AppUserId { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Case Case { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
