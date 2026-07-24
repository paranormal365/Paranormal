using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record OrganizationRolePermissionRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationRoleId { get; init; }
    public OrganizationSecurityTable TableName { get; init; }
    public OrganizationSecurityAction Actions { get; init; }
    public DateTime DateCreated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
}
