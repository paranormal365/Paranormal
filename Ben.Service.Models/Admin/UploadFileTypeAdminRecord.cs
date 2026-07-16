namespace Ben.Service.Models.Admin;

public record UploadFileTypeAdminRecord
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? IconClass { get; init; }
    public string? ColorClass { get; init; }
    public bool IsActive { get; init; }
    public bool IsPublic { get; init; }
    public int SortOrder { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
