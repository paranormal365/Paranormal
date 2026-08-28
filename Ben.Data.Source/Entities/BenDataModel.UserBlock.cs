namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One person refusing to see another's public posts.
    /// </summary>
    /// <remarks>
    /// <para>App Review Guideline 1.2: an app with user-generated content must let a person block
    /// an abusive user, not only report them. Reporting asks a moderator to act; blocking acts
    /// now, for this one reader, without waiting for anyone.</para>
    ///
    /// <para><b>A reading choice, like <see cref="UserFollow"/> — not a wall.</b> Blocking hides
    /// the blocked person's posts and replies from the blocker. It does not hide the blocker from
    /// them: these are public posts, the block must not be detectable by the person blocked (an
    /// abuser who can see they were blocked has been handed a reaction), and a mutual-invisibility
    /// model would imply a privacy guarantee a public feed cannot keep.</para>
    ///
    /// <para><b>Blocking also severs following, both directions.</b> "I never want to see them"
    /// and "I follow their posts" cannot both be true, and leaving the other direction standing
    /// would keep the blocker in the abuser's following feed.</para>
    ///
    /// <para>Unblocking deletes the row rather than flagging it, for <see cref="UserFollow"/>'s
    /// reason: a soft-deleted block is a record of who once fell out with whom.</para>
    /// </remarks>
    public partial class UserBlock
    {
        public Guid Id { get; set; }

        /// <summary>The person who no longer wants to see the other.</summary>
        public Guid BlockerAppUserId { get; set; }

        /// <summary>The person being blocked.</summary>
        public Guid BlockedAppUserId { get; set; }

        public DateTime DateCreated { get; set; }

        public virtual AppUser BlockerAppUser { get; set; } = null!;
        public virtual AppUser BlockedAppUser { get; set; } = null!;
    }
}
