using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A file somebody added to a public place because of what is in it (item 250).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-21: <i>"people don't have to have a dedicated investigation to add files
    /// to the public location. They can add files to the location which can be voted on."</i></para>
    ///
    /// <para><b>Why this is a table rather than a <c>PlaceId</c> on the file.</b> Evidence reaches
    /// a place by two routes with different lifetimes. A file from a PUBLISHED FIELD SESSION is
    /// derived — <see cref="Ben.Data.WebApi.Controllers.Public.ArchiveMediaPublication"/>
    /// deliberately re-asks its rule on every request so that retracting a session takes its bytes
    /// down with it, and a row copied here would outlive the publication it stood for. So this
    /// table holds only what was added HERE, and a place's total is this plus that live query,
    /// de-duplicated by file. One place, one total, and nothing counted twice — which is the
    /// answer Ben chose.</para>
    ///
    /// <para><b>Screened before anybody sees it.</b> The door is open to anybody signed in, which
    /// is the point of a public place page: the person with the photograph is rarely a member of
    /// anything. An open door on a public page is a moderation surface, so it goes through the
    /// same screener and the same held pile the feed already has rather than a second one.
    /// <see cref="ReviewState"/> starts <see cref="FeedMediaReviewState.Pending"/> and only a
    /// verdict moves it, so a screener that breaks grows a queue instead of publishing something
    /// nobody looked at.</para>
    ///
    /// <para><b>Only ever a <see cref="PlaceKind.PublicLocation"/>.</b> Enforced where rows are
    /// made and re-asked where they are read, for the reason the archive gives: a place later
    /// corrected to a private residence has to take its evidence down with it, with nothing to
    /// remember and no page to go and edit.</para>
    /// </remarks>
    public partial class PlaceEvidence : IAuditableEntity
    {
        public Guid Id { get; set; }

        public Guid PlaceId { get; set; }
        public Place? Place { get; set; }

        /// <summary>The file itself. One per row, and one row per file per place.</summary>
        public Guid UploadFileId { get; set; }
        public UploadFile? UploadFile { get; set; }

        /// <summary>Who added it. Shown, because anonymous evidence is worth less than named.</summary>
        public Guid AddedByAppUserId { get; set; }
        public AppUser? AddedByAppUser { get; set; }

        /// <summary>
        /// What they say it is — where in the building, what time, what to listen for.
        /// </summary>
        /// <remarks>
        /// Optional and short. A photograph of a dark corridor says nothing on its own, and the
        /// sentence beside it is most of what makes the file worth voting on.
        /// </remarks>
        public string? Caption { get; set; }

        /// <summary>
        /// Whether a stranger may see it. Starts Pending; only a verdict moves it.
        /// </summary>
        public FeedMediaReviewState ReviewState { get; set; } = FeedMediaReviewState.Pending;

        /// <summary>Why the screener or a moderator held it, in words.</summary>
        public string? ReviewNote { get; set; }

        /// <summary>What the automatic screener scored it, when one ran.</summary>
        public double? ScreenerScore { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }
    }
}
