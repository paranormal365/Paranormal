namespace Ben.Data.Source.Entities
{
    /// <summary>One piece in a publication.</summary>
    /// <remarks>
    /// <para><b>A draft is a post with no <see cref="PublishedUtc"/>.</b> One nullable column
    /// rather than a status enum: there are exactly two states a post can be in, and the timestamp
    /// is wanted anyway for ordering and for telling a reader when it appeared. A separate enum
    /// would be a second record of the same fact, free to disagree with the date beside it.</para>
    ///
    /// <para><b>The body is stored already sanitised.</b> Cleaning on save rather than on render
    /// means every future reader is safe without having to remember to be — the alternative is one
    /// forgotten call site away from serving whatever was submitted.</para>
    /// </remarks>
    public partial class PublicationPost
    {
        public Guid Id { get; set; }

        public Guid PublicationId { get; set; }

        public string Title { get; set; } = null!;

        /// <summary>
        /// The readable part of this post's address, unique within its publication.
        /// </summary>
        /// <remarks>
        /// Generated once and never regenerated. Renaming a post must not break a link somebody
        /// has already shared — see <see cref="Publication.UrlName"/>.
        /// </remarks>
        public string UrlName { get; set; } = null!;

        /// <summary>
        /// A short standfirst, shown in listings — and served <i>instead of</i> the body for a post
        /// whose tier the reader does not hold.
        /// </summary>
        /// <remarks>
        /// That second job is why it is not optional in practice: a tiered post with no excerpt
        /// would appear in a directory as a title and nothing else, which sells nobody anything.
        /// </remarks>
        public string? Excerpt { get; set; }

        /// <summary>The post itself, as sanitised HTML.</summary>
        public string BodyHtml { get; set; } = null!;

        /// <summary>When it was published. Null means it is still a draft.</summary>
        public DateTime? PublishedUtc { get; set; }

        /// <summary>
        /// Which paid tier a reader needs. <b>Null means free</b>, which every post is today.
        /// </summary>
        /// <remarks>
        /// <para>Reserved for monetisation (backlog item 85). <b>Nothing writes a non-null value
        /// yet</b> — there is no billing, no tier list and no way to buy one.</para>
        ///
        /// <para>It exists now, unused, because the alternative is retrofitting a paywall onto
        /// posts that are already published and already being read. The public reader honours it
        /// today — a tiered post serves its excerpt and withholds its body — so the path is written
        /// and tested while it costs nothing.</para>
        /// </remarks>
        public int? RequiredTier { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Publication Publication { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
