namespace Ben.Service.Models.Identity;

public record IdentityUserClaimRecord
{
    public int Id { get; init; }
    public Guid UserId { get; init; }
    public string? ClaimType { get; init; }
    public string? ClaimValue { get; init; }
}
