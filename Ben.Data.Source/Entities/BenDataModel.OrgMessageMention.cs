namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A record that one post named one person with an <c>@</c>.
    /// </summary>
    /// <remarks>
    /// <para>A table rather than a search over post bodies, and that is the whole reason it
    /// exists. "Which posts mentioned me" is a question the notification buckets ask on every
    /// poll; answering it by scanning text for a display name would be slow, and wrong in both
    /// directions — it would miss a renamed account and match anybody whose name is a substring of
    /// somebody else's.</para>
    ///
    /// <para>Written by the server when a post is created, from the names it could actually
    /// resolve. An <c>@</c> followed by something that matches no account is left as plain text
    /// and produces no row: a mention that reaches nobody is a typo, not a notification.</para>
    /// </remarks>
    public partial class OrgMessageMention
    {
        public Guid Id { get; set; }

        public Guid OrgMessageId { get; set; }

        /// <summary>The account named in the post.</summary>
        public Guid MentionedAppUserId { get; set; }

        public DateTime DateCreated { get; set; }

        public virtual OrgMessage OrgMessage { get; set; } = null!;
        public virtual AppUser MentionedAppUser { get; set; } = null!;
    }
}
