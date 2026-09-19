namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One person following another's public posts.
    /// </summary>
    /// <remarks>
    /// <para>Deliberately one-directional and unacknowledged. Following is a reading choice, not a
    /// relationship — there is nothing to accept and nothing to reciprocate, and the person being
    /// followed is not asked. That is the right shape for a feed whose posts are public anyway:
    /// following changes which of them you are shown, not which of them you are allowed to see.
    /// A mutual-consent model would imply a privacy guarantee the feed does not make.</para>
    ///
    /// <para>Unfollowing deletes the row rather than flagging it. There is no history worth
    /// keeping here, and a soft-deleted follow is a record of who once read whom.</para>
    /// </remarks>
    public partial class UserFollow
    {
        public Guid Id { get; set; }

        /// <summary>The person doing the following.</summary>
        public Guid FollowerAppUserId { get; set; }

        /// <summary>The person being followed.</summary>
        public Guid FollowedAppUserId { get; set; }

        public DateTime DateCreated { get; set; }

        public virtual AppUser FollowerAppUser { get; set; } = null!;
        public virtual AppUser FollowedAppUser { get; set; } = null!;
    }
}
