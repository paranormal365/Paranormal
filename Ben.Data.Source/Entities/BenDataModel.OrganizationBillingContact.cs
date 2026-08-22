using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Someone the organization has nominated to receive billing notices.
    /// </summary>
    /// <remarks>
    /// <para><b>Nominated, not inferred.</b> Item 84 is explicit: the owner should be able to say
    /// who receives billing notices rather than the system guessing from roles, because a group's
    /// treasurer is not necessarily an Administrator. Deriving the list from roles would send the
    /// one message that costs money to whoever happened to have the right permission.</para>
    ///
    /// <para>The owner is always notified regardless of this list — a group cannot nominate its way
    /// out of being told its own subscription is ending — so an empty list is a valid state, not a
    /// misconfiguration.</para>
    /// </remarks>
    public class OrganizationBillingContact : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid OrganizationId { get; set; }

        public Guid AppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
