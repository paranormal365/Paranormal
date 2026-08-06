namespace Ben.Service.Models.Entities;

/// <summary>A file linked to a case's general Files/Evidence tab.</summary>
public record CaseFileRecord
{
    public Guid Id { get; init; }
    public Guid CaseId { get; init; }
    public Guid UploadFileId { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public long FileSize { get; init; }
    public string? Description { get; init; }
    public DateTime DateCreated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
}
