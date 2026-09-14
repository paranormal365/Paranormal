using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A picture in a venue's photo library (item 235 phase 12).
    /// </summary>
    /// <remarks>
    /// <para>Ben, 2026-09-13: <i>"Images of the venue should also be saved, either ones taken by the venue owner /
    /// employee, or ones shared with the venue by an organizer."</i></para>
    ///
    /// <para><b>Two ways in.</b> The venue's own people add pictures, which are in the library straight away. An
    /// organizer offers one of their event's gallery pictures, which waits for the venue to accept it: a venue's
    /// public page shows the building the way the venue chooses, not every photograph somebody took there.</para>
    ///
    /// <para><b>An offered picture is the organizer's file, referenced, not copied.</b> While the venue keeps it the
    /// file survives the event's 90-day tidy-up, because nothing deletes a file something still points at.</para>
    /// </remarks>
    public class VenuePhoto : IAuditableEntity
    {
        public const int MaxPerVenue = 60;

        public Guid Id { get; set; }
        public Guid OrganizationVenueProfileId { get; set; }
        public Guid UploadFileId { get; set; }
        public string? Caption { get; set; }
        public int SortOrder { get; set; }

        /// <summary>The group that offered it; null for the venue's own.</summary>
        public Guid? OfferedByOrganizationId { get; set; }

        /// <summary>The event whose gallery it came from, when it was offered.</summary>
        public Guid? OfferedFromHostedEventId { get; set; }

        /// <summary>When the venue accepted it. Null while an offer waits; the venue's own are accepted as added.</summary>
        public DateTime? AcceptedUtc { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual OrganizationVenueProfile OrganizationVenueProfile { get; set; } = null!;
        public virtual UploadFile UploadFile { get; set; } = null!;
        public virtual Organization? OfferedByOrganization { get; set; }
        public virtual HostedEvent? OfferedFromHostedEvent { get; set; }
    }
}
