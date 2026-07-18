using Ben.Data.Common.Enums;

namespace Ben.Data.Source.Entities
{
    /// <summary>
    /// Grants per-page CMS actions to a specific org member or member group.
    /// At least one of <see cref="AppUserId"/> or <see cref="OrgMemberGroupId"/> must be set.
    /// </summary>
    public partial class CmsPagePermission
    {
        public Guid OrganizationPageId { get; set; }

        /// <summary>Null when the grant targets a group rather than an individual.</summary>
        public Guid? AppUserId { get; set; }

        /// <summary>Null when the grant targets an individual rather than a group.</summary>
        public Guid? OrgMemberGroupId { get; set; }

        /// <summary>Bitmask of allowed CMS actions (View / Edit / Delete).</summary>
        public CmsPageAction Actions { get; set; }

        public DateTime DateCreated { get; set; }
        public DateTime? DateUpdated { get; set; }
        public Guid CreatedByAppUserId { get; set; }
        public Guid? UpdatedByAppUserId { get; set; }

        public virtual OrganizationPage OrganizationPage { get; set; } = null!;
        public virtual AppUser? AppUser { get; set; }
        public virtual OrgMemberGroup? OrgMemberGroup { get; set; }
        public virtual AppUser CreatedByAppUser { get; set; } = null!;
        public virtual AppUser? UpdatedByAppUser { get; set; }
    }
}
