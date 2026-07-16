using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record OrganizationUserMembershipRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid AppUserId { get; init; }
    public OrganizationMemberRole Role { get; init; }
    public bool IsActive { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}