namespace Ben.Service.Models.Entities;

public record VideoProjectRecord
{
    public Guid Id { get; init; }
    public Guid? CaseId { get; init; }
    public required string Name { get; init; }
    public required string ProjectJson { get; init; }
    public Guid? PublishedUploadFileId { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }

    /// <summary>
    /// Who made it, for a list that shows more than one person's work.
    /// </summary>
    /// <remarks>
    /// A case's projects are visible to everyone who can reach the case, so "By" is a real column
    /// rather than a row that always says You (2026-09-05 audit, persistence-14 and site-7). Null
    /// where the caller sees only their own projects, and nothing needs saying.
    /// </remarks>
    public string? CreatedByName { get; init; }
}

public record VideoProjectRequest
{
    public required string Name { get; init; }
    public required string ProjectJson { get; init; }
}
