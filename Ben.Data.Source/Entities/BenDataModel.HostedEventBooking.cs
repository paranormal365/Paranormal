using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A room this event is offering, out of the rooms the venue has defined for the place.
    /// </summary>
    /// <remarks>
    /// <para><b>Offering is not owning.</b> <see cref="PlaceRoom"/> is the venue's description of
    /// its own building and outlives any one event; this row says "for these nights, that room is
    /// bookable, and here is what it sleeps this time". A hotel running a séance weekend might
    /// offer six of its twenty rooms and put four beds in a room that normally sleeps two.
    /// </para>
    ///
    /// <para>So the capacity here <b>overrides</b> rather than replaces: null means "whatever the
    /// room says it sleeps", and a number means this event knows better. An event that stated its
    /// own number keeps it when the venue later re-describes the room, which is the behaviour a
    /// host expects of a weekend they have already sold.</para>
    /// </remarks>
    public partial class HostedEventRoom : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }
        public Guid PlaceRoomId { get; set; }

        /// <summary>
        /// How many this room sleeps for this event, or null to use the room's own capacity.
        /// </summary>
        public int? CapacityOverride { get; set; }

        /// <summary>Anything true of this room only for this event — "up two flights, no lift".</summary>
        public string? Note { get; set; }

        /// <summary>Order shown to a guest choosing. Hand-ordered; buildings are not alphabetical.</summary>
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual PlaceRoom PlaceRoom { get; set; } = null!;
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
    /// One night of one booking, in one room.
    /// </summary>
    /// <remarks>
    /// <para>A row per night rather than a range, because a party can move rooms mid-weekend and
    /// because capacity is asked per room per night. A range would make "is the Blue Room free on
    /// Saturday" a question about overlapping intervals instead of a count of rows.</para>
    ///
    /// <para>A day pass has none of these, which is the difference between the two kinds.</para>
    /// </remarks>
    public partial class HostedEventBookingNight
    {
        public Guid Id { get; set; }
        public Guid HostedEventBookingId { get; set; }
        public Guid HostedEventNightId { get; set; }
        public Guid PlaceRoomId { get; set; }

        public DateTime DateCreated { get; set; }

        public virtual HostedEventBooking HostedEventBooking { get; set; } = null!;
        public virtual HostedEventNight HostedEventNight { get; set; } = null!;
        public virtual PlaceRoom PlaceRoom { get; set; } = null!;
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
