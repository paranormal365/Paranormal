namespace Ben.Data.Source.Entities
{
    /// <summary>Somebody following a publication.</summary>
    /// <remarks>
    /// <para>Kept rather than deleted when somebody unsubscribes: <see cref="CancelledUtc"/> marks
    /// it instead. Unlike a feed follow — which is deleted, because a soft-deleted follow is a
    /// record of who once read whom — a subscription is the thing a payment would attach to, and a
    /// cancelled one has to remain answerable for what it covered.</para>
    ///
    /// <para>Re-subscribing clears the cancellation rather than adding a row, so one person has at
    /// most one subscription per publication and the unique index can say so.</para>
    /// </remarks>
    public partial class PublicationSubscription
    {
        public Guid Id { get; set; }

        public Guid PublicationId { get; set; }

        public Guid SubscriberAppUserId { get; set; }

        /// <summary>
        /// Which tier they hold. <b>Null means the free tier</b>, which is every subscription today.
        /// </summary>
        /// <remarks>Reserved for backlog item 85. Nothing writes a non-null value yet.</remarks>
        public int? Tier { get; set; }

        /// <summary>When they unsubscribed. Null means still subscribed.</summary>
        public DateTime? CancelledUtc { get; set; }

        public DateTime DateCreated { get; set; }

        public virtual Publication Publication { get; set; } = null!;
        public virtual AppUser SubscriberAppUser { get; set; } = null!;
    }
}
