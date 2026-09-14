using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A letter the host sent to the people coming to an event (item 235 phase 17a, audit finding A4).
    /// </summary>
    /// <remarks>
    /// <para><b>Why it exists.</b> "Parking has moved to the back", "doors open at eight, not seven", "bring a torch":
    /// every hosting product lets the host tell everybody at once. Before this the room reached only the people who
    /// opened it, and a change to the programme mailed only that session's sign-ups.</para>
    ///
    /// <para><b>Kept, not just sent.</b> The row records what was said, to whom and how many it reached, so a host can
    /// see what went out last week and a guest who says "nobody told us" can be answered.</para>
    /// </remarks>
    public class HostedEventAnnouncement : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid HostedEventId { get; set; }

        public string Subject { get; set; } = null!;

        /// <summary>Plain text; the letter escapes it and keeps its line breaks.</summary>
        public string Body { get; set; } = null!;

        /// <summary>Only the people there on this date. Null is everybody coming to any of them.</summary>
        public Guid? HostedEventNightId { get; set; }

        /// <summary>Also the people still waiting for an answer, not only the confirmed.</summary>
        public bool IncludeUnconfirmed { get; set; }

        /// <summary>How many leads it went to.</summary>
        public int Recipients { get; set; }

        /// <summary>How many letters actually left (zero on a site with no outgoing mail — the bell still rang).</summary>
        public int Emailed { get; set; }

        public Guid SentByAppUserId { get; set; }
        public DateTime SentUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual HostedEventNight? HostedEventNight { get; set; }
        public virtual AppUser SentByAppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
