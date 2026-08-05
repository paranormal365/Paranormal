namespace Ben.Service.Models.Entities;

public record CaseNoteRecord
{
    public Guid Id { get; init; }
    public Guid CaseId { get; init; }
    public Guid AuthorAppUserId { get; init; }
    public string? AuthorDisplayName { get; init; }
    public string? Title { get; init; }
    public required string Body { get; init; }
    public bool IsPinned { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
