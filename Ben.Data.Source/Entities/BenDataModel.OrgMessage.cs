using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// An internal organization message. Supports threading (via ParentMessageId),
    /// multiple delivery channels, per-recipient read tracking, and an encryption flag.
    /// </summary>
    public partial class OrgMessage : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>Owning organization. Null for cross-org or public feed messages.</summary>
        public Guid? OrganizationId { get; set; }

        public Guid AuthorAppUserId { get; set; }

        /// <summary>Parent message ID for threaded replies. Null for top-level messages.</summary>
        public Guid? ParentMessageId { get; set; }

        public OrgMessageChannel ChannelType { get; set; } = OrgMessageChannel.OrgBroadcast;

        public string? Subject { get; set; }

        /// <summary>HTML message body.</summary>
        public string Body { get; set; } = null!;

        /// <summary>Flag indicating this message content should be treated as encrypted/private.</summary>
        public bool IsEncrypted { get; set; }

        /// <summary>When true, this post is visible outside the organization (public feed).</summary>
        public bool IsPublic { get; set; }

        /// <summary>Scopes the message to a specific case team. Null = not case-scoped.</summary>
        public Guid? CaseId { get; set; }

        /// <summary>Total view count — incremented each time a recipient opens the message.</summary>
        public int ViewCount { get; set; }

        /// <summary>
        /// When an administrator hid this post from the public feed. Null means visible.
        /// </summary>
        /// <remarks>
        /// <para>Hidden rather than deleted, and the distinction is deliberate. A deleted post
        /// takes its replies, its reports and the record of the decision with it, so the next
        /// administrator asking "what happened here" finds nothing. Hiding keeps all of that and
        /// removes the post from every feed query.</para>
        ///
        /// <para>Only ever set by an administrator resolving a report — a pile of reports never
        /// hides anything on its own. See <see cref="OrgMessageReport"/>.</para>
        /// </remarks>
        public DateTime? HiddenUtc { get; set; }

        /// <summary>Which administrator hid it.</summary>
        public Guid? HiddenByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization? Organization { get; set; }
        public virtual AppUser AuthorAppUser { get; set; } = null!;
        public virtual OrgMessage? ParentMessage { get; set; }
        public virtual Case? Case { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual AppUser? HiddenByAppUser { get; set; }
        public virtual ICollection<OrgMessage> Replies { get; set; } = new List<OrgMessage>();
        public virtual ICollection<OrgMessageRecipient> Recipients { get; set; } = new List<OrgMessageRecipient>();
        public virtual ICollection<OrgMessageView> Views { get; set; } = new List<OrgMessageView>();

        /// <summary>Feed likes (item 186 F3). Empty for every non-feed message.</summary>
        public virtual ICollection<OrgMessageLike> Likes { get; set; } = new List<OrgMessageLike>();
        public virtual ICollection<OrgMessageMention> Mentions { get; set; } = new List<OrgMessageMention>();
        public virtual ICollection<OrgMessageHashtag> Hashtags { get; set; } = new List<OrgMessageHashtag>();
        public virtual ICollection<OrgMessageReport> Reports { get; set; } = new List<OrgMessageReport>();
    }
}
