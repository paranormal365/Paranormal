using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record CmsPagePermissionRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationPageId { get; init; }
    public Guid? AppUserId { get; init; }
    public Guid? OrgMemberGroupId { get; init; }
    public CmsPageAction Actions { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
