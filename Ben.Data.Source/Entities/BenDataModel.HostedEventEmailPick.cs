using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Places picked on a plan by somebody who is not signed in, waiting for them to prove their
    /// email address (item 235 slice 11d).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-13: <i>"I don't want someone to swamp or spam tickets to an event and it not
    /// be confirmed so, it is blocking others from buying tickets, but also don't want to force
    /// someone to give up information when they don't necessarily have to."</i></para>
    ///
    /// <para><b>Not a booking yet, on purpose.</b> Every booking has a person behind it, and an address
    /// nobody has proved is not a person. So the pick lives here, beside the bookings, for fifteen
    /// minutes: its places read as pending on every plan, and only when the emailed link is clicked
    /// does it become a real hold with the event's own deadline. A mistyped or invented address holds
    /// nothing for longer than that.</para>
    ///
    /// <para><b>No account until the click.</b> Making one at the moment of picking would leave a
    /// passwordless account behind for every address somebody typed, and would put a stranger's hold
    /// on the account of whoever really owns the address.</para>
    ///
    /// <para><b>Deleted a day after it stops mattering</b>, name and phone with it. Somebody who never
    /// clicked gave those to an organizer who never received them.</para>
    /// </remarks>
    public class HostedEventEmailPick : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }

        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;

        /// <summary>Lower-cased, so one address is one person whatever they typed.</summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>Required, not verified: the site cannot send texts.</summary>
        public string Phone { get; set; } = string.Empty;

        public int PartySize { get; set; } = 1;
        public string? Note { get; set; }

        /// <summary>SHA-256 of the emailed token, hex. The token itself is never stored.</summary>
        public string TokenHash { get; set; } = string.Empty;

        /// <summary>When the places go back if the link has not been clicked.</summary>
        public DateTime ExpiresUtc { get; set; }

        /// <summary>
        /// Whether its places still count. Cleared when it lapses, is replaced, is let go or becomes
        /// a booking.
        /// </summary>
        /// <remarks>
        /// A flag rather than a comparison with the clock, because the database's arbiter index can
        /// filter on a column and cannot filter on "now".
        /// </remarks>
        public bool IsLive { get; set; } = true;

        /// <summary>When the link was clicked and the pick became a booking.</summary>
        public DateTime? ConfirmedUtc { get; set; }

        /// <summary>The hold it became, which the link goes on showing.</summary>
        public Guid? HostedEventBookingId { get; set; }

        /// <summary>Why it could not become a hold when the link was clicked, in words.</summary>
        public string? RefusedSentence { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }

        /// <summary>Always empty: nobody is signed in. Kept for the audit shape.</summary>
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual HostedEventBooking? HostedEventBooking { get; set; }
        public virtual ICollection<HostedEventEmailPickPlace> Places { get; set; } = [];
    }

    /// <summary>One square of a pick on one night.</summary>
    public class HostedEventEmailPickPlace
    {
        public Guid Id { get; set; }
        public Guid HostedEventEmailPickId { get; set; }
        public Guid HostedEventNightId { get; set; }

        /// <summary>The room or seat, or null for a place that night with no unit.</summary>
        public Guid? HostedEventLayoutUnitId { get; set; }

        public int? People { get; set; }

        /// <summary>The parent's <see cref="HostedEventEmailPick.IsLive"/>, copied for the index.</summary>
        public bool IsLive { get; set; } = true;

        public virtual HostedEventEmailPick HostedEventEmailPick { get; set; } = null!;
        public virtual HostedEventNight HostedEventNight { get; set; } = null!;
        public virtual HostedEventLayoutUnit? HostedEventLayoutUnit { get; set; }
    }
}
