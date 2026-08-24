using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// One human judgment about whether a post's content matches its experience type
    /// (item 186 F6). APPEND-ONLY — this table is the asset.
    /// </summary>
    /// <remarks>
    /// <para>Ben asked for "code that learns as we go with building our paranormal database".
    /// This table is the honest version of that: every moderator decision, group claim, and
    /// author correction becomes a labelled example against the platform's own experience
    /// taxonomy, and the weight re-fit reads them all. The model improves exactly as fast as
    /// people use the site — which is the only way it genuinely can.</para>
    ///
    /// <para>Append-only means corrections are new rows, not edits: a judgment that was later
    /// reversed is still a fact about how hard the example was, and the fit can weigh
    /// disagreement. Nothing updates or deletes here (guarded in
    /// <c>FeedLearningService</c>); rows survive their post via SetNull so the example
    /// outlives a deleted or hidden post.</para>
    /// </remarks>
    public class FeedLabelledExample
    {
        public Guid Id { get; set; }

        /// <summary>The post judged. Null after the post is gone; the example remains.</summary>
        public Guid? OrgMessageId { get; set; }

        /// <summary>The experience type the judgment is about — the label's subject.</summary>
        public Guid ExperienceTypeId { get; set; }

        public FeedLabel Label { get; set; }
        public FeedLabelSource Source { get; set; }

        /// <summary>
        /// The features as they stood when judged, denormalized as JSON. The judgment must stay
        /// interpretable even after the post (and its feature row) is deleted — an example that
        /// loses its inputs teaches nothing.
        /// </summary>
        public string? FeaturesJson { get; set; }

        public Guid DecidedByAppUserId { get; set; }
        public DateTime DecidedUtc { get; set; }

        public virtual OrgMessage? OrgMessage { get; set; }
        public virtual ExperienceType ExperienceType { get; set; } = null!;
        public virtual AppUser DecidedByAppUser { get; set; } = null!;
    }
}
