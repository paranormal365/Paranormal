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
    }
}
