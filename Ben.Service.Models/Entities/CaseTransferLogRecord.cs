using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

public record CaseTransferLogRecord
{
    public Guid Id { get; init; }
    public Guid CaseId { get; init; }
    public Guid FromOrganizationId { get; init; }
    public string? FromOrganizationName { get; init; }
    public Guid ToOrganizationId { get; init; }
    public string? ToOrganizationName { get; init; }
    public Guid ProposedByAppUserId { get; init; }
    public string? ProposedByDisplayName { get; init; }
    public Guid? RespondedByAppUserId { get; init; }
    public CaseTransferStatus Status { get; init; }
    public string? TransferReason { get; init; }
    public string? RejectionReason { get; init; }
    public DateTime DateProposed { get; init; }
    public DateTime? DateResponded { get; init; }
}
