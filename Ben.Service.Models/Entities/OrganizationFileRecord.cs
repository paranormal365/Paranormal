namespace Ben.Service.Models.Entities;

public record OrganizationFileRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid UploadFileTypeId { get; init; }
    public required string FileTypeName { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long FileSize { get; init; }
    public string? Description { get; init; }
    public bool IsPublic { get; init; }
    public int SortOrder { get; init; }
    /// <summary>Source user file ID when this was copied from a user's UploadFile.</summary>
    public Guid? SourceUploadFileId { get; init; }
    /// <summary>Display name of who approved this file for public access. Null if not yet published.</summary>
    public string? PublishedByDisplayName { get; init; }
    /// <summary>UTC timestamp when this file was approved for public access.</summary>
    public DateTime? DatePublished { get; init; }
    public required string CreatedByDisplayName { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
}
