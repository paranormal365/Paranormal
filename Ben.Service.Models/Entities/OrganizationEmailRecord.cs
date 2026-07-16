namespace Ben.Service.Models.Entities;

public record OrganizationEmailRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid OrganizationEmailTypeId { get; init; }
    public string? DisplayText { get; init; }
    public required string EmailAddress { get; init; }
    public bool IsPublic { get; init; }
    public bool IsHidden { get; init; }
    public bool IsPrimary { get; init; }
    public DateTime? DateValidated { get; init; }
    public bool IsValidated { get; init; }
    public int SortOrder { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
