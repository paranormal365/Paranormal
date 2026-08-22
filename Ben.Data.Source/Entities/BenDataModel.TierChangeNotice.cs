using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A promise to tell one group, before its renewal, that its plan is changing downward.
    /// </summary>
    /// <remarks>
    /// <para>Only reductions live here. Improvements are announced the moment they are saved and
    /// need no queue; a reduction must reach the group <i>before</i> the renewal that applies it —
    /// two weeks before, floored at "now" for cadences shorter than the notice period.</para>
    ///
    /// <para><b>The sentences are frozen at edit time</b>, not recomputed at delivery. The tier
    /// may be edited again before the notice fires, and the honest message is the one describing
    /// the change that was actually made — a second edit queues a second notice.</para>
    ///
    /// <para><see cref="DeliveredAtUtc"/> is what makes the delivery job idempotent: the job picks
    /// up due, undelivered rows, and a crash between send and mark risks at worst a duplicate
    /// message, which for a billing notice is the right side to err on.</para>
    /// </remarks>
    public class TierChangeNotice : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid OrganizationId { get; set; }

        public Guid SubscriptionTierId { get; set; }

        /// <summary>The reduction sentences, one per line, worded for the group.</summary>
        public string Sentences { get; set; } = string.Empty;

        /// <summary>When the group's current period ends — the date the changes take effect.</summary>
        public DateTime EffectiveAtUtc { get; set; }

        /// <summary>When to send: two weeks before <see cref="EffectiveAtUtc"/>, floored at creation time.</summary>
        public DateTime DeliverAtUtc { get; set; }

        /// <summary>Set when the message actually went out. Null rows are the job's work queue.</summary>
        public DateTime? DeliveredAtUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual SubscriptionTier SubscriptionTier { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
