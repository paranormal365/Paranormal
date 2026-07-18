namespace Ben.Service.Models.Entities;

public record OrgMemberGroupMembershipRecord
{
    public Guid Id { get; init; }
    public Guid OrgMemberGroupId { get; init; }
    public Guid OrganizationUserMembershipId { get; init; }
    public DateTime DateCreated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
}
