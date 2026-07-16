namespace Ben.Service.Models.Identity;

public record IdentityRoleRecord
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public string? NormalizedName { get; init; }
}
