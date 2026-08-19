using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One loan of one piece of equipment: who asked, who decided, who has it, and when it is due
    /// back.
    /// </summary>
    /// <remarks>
    /// <para>Covers both group-owned gear and members' personal gear, because the lifecycle is
    /// identical and only the approver differs — see <see cref="EquipmentCheckoutStatus"/>. The
    /// request/review field pairing (<see cref="RequestNotes"/> / <see cref="ReviewNotes"/> /
    /// <see cref="ReviewedByAppUserId"/> / <see cref="DateReviewed"/>) deliberately mirrors
    /// <c>UploadFilePermissionRequest</c>, which is the same shape of ask-and-decide.</para>
    ///
    /// <para><see cref="BorrowedForOrganizationId"/> is <b>nullable</b>, and that is the point of
    /// the loan-audience model: a loan taken out for a group records which group, while a personal
    /// loan — someone borrowing as themselves, whether a fellow group member or anyone with an
    /// account — has no borrowing group to record. Requiring one would have forced personal
    /// borrowers to pretend to represent a group.</para>
    ///
    /// <para>Both confirmations are attributed rather than assumed. The borrower confirms the
    /// hand-off and the lender confirms the return: each party attests to the transfer coming
    /// toward them, so neither side can close a loan on the other's behalf.</para>
    /// </remarks>
    public class EquipmentCheckout : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid EquipmentItemId { get; set; }
        public Guid BorrowerAppUserId { get; set; }

        /// <summary>
        /// The group this was borrowed for, when it was borrowed for one at all. Null on a personal
        /// loan. Forced to the owning group for group-owned gear.
        /// </summary>
        public Guid? BorrowedForOrganizationId { get; set; }

        /// <summary>The visit this gear was taken out for, if it was tied to one.</summary>
        public Guid? InvestigationId { get; set; }

        public EquipmentCheckoutStatus Status { get; set; } = EquipmentCheckoutStatus.Requested;

        public string? RequestNotes { get; set; }
        public string? ReviewNotes { get; set; }
        public Guid? ReviewedByAppUserId { get; set; }
        public DateTime? DateReviewed { get; set; }

        /// <summary>When the borrower says they need it from — a request, not a reservation.</summary>
        public DateTime? DateNeededFrom { get; set; }

        /// <summary>When it should come back. Null means the lender did not set a deadline.</summary>
        public DateTime? DateDue { get; set; }

        public DateTime? DateCheckedOut { get; set; }
        public Guid? CheckedOutConfirmedByAppUserId { get; set; }

        public DateTime? DateReturned { get; set; }
        public Guid? ReturnedReceivedByAppUserId { get; set; }
        public string? ReturnConditionNotes { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual EquipmentItem EquipmentItem { get; set; } = null!;
        public virtual AppUser BorrowerAppUser { get; set; } = null!;
        public virtual Organization? BorrowedForOrganization { get; set; }
        public virtual Investigation? Investigation { get; set; }
        public virtual AppUser? ReviewedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<EquipmentCheckoutPhoto> Photos { get; set; } = new List<EquipmentCheckoutPhoto>();
        public virtual ICollection<EquipmentCheckoutRenewal> Renewals { get; set; } = new List<EquipmentCheckoutRenewal>();
        public virtual ICollection<EquipmentLoanFeedback> Feedback { get; set; } = new List<EquipmentLoanFeedback>();
    }
}
