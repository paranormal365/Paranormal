using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A single piece of gear — either owned by a person (<see cref="OwnerAppUserId"/>) or by an
    /// organization (<see cref="OwningOrganizationId"/>). Exactly one of the two is set; the
    /// database has no check-constraint enforcing this (the InMemory test provider ignores check
    /// constraints, same reasoning as <c>Investigation.CaseId</c>/<c>PlaceId</c>), so every
    /// controller path that creates or moves ownership validates the XOR itself.
    /// </summary>
    /// <remarks>
    /// One table for both flavors rather than two: the checkout lifecycle, condition photos, and
    /// history are identical either way — only who approves a loan differs, and that's a pure
    /// function of which ownership column is set (see <c>EquipmentAccess</c>). The
    /// <see cref="CurrentHolderAppUserId"/>/<see cref="LastServicedDate"/>/<see cref="DefectNotes"/>
    /// fields are only ever populated for org-owned rows; they ship now, nullable, so Phase 3
    /// (org-owned equipment) needs no reshape of this table.
    /// </remarks>
    public class EquipmentItem : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid? OwnerAppUserId { get; set; }
        public Guid? OwningOrganizationId { get; set; }

        public Guid EquipmentModelId { get; set; }

        public string DisplayName { get; set; } = null!;

        /// <summary>
        /// Private to the owner (and, for org-owned items, holders of the org's Equipment
        /// permission) — never returned to anyone else, including a borrower reviewing gear
        /// shared with their group. See <c>EquipmentAccess</c> for the exact resolution.
        /// </summary>
        public string? SerialNumber { get; set; }

        public DateTime? AcquisitionDate { get; set; }
        public string? Notes { get; set; }

        /// <summary>
        /// True once an item has checkout history and can no longer be hard-deleted — it can only
        /// be retired (hidden from browsing/borrowing, history preserved).
        /// </summary>
        public bool IsRetired { get; set; }

        /// <summary>
        /// The owner has chosen to list this piece publicly. Anonymous visitors then see the item,
        /// its make/model and its photos — but never <see cref="OwnerAppUserId"/>'s name and never
        /// <see cref="SerialNumber"/>. Off by default: publishing someone's property is a decision
        /// they make, not one they discover.
        /// </summary>
        public bool IncludeInGlobalCatalog { get; set; }

        /// <summary>
        /// Who the owner is willing to lend this piece to — the group itself, fellow group members
        /// personally, anyone signed in, or any combination. Independent of who can <i>see</i> it;
        /// see <see cref="EquipmentLoanAudience"/> for why the routes differ by attribution as well
        /// as reach. Defaults to not loanable.
        /// </summary>
        public EquipmentLoanAudience LoanAudience { get; set; }

        /// <summary>
        /// A page worth reading about this piece — the manufacturer's, a review, wherever the owner
        /// found it. Collected per item and shown as a distinct set on the model page, so several
        /// owners' links become one useful list for that make and model.
        /// </summary>
        public string? WebsiteUrl { get; set; }

        /// <summary>How many times this piece's own page has been opened. Lifetime total.</summary>
        /// <remarks>
        /// Visible only to org Administrators and SuperAdmin. A plain counter rather than per-viewer
        /// rows: the question is "is anyone interested in this", and recording <i>who</i> looked
        /// would turn a vanity number into a log of who browsed whose equipment.
        /// </remarks>
        public int ViewCount { get; set; }

        /// <summary>How many times <see cref="WebsiteUrl"/> has been followed. Lifetime total.</summary>
        public int LinkClickCount { get; set; }

        // ── Org-owned-only fields (null on personal items) ──────────────────────────
        public Guid? CurrentHolderAppUserId { get; set; }
        public DateTime? LastServicedDate { get; set; }
        public string? DefectNotes { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser? OwnerAppUser { get; set; }
        public virtual Organization? OwningOrganization { get; set; }
        public virtual EquipmentModel EquipmentModel { get; set; } = null!;
        public virtual AppUser? CurrentHolderAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<EquipmentItemPhoto> Photos { get; set; } = new List<EquipmentItemPhoto>();
        public virtual ICollection<EquipmentItemShare> Shares { get; set; } = new List<EquipmentItemShare>();
        public virtual ICollection<EquipmentServiceLog> ServiceLog { get; set; } = new List<EquipmentServiceLog>();
        public virtual ICollection<EquipmentItemFaq> Faqs { get; set; } = new List<EquipmentItemFaq>();
        public virtual ICollection<EquipmentQuestion> Questions { get; set; } = new List<EquipmentQuestion>();
    }
}
