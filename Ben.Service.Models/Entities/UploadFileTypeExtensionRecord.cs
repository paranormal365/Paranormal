namespace Ben.Service.Models.Entities;

public record UploadFileTypeExtensionRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileTypeId { get; init; }
    public required string Pattern { get; init; }
    public DateTime DateCreated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
}
