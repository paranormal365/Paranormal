using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// What a guest thought of an event they came to (item 235 phase 12).
    /// </summary>
    /// <remarks>
    /// <para><b>Its own table, not the tours' one.</b> A tour review hangs off a tour and a date on it;
    /// a hosted event is a thing of its own with no tour behind it, and making the tour key optional
    /// would have left every tour query guarding against a review that belongs to no tour.</para>
    ///
    /// <para><b>Only from somebody who came:</b> the lead or a named guest of a confirmed booking, once
    /// the event is over. One per person per event, editable.</para>
    ///
    /// <para><b>Hide, never edit.</b> The organizer may hide a review, which leaves the words where the
    /// person who wrote them can still see them; nobody may change them.</para>
    /// </remarks>
    public class HostedEventReview : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }
        public Guid AppUserId { get; set; }

        /// <summary>One to five.</summary>
        public int Stars { get; set; }

        public string? Comment { get; set; }

        public DateTime? HiddenAtUtc { get; set; }
        public Guid? HiddenByAppUserId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
    }
}
