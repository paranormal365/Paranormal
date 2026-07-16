namespace Ben.Service.Models.Admin;

public record AppRoleAdminRecord
{
    public Guid Id { get; init; }
    public string? Name { get; init; }
    public string? NormalizedName { get; init; }
}
