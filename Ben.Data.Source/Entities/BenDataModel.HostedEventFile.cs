using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// A file that belongs to an event: the guest pack, the fire exits, the running order, the waiver
    /// (item 235 phase 11).
    /// </summary>
    /// <remarks>
    /// <para><b>Who it is for is part of the file.</b> The stewards' briefing is for the people helping,
    /// the guest pack for the people coming, the poster for anybody — and the three are different
    /// answers to "may I download this", decided on the server every time
    /// (<see cref="Audience"/>).</para>
    ///
    /// <para><b>Folders are a word, not a tree.</b> "Guest pack", "Stewards", "Menus" — a venue sorts a
    /// weekend's files into a handful of piles, and a folder hierarchy would be a file manager nobody
    /// asked for. There is therefore no path to traverse and nothing to escape from.</para>
    ///
    /// <para>The bytes are an <see cref="UploadFile"/> owned by the event's group, stored under
    /// <c>orgs/{org}/events/{event}/</c>, so the group's files are where its files have always been.</para>
    /// </remarks>
    public class HostedEventFile : IAuditableEntity
    {
        public Guid Id { get; set; }
        public Guid HostedEventId { get; set; }
        public Guid UploadFileId { get; set; }

        /// <summary>The pile it is in: "Guest pack". Null is loose, shown first.</summary>
        public string? Folder { get; set; }

        /// <summary>What it is, when the file name does not say.</summary>
        public string? Description { get; set; }

        public EventFileAudience Audience { get; set; } = EventFileAudience.Staff;
        public int SortOrder { get; set; }

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
