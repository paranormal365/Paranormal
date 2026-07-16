using Ben.Data.Common.Enums;
using Ben.Data.Common.Interfaces;

namespace Ben.Data.Source.Entities;

public partial class OrganizationUserMembership : IAuditableEntity
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid AppUserId { get; set; }
    public OrganizationMemberRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime DateCreated { get; set; }
    public DateTime? DateUpdated { get; set; }
    public Guid CreatedByAppUserId { get; set; }
    public Guid? UpdatedByAppUserId { get; set; }
}