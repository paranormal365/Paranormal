namespace Ben.Service.Models.Entities;

public record OrganizationRoleMembershipRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationRoleId { get; init; }
    public Guid OrganizationUserMembershipId { get; init; }
    public DateTime DateCreated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
}
