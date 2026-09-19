using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A table in the dining room of a hosted event (item 235 phase 13).
    /// </summary>
    /// <remarks>
    /// <para><b>Dining is a seating assignment, not a third kind of plan</b> (decision 2): tables bound nothing
    /// when people book, and the organizer seats confirmed parties once bookings have settled.</para>
    ///
    /// <para><b>The tables belong to the event, the seating to each sitting.</b> A dining room has the same tables
    /// at dinner and at breakfast; who sits at them is decided meal by meal.</para>
    /// </remarks>
    public class HostedEventDiningTable : IAuditableEntity
    {
        public const int MaxSeats = 40;

        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }

        /// <summary>"Table 4", "The window table".</summary>
        public string Name { get; set; } = string.Empty;

        public int Seats { get; set; } = 8;
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
    }

    /// <summary>
    /// Some or all of a confirmed party, seated at one table for one sitting (item 235 phase 13).
    /// </summary>
    /// <remarks>
    /// A party of ten may sit at two tables, so a party can have several rows in a sitting; together they never
    /// seat more people than the party has, and one table never seats more than it has chairs.
    /// </remarks>
    public class HostedEventDiningSeat : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The sitting: one menu of one night.</summary>
        public Guid HostedEventMenuId { get; set; }

        public Guid HostedEventDiningTableId { get; set; }
        public Guid HostedEventBookingId { get; set; }

        /// <summary>How many of the party sit here.</summary>
        public int People { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEventMenu HostedEventMenu { get; set; } = null!;
        public virtual HostedEventDiningTable HostedEventDiningTable { get; set; } = null!;
        public virtual HostedEventBooking HostedEventBooking { get; set; } = null!;
    }
}
