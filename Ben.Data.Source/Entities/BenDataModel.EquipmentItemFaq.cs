using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One question-and-answer an owner has written about their own piece of equipment, ahead of
    /// anybody asking.
    /// </summary>
    /// <remarks>
    /// <para>The public artifact of the FAQ/Q&amp;A pair. <see cref="EquipmentQuestion"/> is the
    /// private half — a real thread between two people — and this is the published half, which
    /// carries no author on any projection. Promoting an answered question <b>copies</b> its text
    /// into a new row here rather than publishing the thread, so the private conversation and the
    /// public answer are never the same record and editing one cannot rewrite the other.</para>
    ///
    /// <para>Entries follow the item's own visibility: an FAQ on a piece nobody may see is a piece
    /// of that piece, not a separate thing to protect. The make/model aggregate is narrower still —
    /// it draws only from publicly-listed items, because a per-viewer aggregate would let a reader
    /// infer that somebody in their group owns one.</para>
    /// </remarks>
    public class EquipmentItemFaq : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid EquipmentItemId { get; set; }

        public string Question { get; set; } = string.Empty;
        public string Answer { get; set; } = string.Empty;

        /// <summary>Owner-chosen order. The most-asked thing belongs at the top, not the oldest.</summary>
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual EquipmentItem EquipmentItem { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
