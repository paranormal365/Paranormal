namespace Ben.Service.Models.Identity;

public record IdentityRoleClaimRecord
{
    public int Id { get; init; }
    public Guid RoleId { get; init; }
    public string? ClaimType { get; init; }
    public string? ClaimValue { get; init; }
}
