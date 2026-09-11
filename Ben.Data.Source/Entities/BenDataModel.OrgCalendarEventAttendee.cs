using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>An attendee invited to an org calendar event, with RSVP status and optional task assignment.</summary>
    public class OrgCalendarEventAttendee
    {
        public Guid Id { get; set; }
        public Guid OrgCalendarEventId { get; set; }
        public Guid AppUserId { get; set; }
        public RsvpStatus RsvpStatus { get; set; } = RsvpStatus.Invited;
        public string? AssignedTask { get; set; }
        public DateTime? DateRsvp { get; set; }
        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        // ── A seat on a tour date (item 234, Ben 2026-09-10) ─────────────────

        /// <summary>
        /// Where this sign-up has got to, when the date belongs to a tour. Null otherwise.
        /// </summary>
        /// <remarks>
        /// Null is every attendee row that already exists and every event that is not a tour date,
        /// and it means the rule this site had before: signing up is coming. See
        /// <see cref="TourSeatStatus"/> for why this is a second field rather than more values on
        /// <see cref="RsvpStatus"/>.
        /// </remarks>
        public TourSeatStatus? SeatStatus { get; set; }

        /// <summary>
        /// How many places this sign-up holds. One unless somebody asked for more.
        /// </summary>
        /// <remarks>
        /// Ben: <i>"the seat or seats have been reserved for the tour."</i> A date's capacity counts
        /// PLACES from here on, not rows — and because every row that already exists holds one, the
        /// sum equals the old count and nothing about an ordinary event moves.
        /// </remarks>
        public int Seats { get; set; } = 1;

        /// <summary>When the business approved or turned it down. Null while it is still waiting.</summary>
        public DateTime? SeatDecidedUtc { get; set; }

        /// <summary>Who decided. Kept because "the business approved it" is a person, named.</summary>
        public Guid? SeatDecidedByAppUserId { get; set; }

        /// <summary>
        /// When the guest said back that they know the seat is theirs. Optional, always.
        /// </summary>
        /// <remarks>
        /// Ben: <i>"the person who is touring can confirm it on the app - if they want."</i> Nothing
        /// depends on it and no reminder is withheld for want of it — it is one of them telling the
        /// other they saw it.
        /// </remarks>
        public DateTime? GuestAcknowledgedUtc { get; set; }

        public virtual OrgCalendarEvent OrgCalendarEvent { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? SeatDecidedByAppUser { get; set; }
    }
}
