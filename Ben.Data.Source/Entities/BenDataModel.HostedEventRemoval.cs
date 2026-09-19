using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// IsHaunted taking an event off the site, and the organizer's appeal against it (item 235 phase 17b).
    /// </summary>
    /// <remarks>
    /// <para><b>A recorded state, not a purge.</b> The event, its bookings and its history stay, so an appeal has
    /// something to restore and a dispute has something to read. This row is who removed it, when, what it was before,
    /// whether its credit came back, and the appeal.</para>
    ///
    /// <para><b>One appeal per removal.</b> An event removed again after an upheld appeal gets a new row.</para>
    ///
    /// <para><b>The note is for the site's own people.</b> The organizer's letter is deliberately generic; what the
    /// reviewer wrote here is never sent to anyone.</para>
    /// </remarks>
    public class HostedEventRemoval : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid HostedEventId { get; set; }

        /// <summary>What the event was before it was removed, for the record and for the reviewer of an appeal.</summary>
        public HostedEventLifecycleState PreviousState { get; set; }

        /// <summary>Why, for the site's own people only. Never sent.</summary>
        public string? Note { get; set; }

        public Guid RemovedByAppUserId { get; set; }
        public DateTime RemovedUtc { get; set; }

        /// <summary>Whether a spent event credit went back to whoever paid for it.</summary>
        public bool CreditReturned { get; set; }

        /// <summary>How many parties with a place were written to.</summary>
        public int GuestsTold { get; set; }

        public HostedEventAppealState AppealState { get; set; } = HostedEventAppealState.NotAppealed;

        /// <summary>What the organizer wrote.</summary>
        public string? AppealMessage { get; set; }
        public Guid? AppealedByAppUserId { get; set; }
        public DateTime? AppealedUtc { get; set; }

        /// <summary>What the reviewer said back. Sent to the organizer with the answer.</summary>
        public string? DecisionNote { get; set; }
        public Guid? DecidedByAppUserId { get; set; }
        public DateTime? DecidedUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual AppUser RemovedByAppUser { get; set; } = null!;
        public virtual AppUser? AppealedByAppUser { get; set; }
        public virtual AppUser? DecidedByAppUser { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
