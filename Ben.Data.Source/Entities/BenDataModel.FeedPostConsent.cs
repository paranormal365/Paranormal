namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// The recorded agreement behind publishing a private-engagement case's footage to the feed
    /// (item 186 F7). APPEND-ONLY.
    /// </summary>
    /// <remarks>
    /// <para>Item 184 built the private lane on a promise: a client's engagement stays private.
    /// The one door out is the client-facing person who worked it choosing to publish a render —
    /// and that choice must be an explicit tick on wording that says "private engagement" in
    /// those words, recorded here with who agreed and which wording they saw. When a client
    /// asks "who put this footage up", this row is the answer.</para>
    ///
    /// <para>Nothing updates or deletes here. The post may be hidden or deleted later; the fact
    /// that somebody agreed to publish it is what this table remembers.</para>
    /// </remarks>
    public class FeedPostConsent
    {
        public Guid Id { get; set; }

        /// <summary>The post published under this consent. Null after the post is gone.</summary>
        public Guid? OrgMessageId { get; set; }

        /// <summary>The case whose footage went public.</summary>
        public Guid CaseId { get; set; }

        public Guid AgreedByAppUserId { get; set; }
        public DateTime AgreedUtc { get; set; }

        /// <summary>Which wording they ticked. Version 1 is the F7 dialog; a future rewording
        /// bumps it, so "what exactly did they agree to" always has an answer.</summary>
        public int WordingVersion { get; set; }

        public virtual OrgMessage? OrgMessage { get; set; }
        public virtual Case Case { get; set; } = null!;
        public virtual AppUser AgreedByAppUser { get; set; } = null!;
    }
}
