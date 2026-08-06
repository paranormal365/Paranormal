namespace Ben.Service.Models.Entities;

/// <summary>
/// A basic-info reference to someone connected to the case who is not a platform user
/// (e.g. a family member with their own experiences). Never returned by any public-facing
/// endpoint — this is how it stays scrubbed if the case is later made public.
/// </summary>
public record CaseRelatedPersonRecord
{
    public Guid Id { get; init; }
    public Guid CaseId { get; init; }
    public required string Name { get; init; }
    public int? Age { get; init; }
    public string? Relationship { get; init; }
    public bool LivesAtProperty { get; init; }
    public string? Notes { get; init; }
    public DateTime DateCreated { get; init; }
}

public record AddRelatedPersonRequest(
    string Name,
    int? Age,
    string? Relationship,
    bool LivesAtProperty,
    string? Notes);
