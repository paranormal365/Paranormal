using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Somewhere else a tour can be found (item 233, Ben 2026-09-10).
    /// </summary>
    /// <remarks>
    /// <para><b>Ben:</b> "We should probably let the tour add their Instagram, Facebook, X, URL,
    /// TikTok, BlueSky, Rumble, YouTube… These could be displayed at the bottom of their pages."
    /// A walking-tour business lives on the accounts where it posts last night's photographs, and
    /// a page that does not link to them is asking somebody to go and search for the name.</para>
    ///
    /// <para>One row per platform per tour: two Instagram accounts for one walk is a mistake
    /// rather than a feature, and the unique index says so. The order is the business's own.</para>
    ///
    /// <para>The URL is checked against the platform's own hosts on the way in
    /// (<see cref="SocialPlatforms.IsAllowed"/>) — an icon that says Instagram and opens somewhere
    /// else is a link-laundering trick on a page anyone can publish to.</para>
    /// </remarks>
    public partial class TourSocialLink
    {
        public Guid Id { get; set; }
        public Guid TourId { get; set; }

        /// <summary>Which service. Stored as its number; the enum is append-only.</summary>
        public SocialPlatform Platform { get; set; }

        /// <summary>Where it points. Absolute http or https, on that platform's own hosts.</summary>
        public string Url { get; set; } = null!;

        /// <summary>The order the business listed them in.</summary>
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public Guid CreatedByAppUserId { get; set; }

        public virtual Tour Tour { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
    }
}
