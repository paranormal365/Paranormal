using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    public partial class CmsSection
    {
        public Guid OrganizationPageId { get; set; }

        /// <summary>Determines how <see cref="ContentJson"/> is interpreted by the UI.</summary>
        public CmsSectionType SectionType { get; set; }

        public string? Title { get; set; }

        /// <summary>
        /// JSON payload whose schema depends on <see cref="SectionType"/>:
        /// RichText → { "html": "..." }
        /// ImageBanner → { "uploadFileId": "...", "altText": "...", "linkUrl": "..." }
        /// FileGallery → { "uploadFileIds": ["...", "..."] }
        /// ContactInfo → { "showAddresses": true, "showEmails": true, ... }
        /// MemberRoster → { "memberIds": ["..."], "showRole": true, "showBio": false }
        /// CustomHtml → { "html": "..." }
        /// </summary>
        public string ContentJson { get; set; } = "{}";

        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual OrganizationPage OrganizationPage { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
