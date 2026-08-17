using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One entry in a piece of equipment's service and defect history — serviced, broken, fixed.
    /// </summary>
    /// <remarks>
    /// <para>Append-only in spirit: entries are the record of what happened to a piece of gear, and
    /// <see cref="EquipmentItem.LastServicedDate"/> and <see cref="EquipmentItem.DefectNotes"/> are
    /// a cache of the latest entry's effect, written in the same save. That keeps "is this thing
    /// broken right now?" a column read while the log keeps the account.</para>
    ///
    /// <para><see cref="EntryDate"/> is separate from <c>DateCreated</c> on purpose: gear is
    /// routinely serviced on one day and logged on another, and back-dating an entry should not
    /// mean lying about when it was typed.</para>
    /// </remarks>
    public class EquipmentServiceLog : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid EquipmentItemId { get; set; }

        public EquipmentServiceLogType EntryType { get; set; }

        /// <summary>When the work happened — not when the row was written. See remarks.</summary>
        public DateTime EntryDate { get; set; }

        public string Notes { get; set; } = string.Empty;

        /// <summary>Who did the work, when that is someone other than whoever logged it.</summary>
        public Guid? PerformedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual EquipmentItem EquipmentItem { get; set; } = null!;
        public virtual AppUser? PerformedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
