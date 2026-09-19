namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A group's publication: a chronological series of long-form posts people subscribe to.
    /// </summary>
    /// <remarks>
    /// <para><b>Not an <see cref="OrganizationPage"/>.</b> Pages carry site structure — an About
    /// page, a Services page — and are edited in place, so what a page says is whatever it says
    /// now. Publication posts are chronological and subscribable, and are never edited into each
    /// other: what somebody read last month stays what they read. Reusing pages for this would
    /// have meant one table answering two questions that disagree about time.</para>
    ///
    /// <para>Owned by an organisation rather than a person. A group's publication survives the
    /// member who started it leaving, which is the whole reason a group has one.</para>
    /// </remarks>
    public partial class Publication
    {
        public Guid Id { get; set; }

        public Guid OrganizationId { get; set; }

        public string Title { get; set; } = null!;

        /// <summary>
        /// The readable part of this publication's address — <c>/publications/{UrlName}</c>.
        /// </summary>
        /// <remarks>
        /// Unique across the site, not just within the organisation, because the public address
        /// carries no organisation in it. Generated once from the title and <b>never regenerated
        /// on rename</b>: item 89 established what happens otherwise — a renamed thing silently
        /// breaks every link anybody shared, and a released name can capture another's traffic.
        /// </remarks>
        public string UrlName { get; set; } = null!;

        /// <summary>What the publication is about, shown on its page and in the directory.</summary>
        public string? Description { get; set; }

        /// <summary>
        /// Off means the publication and its posts are invisible to the public, whatever their own
        /// state.
        /// </summary>
        /// <remarks>
        /// Distinct from a post's own published state, and both must be true for a reader to see
        /// anything. It is the switch for "we are not ready to show this at all", which is
        /// different from having no published posts yet.
        /// </remarks>
        public bool IsPublic { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
        public virtual ICollection<PublicationPost> Posts { get; set; } = new List<PublicationPost>();
        public virtual ICollection<PublicationSubscription> Subscriptions { get; set; } = new List<PublicationSubscription>();
    }
}
