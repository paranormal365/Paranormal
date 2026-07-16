namespace Ben.Service.Models.Admin;

public record AppUserTokenAdminRecord
{
    public Guid UserId { get; init; }
    public string LoginProvider { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}
