namespace Ben.Service.Models.Admin;

public record AppUserClaimAdminRecord
{
    public int Id { get; init; }
    public Guid UserId { get; init; }
    public string? ClaimType { get; init; }
    public string? ClaimValue { get; init; }
}
