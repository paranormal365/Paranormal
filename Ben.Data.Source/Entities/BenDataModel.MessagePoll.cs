namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A poll attached to a message (item 233, Ben 2026-09-11).
    /// </summary>
    /// <remarks>
    /// <para><b>Keyed to an <see cref="OrgMessage"/>, not to the feed.</b> That entity is already
    /// what a feed post, a case comment and a group's own message all are, so a poll written once
    /// works in all three places — which is what "reusable" has to mean here, or the second caller
    /// gets a second poll implementation.</para>
    ///
    /// <para>The question lives here and the answers in <see cref="MessagePollOption"/>, because a
    /// poll with its options in one column could never be voted on — a vote has to point at an
    /// option that exists on its own.</para>
    /// </remarks>
    public partial class MessagePoll
    {
        public Guid Id { get; set; }

        /// <summary>The message this poll belongs to. One poll per message.</summary>
        public Guid OrgMessageId { get; set; }

        /// <summary>What is being asked.</summary>
        public string Question { get; set; } = null!;

        /// <summary>
        /// When voting stops, or null for a poll that stays open.
        /// </summary>
        /// <remarks>
        /// A moment rather than a duration: "three days" is what the author picks and an instant is
        /// what everybody reading it has to agree on, including a reader in another zone and the
        /// server deciding whether a vote is too late.
        /// </remarks>
        public DateTime? ClosesAtUtc { get; set; }

        /// <summary>Whether somebody may choose more than one answer.</summary>
        public bool AllowMultiple { get; set; }

        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual OrgMessage OrgMessage { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual ICollection<MessagePollOption> Options { get; set; } = new List<MessagePollOption>();
    }

    /// <summary>One answer somebody can pick.</summary>
    public partial class MessagePollOption
    {
        public Guid Id { get; set; }
        public Guid MessagePollId { get; set; }

        public string Text { get; set; } = null!;

        /// <summary>The order the author put them in, which is the order they are shown.</summary>
        public int SortOrder { get; set; }

        public virtual MessagePoll MessagePoll { get; set; } = null!;
        public virtual ICollection<MessagePollVote> Votes { get; set; } = new List<MessagePollVote>();
    }

    /// <summary>
    /// One person's answer.
    /// </summary>
    /// <remarks>
    /// <para>The poll id is carried as well as the option id, redundantly, so the database itself
    /// can hold the rule that matters: one vote per person per poll, as a unique index. Derived
    /// from the option at write time it would be a rule only the code remembered.</para>
    ///
    /// <para>That index is dropped for a multiple-choice poll, which is why the uniqueness is on
    /// (poll, option, person) rather than (poll, person) — picking two answers is two rows, and
    /// picking the same answer twice is still refused.</para>
    /// </remarks>
    public partial class MessagePollVote
    {
        public Guid Id { get; set; }
        public Guid MessagePollId { get; set; }
        public Guid MessagePollOptionId { get; set; }
        public Guid AppUserId { get; set; }

        public DateTime DateCreated { get; set; }

        public virtual MessagePoll MessagePoll { get; set; } = null!;
        public virtual MessagePollOption MessagePollOption { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
    }
}
