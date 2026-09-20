using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A letter somebody wrote, standing in for the one the code writes (item 246).
    /// </summary>
    /// <remarks>
    /// <para><b>No row means the built-in letter.</b> That is the whole safety story: every letter
    /// the site sends works today with no rows in this table at all, and a template only ever
    /// REPLACES one. Deleting a row is therefore also the revert — there is nothing to restore
    /// because the original never left the code.</para>
    ///
    /// <para><b>A draft is private until it is published</b>, and publishing is what takes effect.
    /// Editing a letter that goes to thousands of people is not something to do live, and the
    /// author needs to be able to look at it first — which is what the preview is for.</para>
    ///
    /// <para><b>Keyed by <c>Kind</c>, which is declared</b> (<c>MailKinds</c>), not guessed from a
    /// subject line. That is the reason kinds exist at all: a template joined to a guess stops
    /// applying the day somebody rewords a subject.</para>
    /// </remarks>
    public partial class EmailTemplate : IAuditableEntity
    {
        public Guid Id { get; set; }

        /// <summary>The <c>MailKinds</c> key this stands in for. Unique.</summary>
        public string Kind { get; set; } = null!;

        /// <summary>
        /// The subject people receive, with tokens still in it. Null until first published.
        /// </summary>
        public string? Subject { get; set; }

        /// <summary>The body people receive, with tokens still in it. Null until first published.</summary>
        public string? BodyHtml { get; set; }

        /// <summary>When this last took effect. Null means the built-in letter is still in use.</summary>
        public DateTime? PublishedUtc { get; set; }
        public Guid? PublishedByAppUserId { get; set; }

        /// <summary>What the author is working on, seen by nobody else until it is published.</summary>
        public string? DraftSubject { get; set; }

        /// <inheritdoc cref="DraftSubject"/>
        public string? DraftBodyHtml { get; set; }
        public DateTime? DraftSavedUtc { get; set; }
        public Guid? DraftAuthorAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        /// <summary>True when this row is what a reader would actually get.</summary>
        public bool IsLive => PublishedUtc is not null
                           && !string.IsNullOrWhiteSpace(Subject)
                           && !string.IsNullOrWhiteSpace(BodyHtml);

        /// <summary>True when the author has changes nobody has received yet.</summary>
        public bool HasUnpublishedDraft => DraftSavedUtc is not null
                                        && (DraftSubject != Subject || DraftBodyHtml != BodyHtml);
    }
}
