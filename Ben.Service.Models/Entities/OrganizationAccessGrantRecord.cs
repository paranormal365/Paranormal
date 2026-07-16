using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record OrganizationAccessGrantRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid AppUserId { get; init; }
    public OrganizationSecurityTable TableName { get; init; }
    public OrganizationSecurityAction Actions { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}