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

        // ── The pass a guide scans at the meeting point (item 247) ───────────
        //
        // On the attendee rather than in a table of its own, which is where a hosted event keeps
        // its passes. A hosted event's pass belongs to a BOOKING — a party of six with rooms, a
        // status that can be turned down, and a history of reissues somebody may have to explain.
        // A tour seat is one person. The row it would point at carries exactly one pass for its
        // whole life, so a second table would hold one row per row and buy nothing but a join.

        /// <summary>The secret in this guest's pass, or null before one is minted.</summary>
        /// <remarks>
        /// Unguessable by construction rather than by obscurity: it is the only thing standing
        /// between a stranger and somebody else's place on a walk. Reissuing replaces it, which is
        /// also how a pass is taken back — there is never a second live code for one seat.
        /// </remarks>
        public string? PassToken { get; set; }

        /// <summary>When the pass was minted. Null when there is none.</summary>
        public DateTime? PassIssuedUtc { get; set; }

        /// <summary>
        /// When a guide scanned this guest in, and who scanned them.
        /// </summary>
        /// <remarks>
        /// Kept so a second scan can say "already here, at 7.42" rather than waving somebody
        /// through twice — a guide on a dark street needs the answer, not a silent success.
        /// </remarks>
        public DateTime? CheckedInUtc { get; set; }

        public Guid? CheckedInByAppUserId { get; set; }

        public virtual OrgCalendarEvent OrgCalendarEvent { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? SeatDecidedByAppUser { get; set; }
    }
}
