using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A photograph on an event's public page, chosen by its host (item 235 phase 11).
    /// </summary>
    /// <remarks>
    /// <para><b>The host's pictures, not the room's.</b> A guest's photo from the room is the guest's,
    /// and some of the people in it did not agree to be on a public page (Ben, 2026-09-13). So the public
    /// gallery holds what the organizers put there themselves: last year's weekend, the building, the
    /// ballroom set for dinner.</para>
    ///
    /// <para>Fitted inside 1920×1080 and stripped of everything the camera wrote, exactly as a tour's
    /// gallery is — <see cref="TourGalleryImage"/> is the model this follows.</para>
    /// </remarks>
    public class HostedEventGalleryImage : IAuditableEntity
    {
        public const int MaxPerEvent = 50;

        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }
        public Guid UploadFileId { get; set; }

        /// <summary>First is the page's hero when the event has no cover of its own.</summary>
        public int SortOrder { get; set; }

        /// <summary>Shown under the picture and read aloud as its description.</summary>
        public string? Caption { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual HostedEvent HostedEvent { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
