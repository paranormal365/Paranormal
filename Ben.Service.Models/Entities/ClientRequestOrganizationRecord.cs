using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record ClientRequestOrganizationRecord
{
    public Guid Id { get; init; }
    public Guid ClientRequestId { get; init; }
    public Guid OrganizationId { get; init; }
    public string? OrganizationName { get; init; }
    public ClientOrgRequestStatus Status { get; init; }
    public DateTime DateApplied { get; init; }
    public DateTime? DateResponded { get; init; }
    public Guid? RespondedByAppUserId { get; init; }
    public DateTime DateCreated { get; init; }
    public DateTime? DateUpdated { get; init; }
    public Guid CreatedByAppUserId { get; init; }
    public Guid? UpdatedByAppUserId { get; init; }
}
