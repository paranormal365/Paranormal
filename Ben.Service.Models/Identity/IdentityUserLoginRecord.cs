namespace Ben.Service.Models.Identity;

public record IdentityUserLoginRecord
{
    public string LoginProvider { get; init; } = string.Empty;
    public string ProviderKey { get; init; } = string.Empty;
    public string? ProviderDisplayName { get; init; }
    public Guid UserId { get; init; }
}
