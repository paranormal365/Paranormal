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

        /// <summary>
        /// One photo or video carried by a feed post (item 186 F4). Null for a text post.
        /// </summary>
        public Guid? MediaUploadFileId { get; set; }

        /// <summary>
        /// Whether that media may be shown. <b>Pending by default, and Pending is never served.</b>
        /// </summary>
        /// <remarks>
        /// Fail-closed by the data model: see <see cref="Ben.Data.Common.Enums.FeedMediaReviewState"/>.
        /// Meaningless when <see cref="MediaUploadFileId"/> is null, and left at its default there.
        /// </remarks>
        public Ben.Data.Common.Enums.FeedMediaReviewState MediaReviewState { get; set; }

        /// <summary>
        /// What the screener or the moderator said about it. For the queue, never for the poster.
        /// </summary>
        /// <remarks>
        /// Telling somebody exactly which check their upload tripped is telling them exactly how
        /// to dress the next one.
        /// </remarks>
        public string? MediaReviewNote { get; set; }

        /// <summary>Who decided, when a person did. Null when a screener decided, or nobody has.</summary>
        public Guid? MediaReviewedByAppUserId { get; set; }

        /// <summary>When the decision was made.</summary>
        public DateTime? MediaReviewedUtc { get; set; }

        /// <summary>The file itself.</summary>
        public virtual UploadFile? MediaUploadFile { get; set; }

        /// <summary>
        /// What the author says this post shows, from the platform's experience taxonomy
        /// (item 186 F6). Null for chatter — a category is encouraged for media, never required.
        /// </summary>
        /// <remarks>
        /// Deliberately the SAME taxonomy cases and evidence use, not a feed-only list: every
        /// judgment about whether content matches its type (<see cref="FeedLabelledExample"/>)
        /// then accumulates against the taxonomy of record.
        /// </remarks>
        public Guid? FeedExperienceTypeId { get; set; }

        /// <summary>
        /// How well the media's measured features fit the chosen type, 0–1, scored at post time
        /// (and re-scored on recategorize). Null when unscored: no media, no type, or no
        /// features. A low score NUDGES the author and gently lowers ranking — it never blocks
        /// and is never shown to other readers.
        /// </summary>
        public double? CategoryMatchScore { get; set; }

        public virtual ExperienceType? FeedExperienceType { get; set; }

        /// <summary>The post's measured media facts. Null until extracted (or for text posts).</summary>
        public virtual FeedMediaFeatureSet? MediaFeatures { get; set; }

        /// <summary>Feed likes (item 186 F3). Empty for every non-feed message.</summary>
        public virtual ICollection<OrgMessageLike> Likes { get; set; } = new List<OrgMessageLike>();
        public virtual ICollection<OrgMessageMention> Mentions { get; set; } = new List<OrgMessageMention>();
        public virtual ICollection<OrgMessageHashtag> Hashtags { get; set; } = new List<OrgMessageHashtag>();
        public virtual ICollection<OrgMessageReport> Reports { get; set; } = new List<OrgMessageReport>();
    }
}
