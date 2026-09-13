using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// People who turned up on the night without a booking (item 235 phase 7).
    /// </summary>
    /// <remarks>
    /// <para><b>Ben, 2026-09-13:</b> <i>"availability count for walk ups to event and on-sight
    /// sign ups."</i> Somebody arrives at a hotel on the Saturday having heard about it in the
    /// pub, hands over cash, and walks in. Every part of that is normal and none of it is a
    /// booking.</para>
    ///
    /// <para><b>Why it is not a booking with a made-up account.</b> The site's settled answer to
    /// "an organizer wants to sign up a stranger" is to send a link rather than to create an
    /// account nobody asked for from an address nobody verified — that is what the walk-up invite
    /// on a tour date does, and the reasoning has not changed. But a link is a round trip of
    /// minutes at best, and the person is standing at the door now. So the door records what
    /// actually happened — <b>this many people came in tonight</b> — and offers the link as the
    /// separate, slower thing it is.</para>
    ///
    /// <para><b>What it is for.</b> The head count. A fire officer asks how many people are in the
    /// building, the kitchen asks how many are eating, and the venue asks how the evening went;
    /// all three are wrong by exactly the walk-ups if they are not written down. They have no
    /// pass, no place on the plan, and nothing to confirm.</para>
    ///
    /// <para><b>A name is optional</b>, because a steward taking twelve pounds in a doorway will
    /// often have nothing else, and a screen that demanded one would be a screen somebody works
    /// around by not recording the people at all.</para>
    /// </remarks>
    public partial class HostedEventWalkUp : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid HostedEventNightId { get; set; }

        /// <summary>How many came in together.</summary>
        public int People { get; set; } = 1;

        /// <summary>What they said their name was, if they said.</summary>
        public string? Name { get; set; }

        /// <summary>Anything the steward wants the venue to know — "paid cash", "friend of the band".</summary>
        public string? Note { get; set; }

        public DateTime ArrivedUtc { get; set; }

        /// <summary>Who was on the door.</summary>
        public Guid RecordedByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEventNight HostedEventNight { get; set; } = null!;
        public virtual AppUser RecordedByAppUser { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
