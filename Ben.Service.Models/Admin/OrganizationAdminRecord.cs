namespace Ben.Service.Models.Admin;

public record OrganizationAdminRecord
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string UrlName { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
