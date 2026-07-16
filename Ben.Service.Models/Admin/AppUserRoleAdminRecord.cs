namespace Ben.Service.Models.Admin;

public record AppUserRoleAdminRecord
{
    public Guid UserId { get; init; }
    public Guid RoleId { get; init; }
}
