using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A room or a seat the venue is holding back, for one night or for all of them
    /// (item 235 phase 4).
    /// </summary>
    /// <remarks>
    /// <para><b>A child table rather than a flag on the unit</b>, because "the Suite is held back on
    /// Saturday only" is the common case and a flag cannot say it. A hotel keeps a room for the
    /// owner's family on the Friday and sells it on the Saturday; a theatre blocks the two seats
    /// behind a pillar for the whole run. One shape covers both.</para>
    ///
    /// <para><b>Why it is not just a booking.</b> A blocked room is not a party — nobody is coming,
    /// there is nothing to confirm, nobody to email, and it must not appear on the dietary sheet or
    /// the door's list. Modelling it as a booking with a fake lead would have put a ghost into
    /// every count that walks the bookings, which is most of them.</para>
    ///
    /// <para><b>It is not a permission either.</b> Blocking is the venue saying "not this one";
    /// whether somebody may block is a question for the access rules, and this row only records
    /// that they did.</para>
    /// </remarks>
    public partial class HostedEventUnitBlock : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid HostedEventLayoutUnitId { get; set; }

        /// <summary>The night it applies to, or null for every night of the event.</summary>
        /// <remarks>
        /// Null is a real answer and not a missing one: "this seat is never sold" is the commonest
        /// block there is. Two filtered unique indexes keep the two cases from duplicating each
        /// other — one per (unit, night), and one per unit where the night is null.
        /// </remarks>
        public Guid? HostedEventNightId { get; set; }

        /// <summary>Why it is held back, which changes what a screen may say about it.</summary>
        public HostedEventBlockKind Kind { get; set; } = HostedEventBlockKind.Blocked;

        /// <summary>
        /// The venue's own words. Never shown to a guest.
        /// </summary>
        /// <remarks>
        /// "Mrs Cole's family" is exactly the kind of thing a host writes here and exactly the kind
        /// of thing that must not reach a public plan. The guest's plan says only that the square
        /// is not on offer.
        /// </remarks>
        public string? Note { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEventLayoutUnit HostedEventLayoutUnit { get; set; } = null!;
        public virtual HostedEventNight? HostedEventNight { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
