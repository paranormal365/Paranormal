using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// What one side of a finished loan had to say about the other, and — from the borrower — about
    /// the gear itself.
    /// </summary>
    /// <remarks>
    /// <para><b>The subject never sees it.</b> That is the whole design constraint, and it is why
    /// there are no notifications on this table at all: telling somebody feedback about them exists
    /// is most of the way to showing it to them. Both read endpoints structurally exclude the
    /// subject, and each has a test that fails when its exclusion is removed.</para>
    ///
    /// <para>Ben's reason for the lender-facing half: <i>"so we know they are trustworthy and
    /// respectful with equipment"</i>. Somebody deciding whether to hand over a £400 recorder should
    /// be able to see that the last three lenders got it back on time and clean.</para>
    ///
    /// <para><see cref="Rating"/> is optional beside the free text — a lender who wants to say
    /// nothing numeric should not be forced to. The approver's panel shows an average only with its
    /// count beside it, and not at all below three ratings, so one sour opinion reads as one voice
    /// rather than a verdict.</para>
    ///
    /// <para>The subject is denormalized onto the row (<see cref="SubjectAppUserId"/>,
    /// <see cref="SubjectOrganizationId"/>) because every read asks "everything about this person"
    /// across many loans — walking back through the checkout each time would make the approver's
    /// panel a join per row.</para>
    /// </remarks>
    public class EquipmentLoanFeedback : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid EquipmentCheckoutId { get; set; }

        public Guid AuthorAppUserId { get; set; }

        public EquipmentFeedbackRole Role { get; set; }

        /// <summary>About the other party. Optional — a rating alone is a fair thing to leave.</summary>
        public string? CounterpartyComment { get; set; }

        /// <summary>1–5, or null. Never averaged below three ratings; always shown with its count.</summary>
        public int? Rating { get; set; }

        /// <summary>
        /// About the equipment rather than the person. Borrower-only, and the one part of this table
        /// that is ever public: it feeds the make/model page's reviews, stripped of its author.
        /// </summary>
        public string? ProductComment { get; set; }

        /// <summary>Who this is about, when the other side was a person.</summary>
        public Guid? SubjectAppUserId { get; set; }

        /// <summary>Who this is about, when the gear belonged to a group rather than a member.</summary>
        public Guid? SubjectOrganizationId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual EquipmentCheckout EquipmentCheckout { get; set; } = null!;
        public virtual AppUser AuthorAppUser { get; set; } = null!;
        public virtual AppUser? SubjectAppUser { get; set; }
        public virtual Organization? SubjectOrganization { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
