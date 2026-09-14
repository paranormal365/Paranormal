using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A party arriving on one night of an event (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// <para><b>Per night, which is the whole point.</b> Arrival used to be one stamp on the PASS:
    /// a three-night weekend could record that a party turned up, once, and never which nights.
    /// The door on Saturday could not tell whether the people in front of it had already been in
    /// on Friday, and nobody could answer "who was actually here on the Saturday" afterwards —
    /// which is the question a venue asks when something goes missing (defect 19).</para>
    ///
    /// <para><b>Arrived and left are separate</b>, because a lock-in has people going out to the
    /// car and coming back, and a door that could only say "seen" would lose the count the moment
    /// anybody stepped outside. Left is null for almost everybody: most people arrive and stay.
    /// </para>
    ///
    /// <para><b>It records how many actually came</b>, which is not always the party size. Four
    /// booked and three turned up is ordinary, and the number the kitchen and the fire officer
    /// need is the one at the door rather than the one in the booking.</para>
    ///
    /// <para><b>And how it was recorded</b>, because a scan and a name typed into a search box are
    /// different levels of certainty about who came in, and the difference matters when somebody
    /// disputes it afterwards.</para>
    /// </remarks>
    public partial class HostedEventCheckIn : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid HostedEventBookingId { get; set; }

        /// <summary>Which night they came in on.</summary>
        public Guid HostedEventNightId { get; set; }

        public DateTime ArrivedUtc { get; set; }

        /// <summary>When they left, if anybody marked it. Usually nobody does.</summary>
        public DateTime? LeftUtc { get; set; }

        /// <summary>
        /// How many actually walked in, when it is not the whole party.
        /// </summary>
        /// <remarks>
        /// Null means "all of them", which is the common case and avoids a number that has to be
        /// kept in step with the booking's own party size when a host edits it.
        /// </remarks>
        public int? People { get; set; }

        /// <summary>How the door knew who this was.</summary>
        public HostedEventCheckInMethod Method { get; set; } = HostedEventCheckInMethod.Scanned;

        /// <summary>Who was on the door. Kept because a disputed arrival is a conversation.</summary>
        public Guid RecordedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEventBooking HostedEventBooking { get; set; } = null!;
        public virtual HostedEventNight HostedEventNight { get; set; } = null!;
        public virtual AppUser RecordedByAppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
