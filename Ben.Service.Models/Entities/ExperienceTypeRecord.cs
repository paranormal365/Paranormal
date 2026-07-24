namespace Ben.Service.Models.Entities;

public record ExperienceTypeRecord
{
    public Guid Id { get; init; }
    public Guid ExperienceCategoryId { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? IconClass { get; init; }
    public int SortOrder { get; init; }
    public bool IsActive { get; init; }
    public bool IsApproved { get; init; }
    public Guid? ProposedByOrganizationId { get; init; }
    public Guid? ApprovedByAppUserId { get; init; }
    public DateTime? DateApproved { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
