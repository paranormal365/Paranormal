namespace Ben.Service.Models.Entities;

/// <summary>
/// Read-only snapshot returned when querying the organization file delete audit log.
/// </summary>
public record OrganizationFileDeleteLogRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public required string OrganizationName { get; init; }
    public Guid OriginalFileId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long FileSize { get; init; }
    public string? StoragePath { get; init; }
    public Guid? SourceUploadFileId { get; init; }
    public bool WasPublic { get; init; }
    public string? WasPublishedByDisplayName { get; init; }
    public DateTime? WasDatePublished { get; init; }
    public Guid DeletedByAppUserId { get; init; }
    public required string DeletedByDisplayName { get; init; }
    public DateTime DateDeleted { get; init; }
}
