namespace Ben.Service.Models.Identity;

public record IdentityUserTokenRecord
{
    public Guid UserId { get; init; }
    public string LoginProvider { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}
