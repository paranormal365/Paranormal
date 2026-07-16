namespace Ben.Service.Models.Admin;

public record AppRoleClaimAdminRecord
{
    public int Id { get; init; }
    public Guid RoleId { get; init; }
    public string? ClaimType { get; init; }
    public string? ClaimValue { get; init; }
}
