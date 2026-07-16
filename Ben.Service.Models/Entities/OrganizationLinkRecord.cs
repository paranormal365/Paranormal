namespace Ben.Service.Models.Entities;

public record OrganizationLinkRecord
{
    public Guid Id { get; init; }
    public Guid OrganizationId { get; init; }
    public Guid OrganizationLinkTypeId { get; init; }
    public string? DisplayText { get; init; }
    public required string LinkUrl { get; init; }
    public bool IsPublic { get; init; }
    public bool IsActive { get; init; }
    public bool IsVerifiedApproved { get; init; }
    public DateTime? DateVerifiedApproved { get; init; }
    public Guid? VerifiedApprovedByAppUserId { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
