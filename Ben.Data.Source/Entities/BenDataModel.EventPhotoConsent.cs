using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A guest's agreement that their photos at one event may be shown in its room and on its photo wall
    /// (item 235 phase 11).
    /// </summary>
    /// <remarks>
    /// Ben, 2026-09-13: "If we allow attendees to upload without having to go through the organizer or
    /// employee, we should tell them the first time that by submitting … they are giving permission to use
    /// the photo … it may be on a photo wall or slideshow." Asked once per person per event, recorded with
    /// the words they were shown, and required by the server — so it is a fact that they agreed, not a
    /// banner somebody may or may not have read.
    /// </remarks>
    public class EventPhotoConsent : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }
        public Guid AppUserId { get; set; }

        /// <summary>The notice as it read when they agreed.</summary>
        public string Wording { get; set; } = null!;

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual AppUser AppUser { get; set; } = null!;
    }
}
