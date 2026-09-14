using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One thing on an event's programme: a class, a talk, a séance, a walk (item 235 phase 10).
    /// </summary>
    /// <remarks>
    /// <para><b>"The ghost hunt but not the dinner."</b> A night is atomic for booking — you are
    /// staying or you are not — and a session is how a guest opts in to one part of it. Some sessions
    /// need a place (the Ovilus class holds fifteen); some are for whoever turns up (a talk in the
    /// ballroom), which is <see cref="RequiresSignUp"/> false.</para>
    ///
    /// <para><b>Absolute times.</b> Stored in UTC and shown on the venue's clock, because a guest
    /// flying in from another time zone needs "9 PM at the hotel", not 9 PM wherever they are.</para>
    ///
    /// <para><b>Places taken is a stored counter with a concurrency check</b>
    /// (<see cref="PlacesTaken"/>). Two guests pressing Sign up on the last place in the same second
    /// both read "14 of 15"; the counter makes the second save fail and retry, and the retry sees 15.
    /// Counting rows instead would let both in.</para>
    /// </remarks>
    public class HostedEventSession : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }

        public string Title { get; set; } = null!;
        public string? Description { get; set; }

        public DateTime StartsAtUtc { get; set; }
        public DateTime EndsAtUtc { get; set; }

        /// <summary>One of the venue's rooms, when it happens in one.</summary>
        public Guid? PlaceRoomId { get; set; }

        /// <summary>Where, in words, when it is not a room: "the garden", "meet at reception".</summary>
        public string? LocationText { get; set; }

        /// <summary>Who leads it, as the programme should print it.</summary>
        public string? LedBy { get; set; }

        /// <summary>How many it holds. Null is no limit.</summary>
        public int? Capacity { get; set; }

        /// <summary>Whether a guest has to sign up. False is "just come".</summary>
        public bool RequiresSignUp { get; set; }

        /// <summary>People holding a place, kept in step with the sign-ups; the concurrency token.</summary>
        public int PlacesTaken { get; set; }

        public int SortOrder { get; set; }

        public DateTime? CalledOffUtc { get; set; }
        public string? CancelledReason { get; set; }

        /// <summary>
        /// When its time or place last changed after the programme was published. Drives the bell's
        /// "the programme changed" row, and the letter to everybody signed up.
        /// </summary>
        public DateTime? ChangedUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual PlaceRoom? PlaceRoom { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<HostedEventSessionSignUp> SignUps { get; set; } = [];
    }

    /// <summary>
    /// A guest's place in a session, or their place in the queue for one (item 235 phase 10).
    /// </summary>
    /// <remarks>
    /// <b>A queue, not a refusal.</b> A full class is a class somebody might drop out of, and the
    /// guest who asked first should get that place without having to keep checking.
    /// <see cref="WaitlistedUtc"/> set means waiting; cleared means in, and
    /// <see cref="PromotedUtc"/> says when a waiting guest was moved up.
    /// </remarks>
    public class HostedEventSessionSignUp : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventSessionId { get; set; }
        public Guid AppUserId { get; set; }

        /// <summary>The booking that entitles them, for a guest. Null for somebody helping at the event.</summary>
        public Guid? HostedEventBookingId { get; set; }

        /// <summary>How many of their party this is for.</summary>
        public int People { get; set; } = 1;

        public DateTime? WaitlistedUtc { get; set; }
        public DateTime? PromotedUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEventSession HostedEventSession { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
        public virtual HostedEventBooking? HostedEventBooking { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
