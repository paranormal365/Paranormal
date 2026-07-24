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

        /// <summary>When true the page is visible to the public; when false only org members can view it.</summary>
        public bool IsPublic { get; set; }

        public int SortOrder { get; set; }

        /// <summary>Parent page ID for hierarchical navigation. Null for top-level pages.</summary>
        public Guid? ParentPageId { get; set; }

        /// <summary>
        /// When set, this page belongs to a specific case (auto-generated when a case is accepted).
        /// Null for org-wide pages.
        /// </summary>
        public Guid? CaseId { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual Organization Organization { get; set; } = null!;
        public virtual OrganizationPage? ParentPage { get; set; }
        public virtual Case? Case { get; set; }
        public virtual ICollection<OrganizationPage> ChildPages { get; set; } = new List<OrganizationPage>();
        public virtual ICollection<CmsSection> CmsSections { get; set; } = new List<CmsSection>();
        public virtual ICollection<CmsPagePermission> PagePermissions { get; set; } = new List<CmsPagePermission>();
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
