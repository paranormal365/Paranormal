using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A request for more time on a loan that is already out, and what the lender said about it.
    /// </summary>
    /// <remarks>
    /// <para>A child of the loan rather than a state of it. The gear never changes hands during a
    /// renewal, so the loan stays <c>CheckedOut</c> throughout; only its due date moves, and only
    /// if the request is approved.</para>
    ///
    /// <para>Keeping it as its own row is what preserves the conversation: "asked for another week,
    /// was given three days" survives, where simply editing the loan's due date would leave no
    /// trace that anything was asked. Multiple renewals per loan are allowed — one pending at a
    /// time, enforced in the controller.</para>
    ///
    /// <para>Field pairing mirrors <see cref="EquipmentCheckout"/>'s own request/review shape, so
    /// both read the same way.</para>
    /// </remarks>
    public class EquipmentCheckoutRenewal : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid EquipmentCheckoutId { get; set; }

        /// <summary>The new due date being asked for.</summary>
        public DateTime RequestedDateDue { get; set; }

        public EquipmentRenewalStatus Status { get; set; } = EquipmentRenewalStatus.Requested;

        public string? RequestNotes { get; set; }
        public string? ReviewNotes { get; set; }
        public Guid? ReviewedByAppUserId { get; set; }
        public DateTime? DateReviewed { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual EquipmentCheckout EquipmentCheckout { get; set; } = null!;
        public virtual AppUser? ReviewedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
