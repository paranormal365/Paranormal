using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One person's dismissal of one walkthrough tour (item 166 W0).
    /// </summary>
    /// <remarks>
    /// A table row, deliberately not localStorage: an admin impersonating a person must see the
    /// person's real tour state, and someone clearing their browser must not have every tour
    /// replay at them. Row present = the tour never auto-launches again for this person; it can
    /// still be relaunched by hand from its ? affordance. <see cref="Completed"/> records
    /// whether they saw it through or skipped out — both dismiss, but the difference is worth
    /// knowing when a tour's usefulness is ever questioned.
    /// </remarks>
    public class UserTourState : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid AppUserId { get; set; }
        public string TourName { get; set; } = string.Empty;
        public bool Completed { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
