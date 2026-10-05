using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A titled piece of writing a venue adds to its own page — "Ghost Hunt Weekends", "Dining", "The chapel".
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-10-05: <i>"If they want to add text to the site for their location it should be okay ...
    /// I would like to make things editable for them."</i> The venue page had three fixed things to write (the
    /// building's story, the house rules, photo captions); a hotel that runs hunts, shows and a chapel had nowhere
    /// to say so. Sections are as many as the venue wants, in the order it puts them, shown after the story.</para>
    ///
    /// <para><b>Plain text, like the story.</b> Shown with its line breaks kept and nothing interpreted, so what a
    /// venue types is exactly what a visitor reads.</para>
    /// </remarks>
    public class VenueSection : IAuditableEntity
    {
        /// <summary>A heading, not a paragraph.</summary>
        public const int MaxTitleLength = 200;

        public Guid Id { get; set; }
        public Guid OrganizationVenueProfileId { get; set; }
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public int SortOrder { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual OrganizationVenueProfile OrganizationVenueProfile { get; set; } = null!;
    }
}
