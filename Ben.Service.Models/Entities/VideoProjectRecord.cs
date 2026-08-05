namespace Ben.Service.Models.Entities;

public record VideoProjectRecord
{
    public Guid Id { get; init; }
    public Guid CaseId { get; init; }
    public required string Name { get; init; }
    public required string ProjectJson { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
}

public record VideoProjectRequest
{
    public required string Name { get; init; }
    public required string ProjectJson { get; init; }
}
