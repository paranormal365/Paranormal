using System;

namespace Ben.Data.Source.Entities
{
    public partial class OrganizationPage
    {
        public Guid OrganizationId { get; set; }
        public bool IsHome { get; set; }
        public string PageTitle { get; set; } = null!;
        public string UrlName { get; set; } = null!;
        public string PageHtml { get; set; } = null!;
        public bool IsPublished { get; set; }
        public int SortOrder { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
