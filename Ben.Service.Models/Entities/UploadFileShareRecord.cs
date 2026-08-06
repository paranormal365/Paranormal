using Ben.Data.Common.Enums;

namespace Ben.Service.Models.Entities;

/// <summary>A single share grant on an UploadFile — one of person/investigation-team/organization/public.</summary>
public record UploadFileShareRecord
{
    public Guid Id { get; init; }
    public Guid UploadFileId { get; init; }
    public ShareTargetType TargetType { get; init; }
    public Guid? TargetAppUserId { get; init; }
    public Guid? TargetInvestigationId { get; init; }
    public Guid? TargetOrganizationId { get; init; }
    public Guid SharedByAppUserId { get; init; }
    public DateTime DateCreated { get; init; }
}

public record CreateShareRequest(
    ShareTargetType TargetType,
    Guid? TargetAppUserId,
    Guid? TargetInvestigationId,
    Guid? TargetOrganizationId);
