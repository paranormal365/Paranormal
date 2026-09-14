using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// How one person wants to hear about one group's bookings (item 235 phase 8).
    /// </summary>
    /// <remarks>
    /// <b>No row means as-it-happens.</b> The default is the loud one on purpose: the failure this
    /// whole phase exists to prevent is a request nobody heard about, and a default that stayed
    /// quiet until somebody found a settings page would recreate it for everybody who never did.
    /// </remarks>
    public partial class EventBookingAlertPreference : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid AppUserId { get; set; }
        public Guid OrganizationId { get; set; }
        public EventBookingAlertMode Mode { get; set; } = EventBookingAlertMode.AsItHappens;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser AppUser { get; set; } = null!;
        public virtual Organization Organization { get; set; } = null!;
    }

    /// <summary>
    /// Where one person's letters about one event have got to (item 235 phase 8).
    /// </summary>
    /// <remarks>
    /// <para><b>A cursor, not a queue.</b> Nothing is written when a booking arrives — the alert
    /// job finds new arrivals by when they were made, and this row remembers how far it has told
    /// this person. One row per person per event, however busy the weekend, where a row per booking
    /// per person would be forty writes for forty requests to say what one timestamp says.</para>
    ///
    /// <para><b>It is also what makes a rush one letter.</b> <see cref="LastAlertUtc"/> is when the
    /// last letter went; arrivals within fifteen minutes of it are the same rush and wait for the
    /// summary instead of each sending their own.</para>
    /// </remarks>
    public partial class EventBookingAlertState : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid AppUserId { get; set; }
        public Guid HostedEventId { get; set; }

        /// <summary>When the last letter about new bookings went to this person for this event.</summary>
        public DateTime? LastAlertUtc { get; set; }

        /// <summary>
        /// Every booking made at or before this has been told to this person.
        /// </summary>
        /// <remarks>
        /// Null the first time, and then only bookings from the last hour count as new — so turning
        /// this feature on, or making somebody a decider, does not post them last month's queue.
        /// </remarks>
        public DateTime? AlertsCoverUpToUtc { get; set; }

        /// <summary>When the last digest went, so it goes once a day or once a week and not every pass.</summary>
        public DateTime? LastDigestUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual AppUser AppUser { get; set; } = null!;
        public virtual HostedEvent HostedEvent { get; set; } = null!;
    }
}
