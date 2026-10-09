using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities;

public partial class OrganizationUserMembership : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid AppUserId { get; set; }
    public OrganizationMemberRole Role { get; set; }

    /// <summary>The member's title on the group's ladder (item 157), or null for none. A label,
    /// not a permission: nothing may read this to decide access.</summary>
    public Guid? MemberLevelId { get; set; }

    public virtual OrganizationMemberLevel? MemberLevel { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Whether this member agreed to be named on the group's public pages — its "Our members" section
    /// (backlog 256, 10/09/2026). Off until the member turns it on from their own profile: a group can choose
    /// whom to show, but only from the people who said yes, because a public page naming somebody as a ghost
    /// hunter is theirs to agree to.
    /// </summary>
    public bool ShowOnPublicPages { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public Guid CreatedByAppUserId { get; set; }
    public Guid? UpdatedByAppUserId { get; set; }
}