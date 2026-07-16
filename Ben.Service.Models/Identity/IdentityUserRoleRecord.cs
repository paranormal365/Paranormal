namespace Ben.Service.Models.Identity;

public record IdentityUserRoleRecord
{
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
}
