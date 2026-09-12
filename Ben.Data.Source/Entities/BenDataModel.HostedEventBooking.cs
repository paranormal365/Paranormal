using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One thing this event allocates to a party: a room to sleep in, or a seat to sit in.
    /// </summary>
    /// <remarks>
    /// <para><b>One model for both kinds</b> (item 235 phase 2.4). A room and a seat are the same
    /// shape underneath — each is chosen by a guest while booking, and each bounds how many people
    /// can come at all — so each is capacity-checked at the moment the venue confirms. What the
    /// event is allocating is said once, on <see cref="HostedEvent.LayoutKind"/>, rather than
    /// inferred from which fields happen to be filled in.</para>
    ///
    /// <para><b>Offering is not owning.</b> On a Rooms layout this row points at a
    /// <see cref="PlaceRoom"/> — the venue's own description of its building, which outlives any
    /// one event and carries the history that is half the reason anybody is booking. This row says
    /// "for these nights, that room is bookable, and here is what it sleeps and costs THIS time".
    /// A hotel running a seance weekend might offer six of its twenty rooms and put four beds in a
    /// room that normally sleeps two.</para>
    ///
    /// <para><b>A seat has no such backing row and carries its own <see cref="Label"/>.</b>
    /// Describing four hundred theatre seats as four hundred rooms of the venue would be a naming
    /// lie that everything downstream then has to read past, and a seat has no history, no beds
    /// and nothing to attribute a reading to.</para>
    /// </remarks>
    public partial class HostedEventLayoutUnit : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }

        /// <summary>
        /// The venue's own room this unit stands for, on a Rooms layout. Null on a Seats layout.
        /// </summary>
        public Guid? PlaceRoomId { get; set; }

        /// <summary>
        /// What to call it — "H9", "Row C seat 4". Null on a Rooms unit, which uses the room's own
        /// name so that renaming the room renames it everywhere at once.
        /// </summary>
        public string? Label { get; set; }

        /// <summary>
        /// The part of the building it is in — "Main Floor", "Balcony", "Second floor".
        /// </summary>
        /// <remarks>
        /// Free text, and separate from the plan's grid. A section is how a guest is told where
        /// they will be and how a price is explained ("Balcony, $18"); the grid is only where the
        /// square is drawn. A venue that never fills this in gets one unnamed section, which is the
        /// right answer for a small room with twelve chairs in it.
        /// </remarks>
        public string? Section { get; set; }

        /// <summary>
        /// How many this holds for this event, or null to fall back to the room's own capacity.
        /// </summary>
        /// <remarks>
        /// <para>Overrides rather than replaces: an event that stated its own number keeps it when
        /// the venue later re-describes the room, which is what a host expects of a weekend they
        /// have already sold.</para>
        ///
        /// <para><b>A seat is always one.</b> The designer writes 1 and will not let it be
        /// anything else — a seat that holds three is not a seat, and one existing would quietly
        /// break every count that trusts the number.</para>
        /// </remarks>
        public int? Capacity { get; set; }

        /// <summary>Anything true of it only for this event — "up two flights, no lift".</summary>
        public string? Note { get; set; }

        /// <summary>
        /// What it costs, shown to guests and <b>never charged</b>.
        /// </summary>
        /// <remarks>
        /// <para>Ben, 2026-09-12: <i>"We can tell them the price, but we do not collect money."</i>
        /// The site takes nothing (DECISION 1) — the venue and the guest settle between
        /// themselves, exactly as a walk's seat works. This exists so somebody choosing between the
        /// Blue Room and the Suite chooses with the price in front of them instead of writing to
        /// ask.</para>
        ///
        /// <para><b>Null is "ask the venue", not "free".</b> A venue that has not filled this in
        /// must not have a zero printed against its rooms, and something genuinely included in the
        /// price is said with a zero on purpose.</para>
        ///
        /// <para>Per NIGHT. On a one-night event that is simply its price; on a weekend it is what
        /// a party staying two of the three nights is actually quoted.</para>
        /// </remarks>
        public decimal? Price { get; set; }

        /// <summary>
        /// Which square of the plan it is drawn in, or null when nobody has placed it.
        /// </summary>
        /// <remarks>
        /// <para><b>A grid, not a canvas.</b> A hotel floor is a corridor with rooms either side
        /// and a theatre is rows of seats; a row-and-column position says both in two integers —
        /// no coordinates, no scale, no drawing. A freeform canvas would ask a venue to be an
        /// architect and would still not tell anybody anything a tidy grid does not.</para>
        ///
        /// <para><b>Null is not zero.</b> An unplaced unit sits in the designer's tray rather than
        /// landing silently in the top-left corner on top of whatever is already there — and an
        /// event that never opens the designer keeps a plain list, which is the right default for
        /// a cellar and a corridor.</para>
        /// </remarks>
        public int? LayoutRow { get; set; }

        /// <inheritdoc cref="LayoutRow"/>
        public int? LayoutColumn { get; set; }

        /// <summary>Order shown to a guest choosing. Hand-ordered; buildings are not alphabetical.</summary>
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual PlaceRoom? PlaceRoom { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }

    /// <summary>
    /// One party's place at an event: who leads it, how many they are, and what the venue said.
    /// </summary>
    /// <remarks>
    /// <para><b>A booking is a party, not a person.</b> Four people arriving together are one
    /// booking with a party size of four, one lead who corresponds with the venue, and up to four
    /// named guests carrying their own dietary notes. Modelling them as four sign-ups would ask
    /// the venue to work out which four belong in one room, and would let three of them cancel
    /// while the fourth kept a room built for four.</para>
    ///
    /// <para><b>Confirmation writes the umbrella attendee row</b>
    /// (<see cref="UmbrellaAttendeeId"/>), with <c>Seats</c> equal to the party size. That is what
    /// makes every count that already exists — the public list, the 24-hour reminder, the phone's
    /// "mine", evidence submission — go on meaning <i>has a place</i>, without one of them learning
    /// what a hosted event is. It is nulled again if the booking is later cancelled.</para>
    ///
    /// <para><b>Editable after confirmation</b>, at Ben's request: party size, the room-nights and
    /// the guests can all change, because a real weekend has somebody dropping out on the Thursday.
    /// Each edit re-checks capacity, so an edit cannot do what a booking could not.</para>
    /// </remarks>
    public partial class HostedEventBooking : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }

        /// <summary>The person the venue corresponds with, and who may edit it.</summary>
        public Guid LeadAppUserId { get; set; }

        /// <summary>How many people, the lead included. What the umbrella row's <c>Seats</c> becomes.</summary>
        public int PartySize { get; set; } = 1;

        public HostedEventBookingKind Kind { get; set; }
        public HostedEventBookingStatus Status { get; set; } = HostedEventBookingStatus.Requested;

        /// <summary>When the venue decided, and who decided. Null while it is still a request.</summary>
        public DateTime? DecidedUtc { get; set; }
        public Guid? DecidedByAppUserId { get; set; }

        /// <summary>
        /// Why it was turned down or cancelled, in the venue's own words.
        /// </summary>
        /// <remarks>
        /// Shown to the guest. A refusal with no reason reads as arbitrary, and the commonest
        /// reason — the room they asked for is gone, another is free — is one the guest can act on.
        /// </remarks>
        public string? DecisionNote { get; set; }

        /// <summary>When the guest confirmed they had read the decision. Clears the bell.</summary>
        public DateTime? GuestAcknowledgedUtc { get; set; }

        /// <summary>What the guest said when asking — arrival time, a request, an explanation.</summary>
        public string? Note { get; set; }

        /// <summary>
        /// When the guest asked to get out of a booking the venue had already confirmed.
        /// </summary>
        /// <remarks>
        /// A request holds nothing, so withdrawing one simply deletes it. A confirmed booking is
        /// different: the venue has catered, staffed and possibly turned somebody else away
        /// against it, so the guest ASKS and the host releases it. A room freed without the host
        /// knowing is a room that stays empty.
        /// </remarks>
        public DateTime? CancellationRequestedUtc { get; set; }

        /// <summary>Why they cannot come, in their own words. Shown to the venue.</summary>
        public string? CancellationReason { get; set; }

        /// <summary>
        /// The umbrella calendar attendee this booking wrote when it was confirmed.
        /// </summary>
        /// <remarks>
        /// Nullable because a request has not written one and a cancellation removes it. Held by id
        /// rather than looked up, so releasing it is exact rather than a query that might match a
        /// row somebody added by another door.
        /// </remarks>
        public Guid? UmbrellaAttendeeId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual AppUser LeadAppUser { get; set; } = null!;
        public virtual AppUser? DecidedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }

        public virtual ICollection<HostedEventBookingNight> Nights { get; set; } = [];
        public virtual ICollection<HostedEventBookingGuest> Guests { get; set; } = [];

        /// <summary>
        /// Every pass ever issued against this booking, revoked ones included.
        /// </summary>
        /// <remarks>
        /// The revoked ones are kept on purpose. A door shown an old code must be told it was
        /// replaced, which is a different sentence from being told it was never real.
        /// </remarks>
        public virtual ICollection<HostedEventPass> Passes { get; set; } = [];
    }

    /// <summary>
    /// One night of one booking: which night they are here, and what they hold that night.
    /// </summary>
    /// <remarks>
    /// <para>A row per night rather than a range, because a party can move mid-weekend and because
    /// capacity is asked per unit per night. A range would make "is the Blue Room free on Saturday"
    /// a question about overlapping intervals instead of a count of rows.</para>
    ///
    /// <para><b>This row means "we are here that night", and holding a room is a separate fact
    /// about it</b> (Ben, 2026-09-12: <i>"maybe a three day event they want to be there only two
    /// days"</i>). A party staying Friday and Saturday of a three-night weekend has two rows and is
    /// simply absent on the Sunday. A day pass for Saturday only has one row with no unit — which
    /// is what lets a three-day event sell Saturday without selling Friday, and what lets
    /// Saturday's cook know how many people are actually in the building that day.</para>
    ///
    /// <para>A day pass with NO rows at all is the older, simpler thing: here for the event,
    /// whichever days that turns out to mean. Both are real and a venue may want either.</para>
    /// </remarks>
    public partial class HostedEventBookingNight
    {
        public Guid Id { get; set; }
        public Guid HostedEventBookingId { get; set; }
        public Guid HostedEventNightId { get; set; }

        /// <summary>
        /// The room or seat they hold that night, or null when they are here without one.
        /// </summary>
        /// <remarks>
        /// <para>The event's own unit rather than the venue's room, so that a seat — which has no
        /// room behind it — is held in exactly the same way, and so that what a party was given
        /// survives the venue later re-describing its building.</para>
        ///
        /// <para><b>Null is a real and useful state:</b> somebody coming for Saturday and going
        /// home again. Making it required would force a fake room onto every day guest and would
        /// leave "which days is this day pass for" unanswerable.</para>
        /// </remarks>
        public Guid? HostedEventLayoutUnitId { get; set; }

        public DateTime DateCreated { get; set; }

        public virtual HostedEventBooking HostedEventBooking { get; set; } = null!;
        public virtual HostedEventNight HostedEventNight { get; set; } = null!;
        public virtual HostedEventLayoutUnit? HostedEventLayoutUnit { get; set; }
    }

    /// <summary>
    /// Somebody in the party, named so the kitchen and the door know who is coming.
    /// </summary>
    /// <remarks>
    /// <para><b>An account is optional.</b> A guest brings their partner, who has never heard of
    /// this site and never will. So the row carries a display name, and an <c>AppUserId</c> only
    /// when the person happens to have an account — which is what lets a member's own bookings
    /// appear under their profile without forcing everybody else to register to eat dinner.</para>
    ///
    /// <para><b>Dietary notes are free text and they are personal data.</b> "Coeliac" and "nut
    /// allergy" are health information about a named individual, so they are visible to the venue's
    /// staff with booking access and to door staff for their own night (DECISION 10), and to
    /// nobody else. They never reach a public page.</para>
    /// </remarks>
    public partial class HostedEventBookingGuest
    {
        public Guid Id { get; set; }
        public Guid HostedEventBookingId { get; set; }

        /// <summary>What to call them. The only required thing about a guest.</summary>
        public string DisplayName { get; set; } = null!;

        /// <summary>Set only when this guest has an account here.</summary>
        public Guid? AppUserId { get; set; }

        /// <summary>Allergies, intolerances and what they will not eat, in their own words.</summary>
        public string? DietaryNotes { get; set; }

        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        public virtual HostedEventBooking HostedEventBooking { get; set; } = null!;
        public virtual AppUser? AppUser { get; set; }
    }
}
