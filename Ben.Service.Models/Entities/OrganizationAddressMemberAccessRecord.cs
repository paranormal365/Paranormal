namespace Ben.Service.Models.Entities;

public record OrganizationAddressMemberAccessRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationAddressId { get; init; }
    public Guid OrganizationUserMembershipId { get; init; }
    public DateTime DateCreated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
}
